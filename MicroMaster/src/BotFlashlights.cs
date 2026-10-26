using System;
using System.Collections.Generic;
using Player;
using SNetwork;
using LevelGeneration;
using UnityEngine;
namespace PreLuna.MicroMaster
{
    public enum BotFlashlightMode { AI, ControlledOnly, AllBots }

    // Own native lamps independently of the input lease and camera window.
    internal sealed class BotFlashlights
    {
        readonly Dictionary<int, NativeFlashlight> lamps = new();
        PlayerAIBot controlled;
        IntPtr floor, master;
        bool world;
        internal bool WorldValid => world && SNet.IsMaster && master != IntPtr.Zero && SNet.Master?.Pointer == master
            && GameStateManager.CurrentStateName == eGameStateName.InLevel && floor != IntPtr.Zero && Builder.CurrentFloor?.Pointer == floor;
        bool Enabled => Plugin.Enabled.Value && Plugin.NativeLight.Value && Plugin.LightMode.Value != BotFlashlightMode.AI;
        bool Eligible(PlayerAgent agent) => Enabled && WorldValid && agent?.Owner?.IsBot == true
            && !agent.IsBeingDestroyed && !agent.IsBeingDespawned && PlayerManager.Current?.GetPlayerAgentInSlot(agent.PlayerSlotIndex)?.Pointer == agent.Pointer
            && (Plugin.LightMode.Value == BotFlashlightMode.AllBots || controlled?.Agent?.Pointer == agent.Pointer);
        internal NativeFlashlight Find(PlayerAgent agent) => agent != null && lamps.TryGetValue(agent.PlayerSlotIndex, out var lamp) && lamp.Owns(agent) ? lamp : null;
        internal bool Controls(PlayerAgent agent) => Eligible(agent) && Find(agent)?.ParametersReady == true;
        // Switch intent is independent of render readiness and of the control lease.
        internal bool AllowSwitch(PlayerAgent agent, bool enabled, int boundary)
        {
            if (enabled || !Eligible(agent)) return true;
            var lamp = Ensure(agent);
            return lamp == null || lamp.AllowOff(boundary);
        }
        internal IDisposable BeginLightHandoff(PlayerAgent agent) => Eligible(agent) ? Ensure(agent)?.BeginHandoff() : null;
        internal bool Owns(Light light) { foreach (var lamp in lamps.Values) if (lamp.Owns(light)) return true; return false; }
        internal bool OwnsItem(PlayerAgent agent) => Find(agent)?.Exclusive == true;
        internal bool MaySync(PlayerAgent agent) => WorldValid && agent?.Owner?.IsBot == true;
        internal void Select(PlayerAIBot bot) { controlled = bot; Tick(Time.unscaledTime); }
        internal void Release() { controlled = null; if (Plugin.LightMode.Value != BotFlashlightMode.AllBots) StopLamps(); }
        internal void Tick(float now)
        {
            if (!WorldValid) {
                StopLamps(); world = false;
                if (!SNet.IsMaster || SNet.Master == null || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor == null) return;
                master = SNet.Master.Pointer; floor = Builder.CurrentFloor.Pointer; world = true;
            }
            if (!Enabled) { StopLamps(); return; }
            for (int slot = 0; slot < 4; slot++) {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (lamps.TryGetValue(slot, out var previous) && (!previous.Owns(agent) || !Eligible(agent))) {
                    // Keep ownership until End has restored switch/layer state.
                    previous.End(); lamps.Remove(slot);
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
            if (lamps.TryGetValue(agent.PlayerSlotIndex, out var stale)) { stale.End(); lamps.Remove(agent.PlayerSlotIndex); }
            var bot = agent.GetComponent<PlayerAIBot>(); if (bot == null) return null;
            lamp = new NativeFlashlight(); lamp.Begin(bot); lamps.Add(agent.PlayerSlotIndex, lamp); return lamp;
        }
        internal void BeforeSetup(PlayerAgent agent, uint id, ItemEquippable item) => Ensure(agent)?.BeforeNativeSetup(agent, id, item);
        internal void AfterSetup(PlayerAgent agent) => Find(agent)?.AfterNativeSetup(agent);
        internal void Filter(Light light, ref float value, int kind) { foreach (var lamp in lamps.Values) lamp.FilterNativeWrite(light, ref value, kind); }
        internal bool AllowData(Light light) { foreach (var lamp in lamps.Values) if (!lamp.AllowNativeUpdate(light)) return false; return true; }
        internal void RenderDisabled(CL_Light value) { foreach (var lamp in lamps.Values) lamp.RenderDisabled(value); }
        internal string Status(PlayerAgent agent) => Find(agent)?.Status ?? "原生手电：AI自动／等待关卡与房主就绪";
        internal void Remove(PlayerAgent agent)
        {
            if (agent != null && lamps.TryGetValue(agent.PlayerSlotIndex, out var lamp) && lamp.Owns(agent)) { lamp.End(); lamps.Remove(agent.PlayerSlotIndex); }
        }
        void StopLamps() { foreach (var lamp in lamps.Values) lamp.End(); lamps.Clear(); }
        internal void Stop() { StopLamps(); controlled = null; world = false; }
    }
}
