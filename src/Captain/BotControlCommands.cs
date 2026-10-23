using System;
namespace PreLuna.Captain.Control
{
    // Positions are supplied by the rendered bot camera. No native game objects cross the API.
    public readonly record struct BotControlRay(float X, float Y, float Z, float DX, float DY, float DZ)
    {
        public bool Finite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z)
            && float.IsFinite(DX) && float.IsFinite(DY) && float.IsFinite(DZ)
            && DX * DX + DY * DY + DZ * DZ is > .9f and < 1.1f;
    }
    public interface IBotControlCommandService
    {
        bool Mark(long ticket, BotControlRay ray, out string reason);
        bool CommandBusy(long ticket, out string label);
        bool CommandOwns(long agent, long owner);
        bool AllowsAction(long agent, long owner, long descriptor);
        bool AllowsTeleport(long agent, long owner);
    }
    public interface IBotControlStationService
    {
        bool RecallProtected(long agent, long owner);
    }
    public static partial class BotControlApi
    {
        public const int CommandApiVersion = 1;
        public const int StationApiVersion = 1;
        static IBotControlCommandService Commands => Available ? service as IBotControlCommandService : null;
        public static bool CommandsAvailable => Commands != null;
        public static bool Mark(long ticket, BotControlRay ray, out string reason)
        {
            reason = "接管标记接口不可用";
            return Commands != null && ray.Finite && Commands.Mark(ticket, ray, out reason);
        }
        public static bool CommandBusy(long ticket, out string label)
        { label = null; return Commands != null && Commands.CommandBusy(ticket, out label); }
        public static bool CommandOwns(long agent, long owner) => Commands?.CommandOwns(agent, owner) == true;
        public static bool AllowsAction(long agent, long owner, long descriptor)
            => descriptor != 0 && Commands?.AllowsAction(agent, owner, descriptor) == true;
        public static bool AllowsTeleport(long agent, long owner) => Commands?.AllowsTeleport(agent, owner) == true;
        public static bool RecallProtected(long agent, long owner)
            => Available && service is IBotControlStationService stations && stations.RecallProtected(agent, owner);
    }
}
