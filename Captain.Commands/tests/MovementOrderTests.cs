using System;
using PreLuna.Captain.Tasks;
sealed class Session : IPositionTaskSession
{
    public bool Valid { get; set; } = true;
    public bool Grant = true, Renewed = true, Execute = true, Outbound, Inbound, Accepted = true, ThrowStop;
    public bool CanExecute => Execute;
    public string Blocked { get; set; }
    public int Acquires, Renews, OutMoves, BackMoves, Stops, Ends, Suspends, Revalidates;
    public bool ResumeValid = true;
    public PositionTaskStage Result;
    public string Reason;
    public bool Acquire() { Acquires++; return Grant; }
    public bool Renew(bool returning) { Renews++; return Renewed; }
    public bool Arrived(bool returning) => returning ? Inbound : Outbound;
    public bool Move(bool returning) { if(returning)BackMoves++;else OutMoves++;return Accepted; }
    public void Stop() { Stops++;if(ThrowStop)throw new Exception("native stop failed"); }
    public void Suspend() { Suspends++;Stop(); }
    public bool Revalidate(bool returning) { Revalidates++;return ResumeValid; }
    public void End(PositionTaskStage stage,string reason) { Ends++;Result=stage;Reason=reason; }
}
static class MovementOrderTests
{
    static int count;
    static void Check(bool ok,string reason) {count++;if(!ok)throw new Exception(reason);}
    static void Main()
    {
        var s=new Session();var r=new PositionTaskRunner(s,0,15);
        r.Tick(0);Check(r.Stage==PositionTaskStage.Moving&&s.Acquires==1&&s.OutMoves==1,"one accepted lease starts outbound");
        r.Tick(1);Check(!r.Finished&&s.OutMoves==2,"native move success is not coordinate arrival");
        s.Outbound=true;r.Tick(2);Check(r.Stage==PositionTaskStage.Returning&&s.Ends==0,"actual arrival keeps lease until return");
        r.Tick(3);Check(s.BackMoves==1&&s.Acquires==1,"return reuses outbound lease");
        s.Inbound=true;r.Tick(4);Check(r.Finished&&s.Ends==1&&s.Result==PositionTaskStage.Complete,"verified return completes exactly once");
        r.Tick(5);Check(s.Ends==1&&s.Acquires==1,"completed runner never restarts");
        s=new Session{Grant=false};r=new(s,0,15);r.Tick(0);r.Tick(16);
        Check(r.Stage==PositionTaskStage.Failed&&s.OutMoves==0&&s.Renews==0,"acquire timeout never moves without ownership");
        s=new Session{Execute=false};r=new(s,0,15);r.Tick(0);r.Tick(60);
        Check(r.Stage==PositionTaskStage.Paused&&s.OutMoves==0&&s.Ends==0,"protected native action pauses movement without spending move timeout");
        s.Execute=true;r.Tick(61);Check(r.Stage==PositionTaskStage.Moving&&s.OutMoves==1&&s.Acquires==1,"pause resumes same lease");
        s=new Session{Execute=false};r=new(s,0,15);r.Tick(0);r.Tick(136);
        Check(r.Finished&&s.Ends==1,"protection cannot block task forever");
        s=new Session();r=new(s,0,15);r.Cancel(false);r.Tick(0);
        Check(r.Stage==PositionTaskStage.Cancelled&&s.Acquires==0&&s.OutMoves==0,"cancel before acquisition never interrupts scan");
        s=new Session();r=new(s,0,15);r.Tick(0);r.Cancel(true);r.Tick(1);
        Check(r.Stage==PositionTaskStage.Returning&&s.BackMoves==1&&s.Acquires==1,"cancel and return retains one lease");
        s.Inbound=true;r.Tick(2);Check(r.Stage==PositionTaskStage.Cancelled&&s.Ends==1,"cancel return ends with cancelled outcome");
        s=new Session{Renewed=false};r=new(s,0,15);r.Tick(0);
        Check(r.Stage==PositionTaskStage.Failed&&s.OutMoves==0&&s.Ends==1,"lost ownership stops before moving");
        s=new Session{Valid=false};r=new(s,0,15);r.Tick(0);
        Check(r.Stage==PositionTaskStage.Failed&&s.Acquires==0,"bot or floor replacement rejected before any request");
        s=new Session{Accepted=false,Blocked="door closed"};r=new(s,0,15);r.Tick(0);
        Check(r.Stage==PositionTaskStage.Failed&&s.Reason=="door closed"&&s.Ends==1,"closed route diagnoses and releases");
        s=new Session();r=new(s,0,15);r.Tick(0);r.Tick(16);
        Check(r.Stage==PositionTaskStage.Failed&&s.Ends==1,"no progress times out instead of indefinite travel");
        s=new Session{Outbound=true,Execute=true};r=new(s,0,15);r.Tick(0);r.Tick(9);
        Check(r.Stage==PositionTaskStage.Complete&&s.Ends==1,"return timeout after real arrival releases to native scan/follow");
        s=new Session{Outbound=true};r=new(s,0,15);r.Tick(0);s.Execute=false;r.Tick(9);
        Check(r.Stage==PositionTaskStage.Complete&&s.Ends==1,"return blocked by rescue still releases without another outgoing request");
        s=new Session{ThrowStop=true};r=new(s,0,15);r.Cancel(false);
        try{r.Tick(0);}catch(Exception){}
        Check(s.Ends==1&&r.Stage==PositionTaskStage.Cancelled,"failed native stop cannot leak ownership cleanup");
        foreach(float dy in new[]{-3f,-1.01f,1.01f,3f,float.NaN})Check(!MovementOrderPolicy.Arrived(0,dy,0),"different floor is not arrival");
        Check(MovementOrderPolicy.Arrived(.6f,.5f,.6f)&&!MovementOrderPolicy.Arrived(1.1f,0,0),"horizontal and vertical arrival tolerances");
        Check(!MovementOrderPolicy.Arrived(float.PositiveInfinity,0,0),"invalid position is not arrival");
        Check(MovementOrderPolicy.Duplicate(91,91)&&!MovementOrderPolicy.Duplicate(91,92)&&!MovementOrderPolicy.Duplicate(0,0),"actual player identity duplicate suppression");
        Check(MovementOrderPolicy.SamePoint(.2f,true)&&!MovementOrderPolicy.SamePoint(.2f,false)&&!MovementOrderPolicy.SamePoint(2,true),"same-looking point in other room is not duplicate");
        s=new Session();r=new(s,0,15);r.Tick(0);r.Pause(true);r.Tick(1);r.Tick(100);
        Check(r.Stage==PositionTaskStage.Paused&&s.Suspends==1&&s.OutMoves==1&&s.Renews==1,"manual pause stops once and releases authority without renewed movement");
        r.Pause(false);r.Tick(101);Check(s.Revalidates==1&&s.Acquires==2&&!r.Finished,"manual resume validates and acquires fresh authority, paused time excluded");
        s=new Session{Outbound=true};r=new(s,0,15);r.Tick(0);r.Pause(true);r.Tick(1);r.Tick(50);r.Pause(false);r.Tick(51);
        Check(r.Stage==PositionTaskStage.Returning&&s.BackMoves==1&&s.Acquires==2,"paused return resumes return instead of repeating outbound");
        s=new Session();r=new(s,0,15);r.Tick(0);r.Pause(true);r.Tick(1);s.ResumeValid=false;r.Pause(false);r.Tick(2);
        Check(r.Stage==PositionTaskStage.Failed&&s.Acquires==1,"invalid resume does not reacquire or move");
        s=new Session();r=new(s,0,15);r.Tick(0);r.Pause(true);r.Tick(1);r.Cancel(false);r.Tick(2);
        Check(r.Stage==PositionTaskStage.Cancelled&&s.Acquires==1,"cancel a saved task without resurrecting lease");
        s=new Session();r=new(s,0,15);r.Pause(true);r.Tick(0);r.Tick(601);
        Check(r.Finished&&s.Acquires==0,"manual pause bounded to ten minutes");
        Console.WriteLine("PASS: "+count+" movement ownership/arrival/pause/cancel/failure assertions; native game not executed.");
    }
}
