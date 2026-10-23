using System;
using PreLuna.Captain.Tasks;
sealed class StationSessionFixture : IStationSession
{
    public bool Valid { get; set; } = true;
    public bool Available { get; set; } = true;
    public bool HasLease { get; set; }
    public bool Arrived { get; set; }
    public bool Grant = true, Renewal = true, Route = true, MoveOK = true, ThrowStop;
    public string Error { get; set; }
    public int Acquires, Renewals, Validations, Moves, Holds, Suspends, Releases, Ends;
    public bool Failed;
    public bool Acquire() { Acquires++;return HasLease=Grant; }
    public bool Renew() { Renewals++;return Renewal; }
    public bool Revalidate() { Validations++;return Route; }
    public bool Move() { Moves++;return MoveOK; }
    public void Hold() { Holds++; }
    public void Suspend() { Suspends++;if(HasLease)Releases++;HasLease=false;if(ThrowStop)throw new Exception("native stop failure"); }
    public void End(bool failed,string reason) { Ends++;Failed=failed; }
}
static class StationTests
{
    static int count;
    static void Check(bool ok,string name) { count++;if(!ok)throw new Exception(name); }
    static void Main()
    {
        var s=new StationSessionFixture();var r=new StationRunner(s,0,15);r.Tick(0);
        Check(r.Stage==StationStage.Moving&&s.Moves==1&&s.Validations==1&&s.Acquires==1,"validate route and acquire one duty lease before moving");
        s.Arrived=true;r.Tick(1);Check(r.Stage==StationStage.Holding&&s.Holds==1&&!r.Finished,"real arrival retains duty, never automatic return");
        r.Tick(100);Check(s.Holds==1&&s.Acquires==1&&s.Moves==1,"holding does not restart movement, face or warp every tick");
        s.Available=false;r.Tick(101);Check(r.Stage==StationStage.Yielded&&s.Releases==1&&!s.HasLease,"scan, rescue and atomic interaction release movement authority");
        r.Tick(102);Check(s.Suspends==1&&s.Moves==1,"same protection does not repeatedly stop actions");
        s.Available=true;r.Tick(104);Check(s.Acquires==1,"wait 2.5 seconds of stable availability after protection");
        r.Tick(105);Check(r.Stage==StationStage.Holding&&s.Acquires==2&&s.Validations==2,"resume duty with fresh validation and fresh lease");
        r.Pause(true);r.Tick(106);r.Tick(150);Check(r.Stage==StationStage.Paused&&s.Acquires==2&&s.Releases==2,"manual pause retains target while authority is released");
        r.Pause(true);r.Tick(151);Check(s.Suspends==2,"duplicate pause doesn't stop or release repeatedly");
        r.Pause(false);r.Tick(152);Check(s.Acquires==3&&s.Validations==3&&r.Stage==StationStage.Holding,"manual continue requires new route and authority");
        s.Arrived=false;r.Tick(153);Check(r.Stage==StationStage.Yielded&&!s.HasLease,"drift beyond radius restarts at safe boundary");
        r.Tick(156);Check(r.Stage==StationStage.Moving&&s.Moves==2&&s.Validations==4,"drift return uses validated route instead of perpetual teleport");
        r.Yield(157);r.Tick(158);Check(!s.HasLease&&s.Acquires==4,"explicit temporary command yields duty immediately");
        s.Available=false;r.Tick(160);s.Available=true;r.Tick(163);Check(s.Acquires==5,"temporary job ends then resumes duty once");
        r.Cancel();r.Tick(164);Check(r.Finished&&!s.HasLease&&s.Ends==1&&!s.Failed,"cancel releases duty once");
        r.Tick(165);Check(s.Ends==1&&s.Acquires==5,"finished duty cannot resurrect");
        s=new(){Available=false};r=new(s,0,15);r.Tick(0);r.Tick(1000);Check(!r.Finished&&s.Acquires==0&&s.Validations==0,"long scan doesn't time out or rebuild route while waiting");
        s=new(){Route=false,Error="door closed"};r=new(s,0,15);r.Tick(0);Check(r.Finished&&s.Failed&&s.Acquires==0&&s.Moves==0,"closed or invalid route fails before movement");
        s=new(){Grant=false};r=new(s,0,15);r.Tick(0);r.Tick(.1f);Check(s.Acquires==1&&s.Moves==0,"rejected lease acquisition throttles retries");
        s=new(){Renewal=false};r=new(s,0,15);r.Tick(0);Check(!s.HasLease&&s.Moves==0&&r.Stage==StationStage.Yielded,"lost lease yields before any native movement");
        s=new(){MoveOK=false,Error="native rejected"};r=new(s,0,15);r.Tick(0);Check(r.Finished&&s.Releases==1,"movement failure ends and releases");
        s=new();r=new(s,0,15);r.Tick(0);r.Tick(16);Check(r.Finished&&s.Moves==1,"approach is bounded, standing itself has no timeout");
        s=new(){Arrived=true};r=new(s,0,15);r.Tick(0);s.Valid=false;r.Tick(1);Check(r.Finished&&s.Ends==1&&s.Releases==1,"bot/floor identity change ends persisted duty");
        s=new();r=new(s,0,15);r.Pause(true);r.Tick(0);r.Tick(601);Check(r.Finished&&s.Acquires==0,"paused duty cannot retain memory forever");
        s=new();r=new(s,0,15);r.Tick(0);s.ThrowStop=true;r.Cancel();try{r.Tick(1);}catch(Exception){}
        Check(s.Ends==1&&r.Finished,"failed native cleanup still ends saved duty");
        Console.WriteLine("PASS: "+count+" station duty/priority/pause/lease/timeout/cleanup assertions; Unity native game not executed.");
    }
}
