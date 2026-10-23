using System;
using System.Collections.Generic;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemActionAuthorization
    {
        sealed class Grant
        {
            public IntPtr Bot, Owner, Item;
            public float Until;
        }
        static readonly Dictionary<IntPtr, Grant> grants = new();
        static readonly List<IntPtr> stale = new();
        static readonly Dictionary<IntPtr, (IntPtr Owner, float Until)> stopping = new();
        static float pruneAt, warningAt;
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction", new[] { typeof(PlayerBotActionBase.Descriptor) }),
                prefix: new HarmonyMethod(typeof(ItemActionAuthorization), nameof(Request)) { priority = Priority.First });
        }
        public static void Command(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (!SNet.IsMaster || bot?.Agent?.Owner?.IsBot != true || desc == null) return;
            var collect = desc.TryCast<PlayerBotActionCollectItem.Descriptor>();
            var item = collect?.TargetItem?.TryCast<ItemInLevel>();
            // Existing commands do not explicitly authorize opening a closed box.
            if (item == null || !AutoPickupAdapter.Open(item)) return;
            grants[desc.Pointer] = new Grant { Bot = bot.Pointer, Owner = bot.Agent.Owner.Pointer, Item = item.Pointer, Until = Time.time + 180 };
        }
        static bool Allowed(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (!SNet.IsMaster || !AutoPickupAdapter.Restrict || desc == null) return true;
            if (OpenTaskAdapter.Authorizes(bot, desc)) return true;
            if (NativePickupAdapter.Authorizes(bot, desc)) return true;
            var unlock = desc.TryCast<PlayerBotActionUnlock.Descriptor>();
            if (unlock?.TargetType == PlayerBotActionUnlock.Descriptor.TargetTypeEnum.Container) return false;
            var highlight = desc.TryCast<PlayerBotActionHighlight.Descriptor>();
            if (highlight?.TargetType == PlayerBotActionHighlight.Descriptor.TargetTypeEnum.Container) return false;
            var collect = desc.TryCast<PlayerBotActionCollectItem.Descriptor>();
            if (collect == null) return true;
            var item = collect.TargetItem?.TryCast<ItemInLevel>();
            return bot?.Agent?.Owner?.IsBot == true && item != null && AutoPickupAdapter.Open(item)
                && grants.TryGetValue(desc.Pointer, out var grant) && grant.Bot == bot.Pointer && grant.Owner == bot.Agent.Owner.Pointer
                && grant.Item == item.Pointer && Time.time <= grant.Until && Time.time >= grant.Until - 180;
        }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, ref bool __result)
        {
            if (Allowed(__instance, __0)) return true;
            __result = false; Warn(__instance, __0, "拒绝未授权请求"); return false;
        }
        static void Warn(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc, string reason)
        {
            if (Time.time < warningAt) return;
            warningAt = Time.time + 5;
            Plugin.Logger.LogInfo("[物资授权] " + reason + " bot=" + bot?.Pointer + " descriptor=" + desc?.Pointer);
        }
        public static void Tick()
        {
            if (!SNet.IsMaster || !AutoPickupAdapter.Restrict) return;
            for (int slot = 0; slot < 4; slot++)
            {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (agent?.Owner?.IsBot != true) continue;
                Quiesce(agent.GetComponent<PlayerAIBot>());
            }
            if (Time.time < pruneAt) return;
            pruneAt = Time.time + 10; stale.Clear();
            foreach (var entry in grants) if (Time.time > entry.Value.Until) stale.Add(entry.Key);
            foreach (var key in stale) grants.Remove(key);
            stale.Clear();
            foreach (var entry in stopping) if (Time.time >= entry.Value.Until) stale.Add(entry.Key);
            foreach (var key in stale) stopping.Remove(key);
        }
        public static void Quiesce(PlayerAIBot bot)
        {
            if (!SNet.IsMaster || !AutoPickupAdapter.Restrict || bot?.Agent?.Owner?.IsBot != true) return;
            var actions = bot.Actions;
            if (actions == null) return;
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var desc = actions[i]?.DescBase;
                if (desc == null || desc.IsTerminated() || Allowed(bot, desc)) continue;
                var owner = bot.Agent.Owner.Pointer;
                if (stopping.TryGetValue(desc.Pointer, out var prior) && prior.Owner == owner
                    && Time.time < prior.Until && Time.time >= prior.Until - .5f) continue;
                if (stopping.Count > 256) stopping.Clear();
                stopping[desc.Pointer] = (owner, Time.time + .5f);
                Warn(bot, desc, "停止未授权物资动作"); bot.StopAction(desc);
                // Consumers still check real IsTerminated/actions; a stop request is not completion.
            }
        }
        public static void Reset() { grants.Clear(); stale.Clear(); stopping.Clear(); pruneAt = warningAt = 0; }
    }
}
