using System;
namespace PreLuna.Captain.Tasks
{
    internal interface IControlCommandPermit
    {
        bool IdentityValid => true;
        bool Acquire(out long token);
        bool Report(ItemTaskStage stage);
        void Hold();
    }
    internal static class ControlCommandPolicy
    {
        public static bool Supported(int handler) => handler is 1 or 10 or 9 or 43 or 55;
        public static string Reject(bool active, bool busy, bool sameTicket, bool sameBot, int handler)
            => !active || !sameTicket ? "接管菜单会话已失效，请重新打开Q"
            : busy ? "接管指令正在执行／核验，不能覆盖或重新标记"
            : !sameBot ? "执行者必须是当前接管机器人"
            : !Supported(handler) ? "当前接管入口未接入此类指令"
            : null;
    }
}
