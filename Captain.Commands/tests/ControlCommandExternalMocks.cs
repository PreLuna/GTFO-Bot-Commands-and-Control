using System;
namespace PreLuna.Captain.Tasks
{
    // Ordinary command regression suites have no active control session.
    internal static class ControlCommandAdapter
    {
        internal static bool Authorizes(Player.PlayerAIBot bot, Player.PlayerBotActionBase.Descriptor desc) => false;
        internal static bool Reserved(IntPtr key) => false;
        internal static bool Teleport(Player.PlayerAIBot bot, Func<bool> operation) => operation();
    }
}
