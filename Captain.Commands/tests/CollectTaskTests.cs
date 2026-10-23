using System;
using System.Collections.Generic;
using System.Linq;
using Player;
using LevelGeneration;
using UnityEngine;
using PreLuna.Captain.Tasks;
using PreLuna.CoopBotsCustom.Commands;
namespace PreLuna.Captain.Tasks
{
    // The menu and world ledger have separate production-source tests. Here we keep
    // the real collection adapter, item session, scheduler and broker reflection bridge.
    internal sealed record CaptainMenuLeaf(int CollectScope, uint CollectType, int CollectSlot, int CollectLimit);
    internal sealed record CaptainSupplyResult(int Id, string Label, string Blocked, uint TypeId, int ItemSlot, string TypeName);
    internal sealed class CaptainSupplyScope { public string Label="房间"; public List<CaptainSupplyResult> Results=new(); }
    internal sealed class KnownSupplySnapshot {
        public List<CaptainSupplyScope> Scopes=new(); public Dictionary<int,ItemInLevel> Targets=new();
        public Dictionary<int,(IntPtr Zone,IntPtr Room)> Regions=new();
    }
    internal static class KnownSupplyIndex {
        public static Dictionary<int,ItemInLevel> CurrentItems=new();
        public static bool Current(int id,ItemInLevel item)=>CurrentItems.TryGetValue(id,out var current)&&current==item;
        public static string PickupReason(PlayerAgent source,ItemInLevel item)=>!ItemRegion.TryResolve(item,out var node)?"物资失效":!ItemRegion.SameSpace(source,node)?"跨层":!AutoPickupAdapter.Open(item)?"柜门关闭":null;
    }
    internal static class ResourceUseAdapter { public static bool Owns(PlayerAIBot bot)=>false; }
    internal static class SingleKillAdapter { public static bool Owns(PlayerAIBot bot)=>false; }
    internal static partial class ItemTaskAdapter {
        public static Dictionary<IntPtr,ItemTaskRunner> Children=new();
        public static HashSet<IntPtr> Reservations=new();
        public static int PickStarts,ReturnStarts;
        public static bool Busy(PlayerAIBot bot)=>CollectTaskAdapter.Owns(bot)||Children.TryGetValue(bot.Pointer,out var task)&&!task.Finished;
        public static bool Reserved(ItemInLevel item)=>Reservations.Contains(item.Pointer);
        public static ItemTaskRunner CurrentTask(PlayerAIBot bot)=>Children.GetValueOrDefault(bot.Pointer);
        public static bool Start(PlayerAIBot bot,ItemTaskKind kind,InventorySlot slot,ItemInLevel target,ContainerBridge destination,string label,
            bool explicitCommand=true,Action<bool> onResult=null,bool collectionChild=false,bool returnAfterOperation=true,ItemTaskLease sharedLease=null) {
            if(Children.TryGetValue(bot.Pointer,out var current)&&!current.Finished || !collectionChild&&CollectTaskAdapter.Owns(bot) || target!=null&&Reserved(target))return false;
            if(target!=null)Reservations.Add(target.Pointer);
            var session=new ItemTaskSession(bot,kind,slot,target,destination,label,()=>{if(target!=null)Reservations.Remove(target.Pointer);},explicitCommand,onResult,sharedLease:sharedLease,stacking:StackTestRuntime.Adapter());
            if(session.Validate()!=null){if(target!=null)Reservations.Remove(target.Pointer);return false;}
            Children[bot.Pointer]=new(kind,session,Time.time,returnAfterOperation);
            if(kind==ItemTaskKind.Pickup)PickStarts++;if(kind==ItemTaskKind.Return)ReturnStarts++;return true;
        }
    }
}
static class CollectTaskTests
{
    static int checks;
    static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    sealed class Fixture {
        public PlayerAIBot Bot=new(); public PlayerBackpack Bag=new(); public KnownSupplySnapshot Snapshot=new();
        public List<ItemInLevel> Items=new();
        public Fixture(int count=3,bool stack=true) {
            CollectTaskAdapter.Reset();ItemTaskAdapter.Children.Clear();ItemTaskAdapter.Reservations.Clear();ItemTaskAdapter.Messages.Clear();
            ItemTaskAdapter.PickStarts=ItemTaskAdapter.ReturnStarts=0;TaskBoard.Journal.Reset();KnownSupplyIndex.CurrentItems.Clear();
            Time.time=1;SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;Builder.CurrentFloor=new();
            StackTestRuntime.Reset();
            BotCommandBroker.AllowAcquire=BotCommandBroker.AllowExecute=BotCommandBroker.AllowTeleport=BotCommandBroker.Lease=true;BotCommandBroker.Resume=false;BotCommandBroker.CloseEnough=false;BotCommandBroker.Releases=BotCommandBroker.Warps=BotCommandBroker.Acquires=0;BotCommandBroker.RejectAfterRelease=false;
            TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=stack;Physics.Blocked=Physics.BodyBlocked=false;Physics.BodyTest=null;Physics.HitCollider=null;
            PlayerBackpackManager.Bags.Clear();PlayerBackpackManager.Worlds.Clear();PlayerBackpackManager.Bags[Bot.Agent.Owner.Pointer]=Bag;
            Bot.SyncValues.Leader.Position=new(100);Snapshot.Scopes.Add(new());
            for(int i=1;i<=count;i++)Add(i);
        }
        public void Add(int id) {
            var item=new ItemInLevel{Data=new(){slot=InventorySlot.ResourcePack,itemID_gearCRC=2},CourseNode=Bot.Agent.CourseNode};item.ItemDataBlock.persistentID=101;item.transform.position=new(id*12);
            item.Sync=new(){Item=item,Custom=new(){ammo=20},State=new(){status=ePickupItemStatus.PlacedInLevel}};
            Items.Add(item);Snapshot.Targets[id]=item;KnownSupplyIndex.CurrentItems[id]=item;Snapshot.Regions[id]=(item.CourseNode.m_zone.Pointer,item.CourseNode.m_area.Pointer);Snapshot.Scopes[0].Results.Add(new(id,"弹药包",null,101,4,"弹药包"));
        }
        public void Issue(int limit=3)=>CollectTaskAdapter.Issue(Bot,Snapshot,new(0,101,4,limit));
        public void Step(){Time.time+=.1f;foreach(var task in ItemTaskAdapter.Children.Values.ToArray())task.Tick(Time.time);CollectTaskAdapter.Tick();}
        public void Finish(){for(int i=0;i<150&&CollectTaskAdapter.Owns(Bot);i++)Step();Check(!CollectTaskAdapter.Owns(Bot),"收集有界结束");}
        public TaskBoardRow Result=>TaskBoard.Journal.Capture(Time.time).Rows.Single(r=>r.Task=="按类型收集");
        public int Attempts=>Items.Sum(i=>i.Sync.Attempts);
        public float Amount=>Bag.AmmoStorage.Counts.GetValueOrDefault(InventorySlot.ResourcePack)?.AmmoInPack??0;
    }
    static void Main() {
        Check(BotTaskBridge.Bind(),"实际反射桥绑定测试仲裁API");
        var borrowed=new Fixture();borrowed.Issue();borrowed.Step();borrowed.Step();
        var holds=new List<BorrowHold>();CollectTaskAdapter.SuspendForBorrow(borrowed.Bot,holds);
        borrowed.Step();Check(holds.Count==1&&holds[0].Yielded&&borrowed.Attempts==0,"实际收集借调等待子任务释放共享租约");
        holds[0].Restore();borrowed.Finish();
        Check(borrowed.Amount==60&&borrowed.Attempts==3&&ItemTaskAdapter.PickStarts==3,"实际借调恢复保留三件候选及计数，未重复拿取");
        borrowed=new Fixture();borrowed.Issue();borrowed.Step();borrowed.Step();holds.Clear();CollectTaskAdapter.SuspendForBorrow(borrowed.Bot,holds);
        CollectTaskAdapter.Control(borrowed.Bot,true);Check(!holds[0].Retained,"玩家重复暂停使旧收集借调记录失效");
        holds[0].Restore();for(int i=0;i<10;i++)borrowed.Step();Check(borrowed.Attempts==0,"收集借调不覆盖玩家后续手动暂停");
        borrowed=new Fixture();borrowed.Issue();CollectTaskAdapter.Control(borrowed.Bot,true);holds.Clear();CollectTaskAdapter.SuspendForBorrow(borrowed.Bot,holds);holds[0].Restore();
        for(int i=0;i<10;i++)borrowed.Step();Check(borrowed.Attempts==0,"借调之前已手动暂停的收集继续保持暂停");
        var pausedBatch=new Fixture();pausedBatch.Issue();pausedBatch.Step();pausedBatch.Step();
        Check(CollectTaskAdapter.Control(pausedBatch.Bot,true),"当前收集可以登记暂停");
        pausedBatch.Step();Time.time=100;pausedBatch.Step();
        Check(pausedBatch.Attempts==0&&ItemTaskAdapter.PickStarts==1&&BotCommandBroker.Releases==1,"暂停移动中的收集只释放租约，不拿下一件");
        Check(CollectTaskAdapter.Control(pausedBatch.Bot,false),"继续原批次，保留顺序");pausedBatch.Finish();
        Check(pausedBatch.Amount==60&&pausedBatch.Attempts==3&&ItemTaskAdapter.PickStarts==3&&BotCommandBroker.Acquires==2,"暂停后逐件核验完整队列，重新获取一次共享租约");
        pausedBatch=new Fixture();pausedBatch.Issue();for(int i=0;i<3;i++)pausedBatch.Step();
        CollectTaskAdapter.Control(pausedBatch.Bot,true);for(int i=0;i<30;i++)pausedBatch.Step();
        Check(pausedBatch.Attempts==1&&ItemTaskAdapter.PickStarts==1&&pausedBatch.Amount==20,"原子交互暂停先完成当前物品，不提交后续候选");
        CollectTaskAdapter.Control(pausedBatch.Bot,false);pausedBatch.Finish();
        Check(pausedBatch.Attempts==3&&pausedBatch.Amount==60&&pausedBatch.Result.Reason.Contains("成功 3/3"),"恢复计数只累计真实成功，已完成子任务不重复拿取");
        var independent=new Fixture(stack:false);StackTestRuntime.Standalone=true;independent.Issue();independent.Finish();
        Check(independent.Amount==60&&independent.Attempts==3&&independent.Result.State==TaskBoardState.Complete,"Core关闭时真实独立补丁观察结果支持逐件收集，总量60");
        var f=new Fixture();f.Issue();Check(CollectTaskAdapter.Owns(f.Bot),"批次登记但不立即伪造拾取成功");f.Issue();
        Check(TaskBoard.Journal.Count==1,"重复提交不覆盖现有收集任务");f.Finish();
        Check(f.Attempts==3&&f.Amount==60,"真实会话逐件合并且总量守恒");
        Check(ItemTaskAdapter.PickStarts==3&&ItemTaskAdapter.ReturnStarts==1,"每件只请求一次拾取，整批只统一返回一次");
        Check(f.Result.State==TaskBoardState.Complete&&f.Result.Reason.Contains("成功 3/3"),"只有同步核验后的三件计入结果");
        Check(ItemTaskAdapter.Reservations.Count==0&&BotCommandBroker.Releases==1,"整批共用控制令牌，最后仅释放一次；所有物品预留清理");
        Check(BotCommandBroker.Acquires==1,"跨多件物品与最终返程不重复获取控制令牌");
        f=new Fixture();BotCommandBroker.RejectAfterRelease=true;BotCommandBroker.Resume=true;BotCommandBroker.ResumeAt=new(200);f.Issue();f.Finish();
        Check(f.Attempts==3&&f.Bot.Agent.Position.x==200&&BotCommandBroker.Releases==1,"扫描暂停上下文保留到整批结束，最后回原扫描位置");
        f=new Fixture(3,false);f.Issue();f.Finish();Check(f.Attempts==1&&f.Amount==20&&f.Result.State==TaskBoardState.Failed,"堆叠关闭，第一件成功后容量检查停止，无偷偷换包");
        Check(f.Result.Reason.Contains("成功 1/3")&&f.Result.Reason.Contains("堆叠"),"提前停止报告实际成功数和容量原因");
        f=new Fixture(5);foreach(var item in f.Items)item.Sync.Custom.ammo=30;f.Issue(5);f.Finish();
        Check(f.Amount==100&&f.Attempts==4&&f.Result.Reason.Contains("成功 4/5"),"达到真实容量停止，部分合并只计算成功世界对象一次");
        float remaining=f.Items.Where(item=>item.Sync.State.status==ePickupItemStatus.PlacedInLevel).Sum(item=>item.Sync.Custom.ammo);
        Check(f.Amount+remaining==150,"整批部分合并不吞掉溢出数量，地面余量加背包守恒");
        f=new Fixture();foreach(var item in f.Items){item.Data.slot=InventorySlot.Consumable;item.Sync.Custom.ammo=2;item.PublicName="荧光棒";}
        for(int i=0;i<f.Snapshot.Scopes[0].Results.Count;i++)f.Snapshot.Scopes[0].Results[i]=f.Snapshot.Scopes[0].Results[i] with {ItemSlot=5,TypeName="荧光棒"};
        f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack]=new(){AmmoInPack=40};
        CollectTaskAdapter.Issue(f.Bot,f.Snapshot,new(0,101,5,3));f.Finish();
        Check(f.Bag.AmmoStorage.Counts[InventorySlot.Consumable].AmmoInPack==6&&f.Amount==40&&f.Attempts==3,"普通消耗品连续拾取，与资源槽独立且保留已有资源数量");
        f=new Fixture();f.Items[0].transform.position=new(120);f.Issue(1);f.Finish();
        Check(f.Items[1].Sync.Attempts==1&&f.Attempts==1,"候选按接单时距机器人近远排序，达到指定配额后停止");
        f=new Fixture();KnownSupplyIndex.CurrentItems.Remove(1);f.Issue();f.Finish();Check(f.Attempts==2&&f.Result.Reason.Contains("跳过 1"),"已被玩家拿走的已知目标跳过，不消耗成功配额");
        f=new Fixture();ItemTaskAdapter.Reservations.Add(f.Items[0].Pointer);f.Issue();f.Finish();Check(f.Items[0].Sync.Attempts==0&&f.Attempts==2,"其他机器人预留目标不争抢");
        f=new Fixture();f.Items[0].CourseNode=new();f.Issue();f.Finish();Check(f.Items[0].Sync.Attempts==0&&f.Result.Reason.Contains("跳过 1"),"目标移出所选房间不会跨范围追拾");
        f=new Fixture();f.Items[0].ItemDataBlock.persistentID=777;f.Issue();f.Finish();Check(f.Items[0].Sync.Attempts==0&&f.Attempts==2,"目标类型变化不会按旧菜单拾取");
        f=new Fixture();f.Snapshot.Scopes[0].Results.Add(f.Snapshot.Scopes[0].Results[0]);f.Issue();f.Finish();Check(f.Attempts==3,"重复来源身份只拾取一次");
        f=new Fixture(2);f.Issue(5);f.Add(3);f.Finish();Check(f.Items[2].Sync.Attempts==0&&f.Attempts==2,"后续SM发现不会扩大已下单候选快照");
        f=new Fixture();f.Items[0].Sync.Accept=false;f.Issue();f.Finish();Check(f.Attempts==1&&f.Amount==0&&f.Result.State==TaskBoardState.Failed,"同步未确认，不拿下一件，不循环重试");
        f=new Fixture();f.Issue();f.Step();while(ItemTaskAdapter.CurrentTask(f.Bot)?.Stage!=ItemTaskStage.Interacting)f.Step();
        CollectTaskAdapter.Cancel(f.Bot,false);f.Finish();Check(f.Attempts==1&&f.Amount==20&&ItemTaskAdapter.ReturnStarts==0&&f.Result.State==TaskBoardState.Cancelled,"原子交互中取消，完成当前一件，不返回不续队列");
        f=new Fixture();f.Issue();f.Step();while(ItemTaskAdapter.CurrentTask(f.Bot)?.Stage!=ItemTaskStage.Interacting)f.Step();
        CollectTaskAdapter.Cancel(f.Bot,true);f.Finish();Check(f.Attempts==1&&ItemTaskAdapter.ReturnStarts==1&&f.Result.State==TaskBoardState.Cancelled,"取消并返回只收尾当前交互然后返回一次");
        f=new Fixture();f.Issue();CollectTaskAdapter.Cancel(f.Bot,false);f.Finish();Check(f.Attempts==0&&ItemTaskAdapter.ReturnStarts==0,"开始前取消无世界交互和无多余返程");
        f=new Fixture(1);f.Issue(1);while(ItemTaskAdapter.ReturnStarts==0)f.Step();CollectTaskAdapter.Cancel(f.Bot,false);f.Finish();Check(f.Result.State==TaskBoardState.Cancelled,"返程中取消也记录取消，不误报失败");
        f=new Fixture();f.Issue();f.Step();var owner=f.Bot.Agent.Owner;f.Bot.Agent.Owner=new();f.Step();Check(!CollectTaskAdapter.Owns(f.Bot)&&f.Attempts==0,"机器人换人后停止旧任务");
        f=new Fixture();f.Issue();f.Step();Builder.CurrentFloor=new();f.Step();Check(!CollectTaskAdapter.Owns(f.Bot)&&f.Attempts==0,"关卡变化后不执行旧队列");
        f=new Fixture();f.Issue();f.Step();BotCommandBroker.AllowExecute=false;Time.time=662;f.Step();Check(!CollectTaskAdapter.Owns(f.Bot)&&f.Result.State==TaskBoardState.Failed,"长期无法恢复的任务总时限清理");
        f=new Fixture();f.Issue();f.Step();BotCommandBroker.AllowExecute=false;f.Step();
        Check(f.Result.State==TaskBoardState.Paused&&f.Attempts==0,"紧急情况暂停整个收集显示，不误报仍在执行");
        BotCommandBroker.AllowExecute=true;f.Finish();Check(f.Attempts==3&&f.Result.State==TaskBoardState.Complete,"紧急情况解除后恢复原队列，不丢任务或重复拾取");
        f=new Fixture();f.Issue();f.Step();CollectTaskAdapter.Reset();Check(!CollectTaskAdapter.HasState&&ItemTaskAdapter.Reservations.Count==0,"重置批次清空预留，不遗留世界引用");
        Console.WriteLine("PASS: "+checks+" 项真实收集适配／子拾取／同步核验断言；原生接口使用模拟，待游戏验收。");
    }
}
