using System;
using System.Collections.Generic;
using Player;
using SNetwork;
using LevelGeneration;
using UnityEngine;
namespace PreLuna.MicroMaster
{
    public enum BotFlashlightMode { AI, ControlledOnly, AllBots }

    // Keep teardown identities until the next level/runtime, even if Unity destroys the runner first.
    internal static class NativeLightLifetime
    {
        static readonly HashSet<(long Agent, int Instance)> retiring = new();
        static readonly HashSet<(long Agent, int Instance)> managed = new();
        internal static void Register((long Agent, int Instance) identity) { managed.Add(identity); retiring.Remove(identity); }
        internal static void Release((long Agent, int Instance) identity) => managed.Remove(identity);
        internal static void Retire((long Agent, int Instance) identity) { managed.Remove(identity); retiring.Add(identity); }
        internal static bool Allows(PlayerAgent agent)
        {
            if (agent == null) return true;
            var key = (agent.Pointer.ToInt64(), agent.GetInstanceID());
            if (retiring.Contains(key)) return false;
            if (managed.Contains(key) && (agent.IsBeingDestroyed || agent.IsBeingDespawned)) { Retire(key); return false; }
            return true;
        }
        internal static void Reset() { retiring.Clear(); managed.Clear(); }
    }

    // Own native lamps independently of the input lease and camera window.
    internal sealed class BotFlashlights
    {
        readonly Dictionary<int, NativeFlashlight> lamps = new();
        PlayerAIBot controlled;
        IntPtr floor, master;
        bool world, closing, quitting;
        internal bool WorldValid => world && !closing && !quitting && SNet.IsMaster && master != IntPtr.Zero && SNet.Master?.Pointer == master
            && GameStateManager.CurrentStateName == eGameStateName.InLevel && floor != IntPtr.Zero && Builder.CurrentFloor?.Pointer == floor;
        bool Enabled => Plugin.Enabled.Value && Plugin.NativeLight.Value && Plugin.LightMode.Value != BotFlashlightMode.AI;
        bool Eligible(PlayerAgent agent) => Enabled && WorldValid && agent?.Owner?.IsBot == true && AllowsSetup(agent)
            && !agent.IsBeingDestroyed && !agent.IsBeingDespawned && PlayerManager.Current?.GetPlayerAgentInSlot(agent.PlayerSlotIndex)?.Pointer == agent.Pointer
            && (Plugin.LightMode.Value == BotFlashlightMode.AllBots || controlled?.Agent?.Pointer == agent.Pointer);
        internal NativeFlashlight Find(PlayerAgent agent) => agent != null && lamps.TryGetValue(agent.PlayerSlotIndex, out var lamp) && lamp.Owns(agent) ? lamp : null;
        internal bool Controls(PlayerAgent agent) => Eligible(agent) && Find(agent)?.ParametersReady == true;
        // Switch intent is independent of render readiness and of the control lease.
        internal bool AllowSwitch(PlayerAgent agent, bool enabled, int boundary)
        {
            if (!AllowsSetup(agent)) return false;
            if (enabled || !Eligible(agent)) return true;
            var lamp = Ensure(agent);
            return lamp == null || lamp.AllowOff(boundary);
        }
        internal bool AllowsSetup(PlayerAgent agent) => NativeLightLifetime.Allows(agent);
        internal IDisposable BeginLightHandoff(PlayerAgent agent) => Eligible(agent) ? Ensure(agent)?.BeginHandoff() : null;
        internal bool Owns(Light light) { foreach (var lamp in lamps.Values) if (lamp.Owns(light)) return true; return false; }
        internal bool OwnsItem(PlayerAgent agent) => Find(agent)?.Exclusive == true;
        internal bool MaySync(PlayerAgent agent) => WorldValid && agent?.Owner?.IsBot == true;
        internal void Select(PlayerAIBot bot) { controlled = bot; Tick(Time.unscaledTime); }
        internal void Release() { controlled = null; if (Plugin.LightMode.Value != BotFlashlightMode.AllBots) StopLamps(); }
        internal void Tick(float now)
        {
            if (closing || quitting) return;
            if (!WorldValid) {
                StopLamps(restoreSwitch: false); world = false;
                if (!SNet.IsMaster || SNet.Master == null || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor == null) return;
                NativeLightLifetime.Reset(); // A valid new world/host owns these live identities again.
                master = SNet.Master.Pointer; floor = Builder.CurrentFloor.Pointer; world = true;
            }
            if (!Enabled) { StopLamps(); return; }
            for (int slot = 0; slot < 4; slot++) {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (lamps.TryGetValue(slot, out var previous) && (!previous.Owns(agent) || !Eligible(agent))) {
                    // A replaced/despawned object must not initialize another lamp while being removed.
                    EndLamp(previous, previous.Owns(agent) && WorldValid); lamps.Remove(slot);
                }
                if (!Eligible(agent)) continue;
                var lamp = Ensure(agent); lamp?.Tick(now);
            }
        }
        NativeFlashlight Ensure(PlayerAgent agent)
        {
            if (!Eligible(agent)) return null;
            var lamp = Find(agent);
            if (lamp != null) return lamp;
            if (lamps.TryGetValue(agent.PlayerSlotIndex, out var stale)) { EndLamp(stale, false); lamps.Remove(agent.PlayerSlotIndex); }
            var bot = agent.GetComponent<PlayerAIBot>(); if (bot == null) return null;
            lamp = new NativeFlashlight(); lamp.Begin(bot); NativeLightLifetime.Register(lamp.Identity); lamps.Add(agent.PlayerSlotIndex, lamp); return lamp;
        }
        internal void BeforeSetup(PlayerAgent agent, uint id, ItemEquippable item) => Ensure(agent)?.BeforeNativeSetup(agent, id, item);
        internal void AfterSetup(PlayerAgent agent) => Find(agent)?.AfterNativeSetup(agent);
        internal void Filter(Light light, ref float value, int kind) { foreach (var lamp in lamps.Values) lamp.FilterNativeWrite(light, ref value, kind); }
        internal bool AllowData(Light light) { foreach (var lamp in lamps.Values) if (!lamp.AllowNativeUpdate(light)) return false; return true; }
        internal void RenderDisabled(CL_Light value) { foreach (var lamp in lamps.Values) lamp.RenderDisabled(value); }
        internal string Status(PlayerAgent agent) => Find(agent)?.Status ?? "原生手电：AI自动／等待关卡与房主就绪";
        internal void Remove(PlayerAgent agent)
        {
            if (agent != null && lamps.TryGetValue(agent.PlayerSlotIndex, out var lamp) && lamp.Identity == (agent.Pointer.ToInt64(), agent.GetInstanceID())) { EndLamp(lamp, false); lamps.Remove(agent.PlayerSlotIndex); }
        }
        void EndLamp(NativeFlashlight lamp, bool restoreSwitch)
        {
            if (!restoreSwitch) NativeLightLifetime.Retire(lamp.Identity);
            try { lamp.End(restoreSwitch); }
            finally { if (restoreSwitch) NativeLightLifetime.Release(lamp.Identity); }
        }
        void StopLamps(bool restoreSwitch = true)
        {
            bool restore = restoreSwitch && WorldValid;
            foreach (var lamp in lamps.Values) EndLamp(lamp, restore);
            lamps.Clear();
        }
        internal void BeginTeardown(bool applicationQuit)
        {
            quitting |= applicationQuit; closing = true;
            StopLamps(restoreSwitch: false); controlled = null; world = false;
        }
        internal void EnterLevel()
        {
            if (quitting || WorldValid) return;
            closing = false; NativeLightLifetime.Reset(); world = false;
        }
        internal void Stop() { StopLamps(); controlled = null; world = false; }
    }
}
