using System;
using System.Collections.Generic;
using PreLuna.Captain.Tasks;
sealed class RescueFake : IRescueSession
{
    public bool Valid { get; set; } = true;
    public bool Downed { get; set; } = true;
    public bool Recovered { get; set; }
    public bool Interacting { get; set; }
    public bool Running { get; set; }
    public bool Available = true, Accept = true;
    public int Requests, Checks, Ends;
    public RescueStage Result;
    public bool Ready(out string reason) { Checks++; reason = Available ? null : "atomic action"; return Available; }
    public bool Begin() { Requests++; if (Accept) Running = true; return Accept; }
    public void Finish(RescueStage result, string reason) { Ends++; Result = result; }
}
static class RescueTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Main()
    {
        var s = new RescueFake {Available=false}; var r = new RescueRunner(s,0);
        for(int i=0;i<10;i++) r.Tick(i*.1f);
        Check(s.Requests==0&&s.Checks==1&&r.Stage==RescueStage.Waiting,"protected atomic work finishes before rescue; readiness throttled");
        s.Available=true; r.Tick(1); Check(s.Requests==1&&r.Stage==RescueStage.Approaching,"one accepted native rescue request");
        for(int i=2;i<60;i++) r.Tick(i*.1f);
        Check(s.Requests==1&&s.Checks==2,"no repeated cancellation or re-request while native rescue runs");
        s.Interacting=true;r.Tick(6);Check(r.Stage==RescueStage.Reviving,"native interaction state, not fabricated progress");
        s.Recovered=true;r.Tick(7);r.Tick(8);Check(r.Stage==RescueStage.Complete&&s.Ends==1,"actual recovered teammate confirms completion exactly once");
        s=new(){Accept=false};r=new(s,0);r.Tick(0);r.Tick(1);r.Tick(2);r.Tick(3);
        Check(r.Stage==RescueStage.Failed&&s.Requests==3&&s.Ends==1,"rejected requests bounded to three");
        s=new(){Available=false};r=new(s,0);r.Tick(0);r.Tick(90);Check(r.Stage==RescueStage.Failed&&s.Requests==0,"waiting for protected work has finite deadline");
        s=new();r=new(s,0);r.Cancel();r.Tick(0);Check(r.Stage==RescueStage.Cancelled&&s.Requests==0,"cancel queued rescue before any action");
        s=new();r=new(s,0);r.Tick(0);r.Cancel();r.Tick(1);Check(r.Stage==RescueStage.Cancelled,"approach cancellation releases reservation");
        s=new();r=new(s,0);r.Tick(0);s.Interacting=true;r.Cancel();r.Tick(1);r.Tick(12);
        Check(!r.Finished,"already started timed interaction allowed to finish");r.Tick(13);Check(r.Stage==RescueStage.Cancelled,"cancel cleanup bounded to twelve seconds");
        s=new();r=new(s,0);r.Tick(0);s.Interacting=true;r.Cancel();r.Tick(1);s.Recovered=true;r.Tick(2);
        Check(r.Stage==RescueStage.Complete,"cancel cannot erase a successful actual revival");
        s=new();r=new(s,0);r.Tick(0);s.Valid=false;r.Tick(1);Check(r.Stage==RescueStage.Failed,"floor / owner / agent identity change ends action");
        s=new();r=new(s,0);r.Tick(0);s.Downed=false;r.Tick(1);Check(r.Stage==RescueStage.Failed,"ineligible state is not falsely considered rescued");
        s=new();r=new(s,0);r.Tick(0);s.Running=false;r.Tick(1);Check(!r.Finished,"native termination gets sync verification grace");
        s.Recovered=true;r.Tick(2);Check(r.Stage==RescueStage.Complete,"delayed health/locomotion sync accepted during grace");
        s=new();r=new(s,0);r.Tick(0);s.Running=false;r.Tick(1);r.Tick(3);Check(r.Stage==RescueStage.Failed&&s.Requests==1,"terminated but unrescued target fails without automatic repeat");
        var match=RescueAllocation.Match(new[]{new RescueEdge(0,2,1),new RescueEdge(0,3,2),new RescueEdge(1,2,2),new RescueEdge(1,3,100)});
        Check(match.Count==2&&match.Exists(e=>e.Bot==0&&e.Target==3)&&match.Exists(e=>e.Bot==1&&e.Target==2),"minimum total distance, not greedy nearest assignment");
        Check(RescueAllocation.Match(new[]{new RescueEdge(0,2,100),new RescueEdge(0,3,1),new RescueEdge(1,3,2)}).Count==2,"coverage prioritised ahead of travel cost");
        Check(RescueAllocation.Match(new[]{new RescueEdge(0,0,0),new RescueEdge(1,2,float.NaN),new RescueEdge(2,3,-1)}).Count==0,"self / invalid edge rejected");
        for(int mask=0;mask<512;mask++) {
            var edges=new List<RescueEdge>();for(int b=0;b<3;b++)for(int t=0;t<3;t++)if((mask&(1<<(b*3+t)))!=0)edges.Add(new(b,t+4,b+t+1));
            match=RescueAllocation.Match(edges);var bots=new HashSet<int>();var targets=new HashSet<int>();
            foreach(var e in match)Check(bots.Add(e.Bot)&&targets.Add(e.Target)&&edges.Contains(e),"one-to-one, valid edges only");
            int optimum=0;float cost=float.PositiveInfinity;
            void Brute(int bot,HashSet<int> taken,int count,float sum) {
                if(bot==3){if(count>optimum||count==optimum&&sum<cost){optimum=count;cost=sum;}return;}
                Brute(bot+1,taken,count,sum);
                foreach(var e in edges)if(e.Bot==bot&&taken.Add(e.Target)){Brute(bot+1,taken,count+1,sum+e.Cost);taken.Remove(e.Target);}
            }
            Brute(0,new(),0,0);float total=0;foreach(var e in match)total+=e.Cost;
            Check(match.Count==optimum&&total==cost,"all 512 assignment graphs achieve maximum matching and minimum cost");
        }
        Console.WriteLine("PASS rescue policy and allocation "+checks+" assertions; no native game calls.");
    }
}
