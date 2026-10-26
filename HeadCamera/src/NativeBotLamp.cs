using System;
using CullingSystem;
using Player;
using UnityEngine;
namespace PreLuna.HeadCamera
{
    public readonly record struct NativeLampCheck(bool SourceReady, bool DesiredOn, bool RenderReady, string Reason, string Details);

    // Read native state; this is not a GPU readback or a claim about rendered pixels.
    public static class NativeBotLamp
    {
        internal static bool Resolve(PlayerAgent agent, out PlayerInventoryBase inventory, out C_LightDynamic culler, out CL_SpotLight spot, out string reason)
        {
            inventory = null; culler = null; spot = null;
            if (agent?.Owner?.IsBot != true) { reason = "目标不是机器人"; return false; }
            inventory = agent.GetComponent<PlayerAIBot>()?.Inventory;
            if (inventory == null) { reason = "机器人背包缺失"; return false; }
            culler = inventory.m_flashlightCLight;
            if (culler == null) { reason = "原生灯裁剪器缺失"; return false; }
            if (culler.m_clusterLight == null) { reason = "原生聚类灯缺失"; return false; }
            spot = culler.m_clusterLight.TryCast<CL_SpotLight>();
            if (spot == null) { reason = "原生聚类灯不是聚光灯"; return false; }
            var source = inventory.m_flashlight; var lamp = spot.UnityLight;
            if (source == null || lamp == null) { reason = "原生灯源缺失"; return false; }
            if (source.Pointer != lamp.Pointer || culler.m_unityLight?.Pointer != source.Pointer) { reason = "背包与渲染灯源不一致"; return false; }
            if (!Valid(lamp)) { reason = "灯源参数无效"; return false; }
            if (spot.ShadowGenerator == null) { reason = "原生阴影生成器缺失"; return false; }
            reason = null; return true;
        }
        internal static bool Valid(Light lamp)
        {
            if (lamp == null) return false;
            float range = lamp.range, intensity = lamp.intensity, angle = lamp.spotAngle;
            var color = lamp.color;
            return float.IsFinite(range) && range > 0 && float.IsFinite(range * range) && float.IsFinite(1f / (range * range))
                && float.IsFinite(intensity) && intensity > 0 && float.IsFinite(angle) && angle >= 1 && angle < 180
                && float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b)
                && color.r >= 0 && color.g >= 0 && color.b >= 0 && Math.Max(color.r, Math.Max(color.g, color.b)) > 0
                && float.IsFinite(color.r * intensity) && float.IsFinite(color.g * intensity) && float.IsFinite(color.b * intensity);
        }
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        public static NativeLampCheck Inspect(PlayerAgent agent, bool includeDetails = false)
        {
            try {
                bool source = Resolve(agent, out var inventory, out var culler, out var spot, out string reason);
                bool desired = inventory?.WantsFlashlightEnabled == true && inventory.FlashlightEnabled;
                bool visible = false;
                var list = C_CullingManager.VisibleSpotLights;
                if (list != null && culler != null) foreach (var item in list) if (item?.Pointer == culler.Pointer) { visible = true; break; }
                bool dataValid = false, allocated = spot?.ShadowGenerator?.Allocated == true;
                if (spot != null) {
                    var data = spot.Data;
                    dataValid = Finite(data.Position) && Finite(data.Irradiance)
                        && Math.Max(data.Irradiance.x, Math.Max(data.Irradiance.y, data.Irradiance.z)) > 0
                        && float.IsFinite(data.InvRangeSqr) && data.InvRangeSqr > 0
                        && float.IsFinite(data.InvNearRange) && data.InvNearRange > 0
                        && float.IsFinite(data.ShadowIndex) && data.ShadowIndex >= 0;
                }
                if (source) reason = !desired ? "未收到开灯指令" : !spot.m_isOn ? "渲染灯已关闭／可能被临时禁用"
                    : !allocated ? "阴影尚未分配" : !dataValid ? "聚类灯数据尚未有效更新"
                    : !culler.IsShown || !visible || !spot.IsActive ? "尚未进入当前相机可见灯集合" : "原生渲染登记就绪";
                bool ready = source && desired && spot.m_isOn && allocated && dataValid && culler.IsShown && visible && spot.IsActive;
                string details = includeDetails ? "reason=" + reason + " agent=" + agent?.Pointer + " source=" + inventory?.m_flashlight?.Pointer
                    + " culler=" + culler?.Pointer + " cluster=" + culler?.m_clusterLight?.Pointer + " renderSource=" + spot?.UnityLight?.Pointer
                    + " wants=" + inventory?.WantsFlashlightEnabled + " enabled=" + inventory?.FlashlightEnabled
                    + " sourceReady=" + source + " renderOn=" + spot?.m_isOn + " allocated=" + allocated + " dataValid=" + dataValid
                    + " shown=" + culler?.IsShown + " visible=" + visible + " active=" + spot?.IsActive + " shadowId=" + spot?.ShadowID : null;
                return new(source, desired, ready, reason, details);
            } catch (Exception e) { return new(false, false, false, "原生灯状态读取异常", e.GetBaseException().Message); }
        }
    }
}
