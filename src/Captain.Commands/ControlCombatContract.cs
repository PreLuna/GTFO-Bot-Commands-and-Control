using Player;
namespace PreLuna.Captain.Tasks
{
    internal interface IControlCombatTask
    {
        bool Finished { get; }
        string Phase { get; }
        void Tick(float now);
        void Cancel();
        void Abort(string reason);
        bool Authorizes(PlayerBotActionBase.Descriptor desc);
    }
}
