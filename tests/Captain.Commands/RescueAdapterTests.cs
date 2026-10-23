using System;
using System.Reflection;
using Player;
using UnityEngine;
using PreLuna.Captain.Tasks;
static class RescueAdapterTests
{
    static int assertions;
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    static object Call(string name,params object[] args)=>typeof(RescueOrders).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static PlayerAgent Make(int slot,bool bot,bool down,float x){
        var agent=new PlayerAgent{PlayerSlotIndex=slot,PlayerName="slot"+slot,Alive=!down,Position=new(x,0,0)};
        agent.Owner.IsBot=bot;agent.Locomotion.m_currentStateEnum=down?PlayerLocomotion.PLOC_State.Downed:PlayerLocomotion.PLOC_State.Stand;
        if(bot){agent.Bot=new(){Agent=agent};var root=new RootPlayerBotAction{m_bot=agent.Bot};agent.Bot.m_rootAction=new(){ActionBase=root};}
        PlayerManager.Current.Slots[slot]=agent;return agent;
    }
    static void Reset(){
        RescueOrders.Reset();PlayerManager.Current=new();LevelGeneration.Builder.CurrentFloor=new();Time.time=0;
        SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
        ItemTaskAdapter.Atomic.Clear();NativePickupAdapter.Busy.Clear();ResourceUseAdapter.Busy.Clear();SingleKillAdapter.Busy.Clear();RouteTraversal.BlockedBots.Clear();
        ItemTaskAdapter.Messages.Clear();ItemTaskAdapter.Suspends=ItemTaskAdapter.Resumes=MovementOrders.Yields=0;TaskBoard.Journal.Reset();
    }
    static PlayerBotActionRevive.Descriptor Desc(PlayerAIBot bot)=>bot.m_rootAction.ActionBase.TryCast<RootPlayerBotAction>().m_reviveAction;
    static void Tick(float time){Time.time=time;RescueOrders.Tick();}
    static void Main(){
        Reset();var target=Make(0,false,true,10);var b=Make(1,true,false,0).Bot;
        RescueOrders.Issue(b,target);RescueOrders.Issue(b,target);Check(RescueOrders.Owns(b),"named assignment owns one bot");
        Tick(0);Check(b.Requests==1&&Desc(b).Client==target,"real adapter selects native root descriptor and Client, one request only");
        Tick(.1f);Check(b.Requests==1,"no repeated requests while approaching");
        Check(!(bool)Call("Allow",b,new PlayerBotActionUseBioscan.Descriptor()),"scanning waits while native rescue runs");
        Check(!(bool)Call("Allow",b,new PlayerBotActionBase.Descriptor{RequiredLayers=PlayerBotActionBase.AccessLayers.Legs}),"follow / station travel cannot replace rescue");
        Check((bool)Call("Allow",b,new PlayerBotActionBase.Descriptor{RequiredLayers=PlayerBotActionBase.AccessLayers.Hands}),"unrelated hand-only actions preserved");
        Check((bool)Call("Allow",b,new PlayerBotActionBase.Descriptor{RequiredLayers=PlayerBotActionBase.AccessLayers.Legs,ParentActionBase=Desc(b).ActionBase}),"native rescue travel child allowed");
        Check(!(bool)Call("BeforeSelection",b.m_rootAction.ActionBase.TryCast<RootPlayerBotAction>(),null,null),"native root cannot silently replace assigned target");
        target.Alive=true;target.Locomotion.m_currentStateEnum=PlayerLocomotion.PLOC_State.Stand;Tick(1);
        Check(!RescueOrders.Owns(b)&&b.Stops==1&&Desc(b).Prio==5,"actual recovery releases reservation and restores native priority");
        Check(ItemTaskAdapter.Suspends==1&&ItemTaskAdapter.Resumes==1,"previous ordinary task restored once");
        Check(TaskBoard.Journal.Capture(1).Rows.Exists(row=>row.Task=="指定救援"&&row.State==TaskBoardState.Complete),"task board records confirmed rescue");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;var other=Make(2,true,false,2).Bot;
        RescueOrders.Issue(b,target);RescueOrders.Issue(other,target);Check(!RescueOrders.Owns(other),"one target cannot be assigned to two bots");
        Check(!(bool)Call("Allow",other,new PlayerBotActionRevive.Descriptor{Client=target}),"native automatic rescue also respects custom target reservation");
        var prior=new PlayerBotActionBase.Descriptor();var conflicting=new PlayerBotActionRevive.Descriptor{Client=target};
        object[] selection={other.m_rootAction.ActionBase.TryCast<RootPlayerBotAction>(),conflicting,prior};Call("AfterSelection",selection);
        Check(ReferenceEquals(selection[1],prior),"filtered rescue preserves previous scan / combat candidate");
        ItemTaskAdapter.Atomic.Add(b.Pointer);Tick(0);Tick(5);Check(b.Requests==0&&MovementOrders.Yields==1,"atomic pickup waits, low-priority duty yielded once");
        ItemTaskAdapter.Atomic.Clear();Tick(6);Check(b.Requests==1,"rescue begins after interaction boundary");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;target.ReviveInteraction.m_interactors.Add(new(){Agent=Make(2,false,false,20)});
        RescueOrders.Issue(b,target);Check(!RescueOrders.Owns(b),"human timed rescue not stolen");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;b.Accept=false;RescueOrders.Issue(b,target);Tick(0);Tick(1);Tick(2);Tick(3);
        Check(b.Requests==3&&!RescueOrders.Owns(b)&&Desc(b).Prio==5,"rejected native actions stop after three and restore original priority");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;RescueOrders.Issue(b,target);Tick(0);
        Desc(b).ActionBase.TryCast<PlayerBotActionRevive>().m_state=PlayerBotActionRevive.State.Revive;RescueOrders.Cancel(b);Tick(1);
        Check(RescueOrders.Owns(b)&&b.Stops==0,"cancel protects started timed revival");Tick(12);Check(!RescueOrders.Owns(b),"atomic cancel deadline bounded");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;RescueOrders.Issue(b,target);Tick(0);
        PlayerManager.Current.Slots[0]=new(){PlayerSlotIndex=0};Tick(1);Check(!RescueOrders.Owns(b),"target slot replacement cannot redirect old rescue");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;RescueOrders.Issue(b,target);Tick(0);LevelGeneration.Builder.CurrentFloor=new();Tick(1);
        Check(!RescueOrders.Owns(b)&&b.Stops==0&&ItemTaskAdapter.Resumes==0,"floor change clears ownership without stale native cleanup or restoring old task");
        Reset();target=Make(0,false,true,20);var second=Make(1,true,true,40);b=Make(2,true,false,0).Bot;other=Make(3,true,false,45).Bot;
        RescueOrders.AssignAll();Tick(0);
        Check(Desc(b).Client==target&&Desc(other).Client==second&&b.Requests==1&&other.Requests==1,"multi-target maximum coverage with distinct shortest-route assignments");
        Check(TaskBoard.Journal.Capture(0).Rows.Exists(row=>row.Task=="多人救援队列"),"waiting targets visible in task board");
        RescueOrders.AssignAll();Tick(.1f);Check(b.Requests==1&&other.Requests==1,"duplicate batch does not reset or restart rescue");
        RescueOrders.CancelBatch();Check(!RescueOrders.HasState,"batch cancellation releases queued and approach assignments");
        Reset();target=Make(0,false,true,20);second=Make(1,true,true,40);b=Make(2,true,false,0).Bot;
        RescueOrders.AssignAll();Tick(0);Check(b.Requests==1,"one available bot assigned once, other target queues");
        target.Alive=true;target.Locomotion.m_currentStateEnum=PlayerLocomotion.PLOC_State.Stand;Tick(1);Tick(1.1f);
        Check(b.Requests==2&&Desc(b).Client==second,"bot becomes available after verified recovery and takes next queued target");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;other=Make(2,true,false,25).Bot;RouteTraversal.BlockedBots.Add(other.Agent.Pointer);
        RescueOrders.AssignAll();Tick(0);Check(b.Requests==1&&other.Requests==0,"closed / unreachable route excludes nearer bot before matching");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;RouteTraversal.BlockedBots.Add(b.Agent.Pointer);
        RescueOrders.AssignAll();Tick(0);Tick(120);Check(!RescueOrders.HasState&&b.Requests==0,"unreachable batch queue expires without endless retry");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;SNetwork.SNet.IsMaster=false;RescueOrders.Issue(b,target);
        Check(!RescueOrders.HasState&&b.Requests==0,"non-host cannot issue rescue");
        Reset();target=Make(0,false,true,20);b=Make(1,true,false,0).Bot;
        Check((bool)Call("Allow",b,new PlayerBotActionRevive.Descriptor{Client=target}),"no captain assignment: original automatic rescue untouched");
        Console.WriteLine("PASS actual RescueOrders adapter "+assertions+" assertions; native game interfaces mocked.");
    }
}
