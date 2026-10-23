using System;
namespace PreLuna.MicroMaster
{
    public readonly record struct MoveIntent(float Right, float Forward)
    {
        public bool Moving => Math.Abs(Right) + Math.Abs(Forward) > .001f;
    }
    public readonly record struct Direction3(float X, float Y, float Z);
    public static class ManualMotionPolicy
    {
        public static MoveIntent Keys(bool w, bool a, bool s, bool d)
        {
            float x = (d ? 1 : 0) - (a ? 1 : 0), z = (w ? 1 : 0) - (s ? 1 : 0);
            float length = MathF.Sqrt(x * x + z * z);
            return length > 1 ? new(x / length, z / length) : new(x, z);
        }
        public static Direction3 World(MoveIntent input, float yaw)
        {
            float r = yaw * MathF.PI / 180, sin = MathF.Sin(r), cos = MathF.Cos(r);
            return new(input.Forward * sin + input.Right * cos, 0, input.Forward * cos - input.Right * sin);
        }
        public static bool InputAllowed(bool controls, bool fps, bool focused, bool chat, bool menu, bool command)
            => controls && fps && focused && !chat && !menu && !command;
    }
    public sealed class ManualLook
    {
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public void Reset(Direction3 direction)
        {
            float flat = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            if (!float.IsFinite(flat) || !float.IsFinite(direction.Y) || flat + Math.Abs(direction.Y) < .001f) { Yaw = Pitch = 0; return; }
            Yaw = MathF.Atan2(direction.X, direction.Z) * 180 / MathF.PI;
            Pitch = Math.Clamp(-MathF.Atan2(direction.Y, flat) * 180 / MathF.PI, -80, 80);
        }
        public void Apply(float x, float y, float sensitivity, bool invert)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(sensitivity)) return;
            sensitivity = Math.Clamp(sensitivity, .05f, 10);
            Yaw = (Yaw + x * sensitivity) % 360;
            Pitch = Math.Clamp(Pitch + y * sensitivity * (invert ? 1 : -1), -80, 80);
        }
        public Direction3 Direction {
            get {
                float yaw = Yaw * MathF.PI / 180, pitch = Pitch * MathF.PI / 180, c = MathF.Cos(pitch);
                return new(MathF.Sin(yaw) * c, -MathF.Sin(pitch), MathF.Cos(yaw) * c);
            }
        }
    }
    public sealed class StableEye
    {
        bool initialized;
        float y, last;
        public void Reset() => initialized = false;
        public float Sample(float groundY, float height, float now)
        {
            float target = groundY + Math.Clamp(height, 1, 1.9f);
            if (!initialized || !float.IsFinite(now) || now < last || Math.Abs(target - y) > 2) { y = target; initialized = true; }
            else y += (target - y) * (1 - MathF.Exp(-Math.Max(0, now - last) / .12f));
            last = now; return Math.Max(groundY + .8f, y);
        }
    }
    public interface IManualWalk
    {
        bool Valid { get; }
        bool TryDirection(Direction3 direction);
        void Stop();
    }
    public sealed class ManualMotionRunner
    {
        readonly IManualWalk walk;
        public bool InputActive { get; private set; }
        public ManualMotionRunner(IManualWalk walk) => this.walk = walk;
        public bool Tick(bool allowed, MoveIntent input, float yaw)
        {
            if (!allowed || !walk.Valid || !input.Moving) { Stop(); return false; }
            InputActive = true;
            try { if (walk.TryDirection(ManualMotionPolicy.World(input, yaw))) return true; }
            catch { Stop(); throw; }
            Stop(); return false;
        }
        public void Stop()
        {
            bool active = InputActive; InputActive = false;
            if (active) walk.Stop();
        }
    }
}
