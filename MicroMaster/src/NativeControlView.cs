using System;
using Player;
using UnityEngine;

namespace PreLuna.MicroMaster
{
    internal static class NativeControlView
    {
        // Read live gameplay data, not the animated remote head or the local body's world position.
        internal static Vector3 Eye(PlayerAgent agent)
        {
            var data = agent?.PlayerData;
            if (data == null || agent.Locomotion == null) throw new InvalidOperationException("机器人原生第一人称视点尚未就绪");
            float crouch = agent.Locomotion.GetCrouch();
            if (!float.IsFinite(crouch)) throw new InvalidOperationException("机器人蹲伏姿态无效");
            var offset = Vector3.Lerp(data.camPosDefault, data.camPosCrouch, Math.Clamp(crouch, 0, 1));
            var eye = agent.transform.TransformPoint(offset);
            if (!float.IsFinite(eye.x) || !float.IsFinite(eye.y) || !float.IsFinite(eye.z)) throw new InvalidOperationException("机器人第一人称眼点无效");
            return eye;
        }
        internal static float FieldOfView()
        {
            var camera = PlayerManager.GetLocalPlayerAgent()?.FPSCamera?.m_camera;
            if (camera == null || !float.IsFinite(camera.fieldOfView) || camera.fieldOfView <= 0 || camera.fieldOfView >= 180)
                throw new InvalidOperationException("本机第一人称视野尚未就绪");
            return camera.fieldOfView;
        }
    }
}
