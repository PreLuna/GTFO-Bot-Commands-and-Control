using System;
using System.Collections.Generic;
using Player;
using UnityEngine;
using PreLuna.HeadCamera;
namespace PreLuna.MicroMaster
{
    // Native inventory initialization owns alignment, culling and GPU updates.
    internal sealed class NativeFlashlight
    {
        readonly List<(Light Source, Transform Layer, bool Active)> layers = new();
        PlayerAIBot bot;
        long agentId, ownerId, itemId, lampId;
        int instanceId;
        uint settingsId = 1;
        bool preparing, restoring, configured, originalWants;
        float retryAt;
        string failure, lastState, lastRenderReason, lastTempReason;
        float nextRenderNotice, lastTempAt, nextTempNotice;
        int tempDisableCount;
        int handoffDepth, rejectedOff;
        float nextOffNotice;
        bool reapply, maintaining;
        internal bool Active { get; private set; }
        internal bool ParametersReady { get; private set; }
        internal bool Exclusive => Active && (ParametersReady || preparing || restoring);
        internal bool RenderReady { get; private set; }
        internal string Status => !ParametersReady ? "原生手电未就绪：" + (failure ?? "等待原生初始化")
            : "原生手电开灯指令维持中；" + NativeBotLamp.Inspect(bot?.Agent).Reason + "，亮度待实测";
        internal bool Owns(PlayerAgent agent) => Active && agent != null && agent.Pointer.ToInt64() == agentId
            && agent.Owner?.Pointer.ToInt64() == ownerId && agent.GetInstanceID() == instanceId
            && agent.Owner.IsBot && !agent.IsBeingDestroyed && !agent.IsBeingDespawned
            && PlayerManager.Current?.GetPlayerAgentInSlot(agent.PlayerSlotIndex)?.GetInstanceID() == instanceId;
        bool IsNative(Light light) => Active && light != null && Owns(bot?.Agent) && bot.Inventory?.m_flashlight?.Pointer == light.Pointer;
        internal bool Owns(Light light)
        {
            if (!Exclusive || light == null || !Owns(bot?.Agent)) return false;
            if (IsNative(light)) return true;
            foreach (var entry in layers) if (entry.Source != null && entry.Source.Pointer == light.Pointer
                || entry.Layer != null && entry.Layer.GetComponent<Light>()?.Pointer == light.Pointer) return true;
            return Owns(light.GetComponentInParent<ItemEquippable>()?.Owner);
        }
        internal void Begin(PlayerAIBot value)
        {
            End(); bot = value;
            agentId = value.Agent.Pointer.ToInt64(); ownerId = value.Agent.Owner.Pointer.ToInt64(); instanceId = value.Agent.GetInstanceID();
            originalWants = value.Inventory?.WantsFlashlightEnabled == true;
            Active = true; ParametersReady = configured = preparing = restoring = false; itemId = lampId = 0; retryAt = 0; failure = lastState = lastRenderReason = lastTempReason = null; nextRenderNotice = nextTempNotice = 0; tempDisableCount = 0;
            handoffDepth = rejectedOff = 0; nextOffNotice = 0; reapply = false;
        }
        internal bool AllowOff(int boundary)
        {
            // Only the local equipment detach may briefly disable its old binding.
            // End() must also be able to restore the original switch state.
            if (restoring || boundary == 2 && handoffDepth > 0) return true;
            rejectedOff++;
            if (Time.unscaledTime >= nextOffNotice) {
                nextOffNotice = Time.unscaledTime + 5;
                Plugin.Instance?.Log.LogInfo("[人机恒亮拒绝关灯] agent=" + agentId + " boundary="
                    + (boundary == 0 ? "Sync" : boundary == 1 ? "Receive" : "Set") + " total=" + rejectedOff
                    + " wants=" + bot.Inventory?.WantsFlashlightEnabled + " enabled=" + bot.Inventory?.FlashlightEnabled
                    + "；拒绝false，不改写成重复网络开灯");
            }
            return false;
        }
        internal IDisposable BeginHandoff() { handoffDepth++; return new Handoff(this); }
        sealed class Handoff : IDisposable
        {
            NativeFlashlight lamp;
            internal Handoff(NativeFlashlight value) { lamp = value; }
            public void Dispose()
            {
                if (lamp == null) return;
                if (lamp.handoffDepth > 0) lamp.handoffDepth--;
                lamp.reapply = true; lamp.retryAt = 0; lamp = null;
            }
        }
        internal void BeforeNativeSetup(PlayerAgent owner, uint id, ItemEquippable item)
        {
            if (restoring || !Owns(owner)) return;
            settingsId = id; preparing = NativeLightSettings.TryGet(id, out _);
            Remember(bot.Inventory?.m_flashlight); Remember(item?.m_lt);
        }
        internal void AfterNativeSetup(PlayerAgent owner)
        {
            if (restoring || !Owns(owner)) return;
            ParametersReady = bot.Inventory?.m_flashlight != null && LampParameters.Read(bot.Inventory.m_flashlight).Valid;
            preparing = false;
            // Defer until the native binding stack has unwound; Receive can call
            // PrepareHelmetFlashlight again, so never re-enter it from this callback.
            reapply = true; retryAt = 0;
            if (!ParametersReady) Warn("原生初始化未产生有效灯参数 ID=" + settingsId);
        }
        internal void FilterNativeWrite(Light light, ref float value, int kind)
        {
            if (!IsNative(light) || !NativeLightSettings.TryGet(settingsId, out var source)) return;
            bool invalid = !float.IsFinite(value) || value <= 0 || kind == 1 && value >= 180;
            if (preparing || invalid) value = kind == 0 ? source.Range : kind == 1 ? source.Angle : source.Intensity;
        }
        internal bool AllowNativeUpdate(Light light)
        {
            if (!IsNative(light) || LampParameters.Read(light).Valid) return true;
            ParametersReady = configured = false; Warn("拒绝无效灯数据；等待原生装备初始化，不在GPU入口回写");
            return false;
        }
        internal void Tick(float now)
        {
            if (!Active) return;
            try {
                RefreshRenderState(now);
                if (handoffDepth > 0 || now < retryAt) return;
                maintaining = true;
                retryAt = now + .5f;
                if (!Owns(bot?.Agent)) return;
                var inventory = bot.Inventory;
                if (inventory == null || bot.Agent.Sync == null) throw new InvalidOperationException("背包／同步接口尚未就绪");
                var item = inventory.WieldedItem; var light = inventory.m_flashlight;
                uint id = item?.HasFlashlight == true && item.AttachedFlashlight != null ? item.AttachedFlashlight.m_settingsID : 1;
                bool changed = !configured || !ParametersReady || itemId != (item?.Pointer.ToInt64() ?? 0) || lampId != (light?.Pointer.ToInt64() ?? 0) || settingsId != id;
                settingsId = id;
                if (!NativeLightSettings.TryGet(id, out _)) { ParametersReady = false; RestoreLayers(); Warn("缺少清零前参数 ID=" + id + "，保留原灯组"); return; }
                if (light == null || inventory.m_flashlightCLight?.m_clusterLight == null) throw new InvalidOperationException("原生灯接口尚未就绪");
                if (changed) {
                    preparing = true; Remember(light); Remember(item?.m_lt);
                    if (item?.HasFlashlight == true && item.AttachedFlashlight != null) inventory.OnItemEquippableFlashlightWielded(item, item.AttachedFlashlight);
                    else inventory.PrepareHelmetFlashlight();
                    AfterNativeSetup(bot.Agent);
                }
                if (!LampParameters.Read(light).Valid) throw new InvalidOperationException("原生初始化参数无效");
                ParametersReady = true; failure = null;
                itemId = item?.Pointer.ToInt64() ?? 0; lampId = light.Pointer.ToInt64(); configured = true;
                if (!inventory.FlashlightEnabled || !inventory.WantsFlashlightEnabled) bot.Agent.Sync.WantsToSetFlashlightEnabled(true, false);
                else if (changed || reapply || !inventory.m_flashlightCLight.m_clusterLight.m_isOn) inventory.ReceiveSetFlashlightStatus(true, false);
                reapply = false; retryAt = now + .5f;
                RefreshRenderState(now);
                string state = "agent=" + agentId + " settings=" + id + " wants=" + inventory.WantsFlashlightEnabled + " enabled=" + inventory.FlashlightEnabled
                    + " clusterOn=" + inventory.m_flashlightCLight.m_clusterLight.m_isOn + " intensity=" + light.intensity + " range=" + light.range
                    + "；仅原生初始化与开关同步，无手动GPU刷新；照明待游戏确认";
                if (state != lastState) { lastState = state; Plugin.Instance?.Log.LogInfo("[原生接管手电] " + state); }
            } catch (Exception error) { ParametersReady = configured = false; RestoreLayers(); Warn(error.GetBaseException().Message); }
            finally { preparing = false; if (maintaining) retryAt = now + .5f; maintaining = false; }
        }
        internal void RenderDisabled(CL_Light value)
        {
            if (!Active || value?.Pointer != bot?.Inventory?.m_flashlightCLight?.m_clusterLight?.Pointer) return;
            lastTempReason = "原生阴影流程调用TempDisableLight"; lastTempAt = Time.unscaledTime; tempDisableCount++;
            if (Time.unscaledTime >= nextTempNotice) {
                nextTempNotice = Time.unscaledTime + 5;
                Plugin.Instance?.Log.LogInfo("[原生灯临时禁用] agent=" + agentId + " cluster=" + value.Pointer + " calls=" + tempDisableCount + "；观察原生调用，不拦截阴影处理");
            }
        }
        void RefreshRenderState(float now)
        {
            var check = NativeBotLamp.Inspect(bot?.Agent);
            RenderReady = ParametersReady && check.RenderReady;
            foreach (var entry in layers) if (entry.Layer != null) {
                bool desired = RenderReady ? false : entry.Active;
                if (entry.Layer.gameObject.activeSelf != desired) entry.Layer.gameObject.SetActive(desired);
            }
            string reason = check.Reason + (lastTempReason != null && now - lastTempAt < 8 ? ";" + lastTempReason : "");
            if (reason != lastRenderReason && now >= nextRenderNotice) {
                lastRenderReason = reason; nextRenderNotice = now + 5;
                Plugin.Instance?.Log.LogInfo("[原生灯渲染状态] " + NativeBotLamp.Inspect(bot?.Agent, true).Details + " RFhidden=" + RenderReady + " recent=" + reason);
            }
        }
        void Warn(string reason) { if (reason == failure) return; failure = reason; Plugin.Instance?.Log.LogWarning("[原生接管手电] " + reason + "；不结束接管"); }
        void Remember(Light source)
        {
            if (source == null) return;
            foreach (string name in new[] { "RF_Core", "RF_L1", "RF_L2", "RF_L3", "RF_L4", "RF_L5", "RF_Spill" }) {
                var layer = source.transform.Find(name); if (layer == null) continue;
                if (layers.Exists(entry => entry.Layer.Pointer == layer.Pointer)) continue;
                layers.Add((source, layer, layer.gameObject.activeSelf));
            }
        }
        void RestoreLayers()
        {
            bool previous = restoring; restoring = true;
            try { foreach (var entry in layers) if (entry.Layer != null) {
                try { entry.Layer.gameObject.SetActive(entry.Active); } catch (Exception e) { Plugin.Instance?.Log.LogWarning("[原生接管手电] RF显隐归还失败：" + e.GetBaseException().Message); }
            } } finally { restoring = previous; }
        }
        internal void End()
        {
            if (!Active) return;
            restoring = true; preparing = false;
            try {
                try {
                    if (Owns(bot?.Agent) && bot.Inventory != null && (bot.Inventory.FlashlightEnabled != originalWants || bot.Inventory.WantsFlashlightEnabled != originalWants)) {
                        if (ControlRuntime.Current?.MaySyncNativeLight(bot.Agent) == true) bot.Agent.Sync.WantsToSetFlashlightEnabled(originalWants, false);
                        else bot.Inventory.ReceiveSetFlashlightStatus(originalWants, false);
                    }
                } catch (Exception e) { Plugin.Instance?.Log.LogWarning("[原生接管手电] 原生开关归还失败：" + e.GetBaseException().Message); }
                RestoreLayers();
                // No parameter, transform, culler, cluster or GPU writes on handback.
            } finally { Active = ParametersReady = RenderReady = preparing = restoring = configured = false; layers.Clear(); bot = null; failure = lastState = lastRenderReason = lastTempReason = null; nextRenderNotice = nextTempNotice = 0; tempDisableCount = 0; }
        }
    }
}
