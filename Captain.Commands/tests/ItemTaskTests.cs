using System;
using PreLuna.Captain.Tasks;
using PreLuna.CoopBotsCustom.Commands;
using Player;
using LevelGeneration;
using UnityEngine;
static class ItemTaskTests
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    sealed class Fixture
    {
        public PlayerAIBot Bot=new(); public PlayerBackpack Bag=new(); public ItemInLevel Target,Old; public ContainerBridge Container=new();
        public ItemTaskSession Session; public ItemTaskRunner Runner; public int Cleanup, Results; public bool? Result; bool cleaned;
        public Fixture(ItemTaskKind kind=ItemTaskKind.Pickup,InventorySlot slot=InventorySlot.ResourcePack,uint heldId=0,float held=0,uint targetId=2,float amount=40,bool explicitCommand=true,bool returnAfterOperation=true)
        {
            Time.time=1; SNetwork.SNet.IsMaster=true; GameStateManager.CurrentStateName=eGameStateName.InLevel; Builder.CurrentFloor=new();
            BotCommandBroker.AllowAcquire=BotCommandBroker.AllowExecute=BotCommandBroker.AllowTeleport=BotCommandBroker.Lease=true;
            BotCommandBroker.Releases=BotCommandBroker.Warps=0; ItemTaskAdapter.TeleportEnabled=true; TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=false;
            BotCommandBroker.Resume=false;
            StackTestRuntime.Reset();
            BotCommandBroker.CloseEnough=false;Physics.Blocked=false;
            PlayerBackpackManager.Bags.Clear(); PlayerBackpackManager.Worlds.Clear(); PlayerBackpackManager.Bags[Bot.Agent.Owner.Pointer]=Bag;
            Target=World(slot,targetId,amount,true,10);
            if(heldId!=0){Old=World(slot,heldId,held,false,0); Bag.Held[slot]=new(){Instance=Old};Bag.AmmoStorage.Counts[slot]=new(){AmmoInPack=held};PlayerBackpackManager.Worlds[heldId]=Old;}
            Session=new(Bot,kind,slot,Target,Container,"测试",()=>{if(!cleaned){cleaned=true;Cleanup++;}},explicitCommand,success=>{Results++;Result=success;},stacking:StackTestRuntime.Adapter());
            Runner=new(kind,Session,1,returnAfterOperation);
        }
        public static ItemInLevel World(InventorySlot slot,uint id,float amount,bool placed,float x)
        {
            var world=new ItemInLevel{Data=new(){slot=slot,itemID_gearCRC=id}};world.transform.position=new(x);
            world.Sync=new(){Item=world,Custom=new(){ammo=amount},State=new(){status=placed?ePickupItemStatus.PlacedInLevel:ePickupItemStatus.PickedUp}};return world;
        }
        public void Tick(float time){Time.time=time;Runner.Tick(time);}
        public void Interact(){Tick(1);Tick(1.1f);Check(Runner.Stage==ItemTaskStage.Interacting,"进入受保护交互阶段");}
        public void Confirm(){Tick(1.7f);Tick(1.8f);}
        public void Finish(){Tick(1.9f);Tick(2);}
    }
    static void PauseTests()
    {
        var f=new Fixture();f.Tick(1);f.Runner.Pause(true);f.Tick(1.05f);f.Tick(100);
        Check(f.Runner.ManuallySuspended&&f.Target.Sync.Attempts==0&&f.Cleanup==0&&BotCommandBroker.Releases==1,"移动阶段暂停保存目标，释放移动权限和不提交物品");
        f.Runner.Pause(false);f.Tick(100.1f);f.Tick(100.7f);f.Tick(100.8f);f.Tick(101);f.Tick(101.1f);
        Check(f.Target.Sync.Attempts==1&&f.Runner.Stage==ItemTaskStage.Complete&&f.Cleanup==1,"暂停时间不耗尽移动预算，恢复后仅一次拾取并核验");
        f=new Fixture();f.Interact();f.Runner.Pause(true);f.Confirm();f.Tick(1.9f);
        Check(f.Runner.ManuallySuspended&&f.Target.Sync.Attempts==1&&f.Result==true&&f.Cleanup==1,"交互时暂停先完成数量核验，释放已完成物品预约，再暂停返程");
        f.Runner.Pause(false);f.Tick(2);f.Tick(2.1f);
        Check(f.Target.Sync.Attempts==1&&f.Cleanup==1,"恢复已完成的拾取只返程，不重复消耗或拾取");
        f=new Fixture();f.Tick(1);f.Runner.Pause(true);f.Tick(1.1f);f.Runner.Cancel(false);f.Tick(1.2f);
        Check(f.Runner.Stage==ItemTaskStage.Cancelled&&f.Cleanup==1&&f.Target.Sync.Attempts==0,"暂停后的取消释放物品预约，不重新取得权限");
        f=new Fixture();f.Tick(1);f.Runner.Pause(true);f.Tick(1.1f);f.Target.Sync.State.status=ePickupItemStatus.PickedUp;f.Runner.Pause(false);f.Tick(2);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"继续时重新核验目标已被拿走，不偷换目标");
    }
    static void Main()
    {
        Check(BotTaskBridge.Bind(),"真实反射桥可以绑定 API v1 模拟接口");
        PauseTests();
        DiagnosticTests();
        StandaloneTests();
        LocalCapacityTests();
        var batchChild=new Fixture(returnAfterOperation:false);batchChild.Interact();batchChild.Confirm();
        Check(batchChild.Runner.Stage==ItemTaskStage.Complete&&batchChild.Result==true&&batchChild.Cleanup==1,"批次子拾取核验后直接释放，仅最后统一返程");
        Check(batchChild.Bot.Agent.Position.x!=batchChild.Bot.SyncValues.Leader.Position.x,"子拾取不每件传送回领队");
        batchChild=new Fixture(returnAfterOperation:false);batchChild.Interact();batchChild.Runner.Cancel(false);batchChild.Confirm();
        Check(batchChild.Runner.Stage==ItemTaskStage.Cancelled&&batchChild.Result==true&&batchChild.Target.Sync.Attempts==1,"批次子拾取原子阶段取消仍核验一次，保留已拿物品");
        Check(ItemTaskPolicy.SupportedSlot(4)&&ItemTaskPolicy.SupportedSlot(5)&&!ItemTaskPolicy.SupportedSlot(8)&&!ItemTaskPolicy.SupportedSlot(3),"资源、消耗品、搬运、装备槽分开");
        Check(ItemTaskPolicy.CanPickup(new(4,1,40,0,0,false,100))==null,"空资源槽允许指定拿取");
        Check(ItemTaskPolicy.CanPickup(new(5,2,3,0,0,false,8))==null,"空消耗品槽允许指定拿取");
        Check(ItemTaskPolicy.CanPickup(new(4,1,40,2,20,true,100))!=null,"同槽不同类型不替换");
        Check(ItemTaskPolicy.CanPickup(new(4,1,40,1,20,false,100))!=null,"未启用堆叠时同类拾取拒绝");
        Check(ItemTaskPolicy.CanPickup(new(4,1,40,1,20,true,100))==null,"启用后同类合并允许");
        Check(ItemTaskPolicy.CanPickup(new(5,1,4,1,8,true,8))!=null,"消耗品按本类型容量拒绝满堆叠");
        Check(ItemTaskPolicy.CanPickup(new(5,1,4,1,20,true,64))==null,"策略可以使用已知扩大的实际容量，不硬编码原版数量");
        Check(ItemTaskPolicy.CanPickup(new(4,1,float.NaN,0,0,true,100))!=null,"非法物品数量拒绝");
        Check(!ItemTaskPolicy.Conserved(80,100)&&ItemTaskPolicy.Conserved(120,100+20),"溢出合并总量验证");
        var f=new Fixture(); f.Interact();f.Confirm();
        Check(f.Target.Sync.Attempts==1&&f.Target.Sync.LastOwner==f.Bot.Agent.Owner,"真实适配源码以机器人身份提交一次主机拾取");
        Check(f.Bag.Held[InventorySlot.ResourcePack].Instance==f.Target&&f.Runner.Stage==ItemTaskStage.Returning,"实际入包和总量确认后才返程");
        f.Finish();Check(f.Runner.Stage==ItemTaskStage.Complete&&f.Cleanup==1&&BotCommandBroker.Releases==1,"成功只释放一次令牌和预留物品");
        f=new Fixture(heldId:2,held:30,amount:40);TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=true;f.Interact();f.Confirm();f.Finish();
        Check(f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack==70&&f.Runner.Stage==ItemTaskStage.Complete,"同类资源合并保持总量");
        f=new Fixture(heldId:2,held:90,amount:30);TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=true;f.Interact();f.Confirm();
        Check(f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack==100&&f.Old.Sync.Custom.ammo==20&&f.Session.Verify(),"原流程部分合并留下余量，总量守恒");
        f=new Fixture(heldId:2,held:30);f.Tick(1);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"真实适配读取关闭的堆叠开关，不偷偷合并");
        f=new Fixture(heldId:1,held:20);f.Tick(1);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Bag.Held[InventorySlot.ResourcePack].Instance==f.Old,"不同资源类型不覆盖背包");
        f=new Fixture(slot:InventorySlot.Consumable,amount:3);var resource=Fixture.World(InventorySlot.ResourcePack,9,60,false,0);
        f.Bag.Held[InventorySlot.ResourcePack]=new(){Instance=resource};f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack]=new(){AmmoInPack=60};
        f.Interact();f.Confirm();f.Finish();Check(f.Bag.Held.Count==2&&f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack==60,"机器人资源包与熔锁器等消耗品槽互不占用");
        f=new Fixture(slot:InventorySlot.Consumable,heldId:2,held:5,amount:4);TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=true;f.Interact();f.Confirm();
        Check(f.Bag.AmmoStorage.Counts[InventorySlot.Consumable].AmmoInPack==8&&f.Old.Sync.Custom.ammo==1&&f.Session.Verify(),"消耗品部分合并保留多余数量");
        f=new Fixture(ItemTaskKind.PutBack,heldId:2,held:60);f.Old.Sync.Custom.ammo=100;f.Interact();f.Confirm();
        Check(f.Old.Sync.Custom.ammo==60&&f.Old.Sync.LastOwner==f.Bot.Agent.Owner&&f.Old.Sync.State.status==ePickupItemStatus.PlacedInLevel,"放回沿用原对象与机器人当前剩余量，不恢复满包");
        Check(!f.Bag.Held.ContainsKey(InventorySlot.ResourcePack)&&f.Session.Verify(),"背包已空且世界物品确认后放回完成");
        f=new Fixture(ItemTaskKind.PutBack,InventorySlot.Consumable,heldId:2,held:2);f.Interact();f.Confirm();Check(f.Old.Sync.Custom.ammo==2&&f.Session.Verify(),"消耗品放回保留数量");
        f=new Fixture(ItemTaskKind.PutBack,heldId:2,held:20);f.Container.Available=false;f.Tick(1);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Bag.Held.Count==1,"已占用容器拒绝任务，携带物不丢失");
        f=new Fixture();f.Interact();f.Target.Sync.Accept=false;f.Confirm();f.Tick(7);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==1&&f.Target.Sync.State.status==ePickupItemStatus.PlacedInLevel,"未确认拾取超时，不反复请求或假成功");
        f=new Fixture();f.Interact();f.Target.Sync.Custom.ammo=20;f.Tick(1.7f);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"交互时物品数量改变则停止提交");
        f=new Fixture(ItemTaskKind.PutBack,heldId:2,held:20);f.Interact();f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack=10;f.Tick(1.7f);
        Check(f.Old.Sync.Attempts==0&&f.Runner.Stage==ItemTaskStage.Failed,"放回过程中使用了资源，不能复制旧数量");
        f=new Fixture();f.Tick(1);f.Runner.Cancel(false);f.Tick(1.1f);
        Check(f.Runner.Stage==ItemTaskStage.Cancelled&&f.Target.Sync.Attempts==0&&f.Cleanup==1,"移动时取消不拾取、不丢弃");
        f=new Fixture();f.Interact();f.Runner.Cancel(false);f.Confirm();
        Check(f.Runner.Stage==ItemTaskStage.Cancelled&&f.Target.Sync.Attempts==1&&f.Bag.Held.Count==1,"交互阶段取消，完成一次交互再停，保留拿到的物品");
        f=new Fixture();f.Tick(1);f.Runner.Cancel(true);f.Tick(1.1f);f.Tick(1.2f);
        Check(f.Target.Sync.Attempts==0&&f.Runner.Stage==ItemTaskStage.Cancelled&&f.Bot.Agent.Position.x==30,"取消移动任务并返回领队");
        f=new Fixture();ItemTaskAdapter.TeleportEnabled=false;f.Tick(1);f.Tick(1.1f);f.Tick(1.2f);
        Check(f.Bot.Requests==1&&f.Bot.Stops==0,"行走中的同一目的地不重复请求或取消");
        f.Runner.Cancel(true);f.Tick(1.3f);f.Tick(1.4f);f.Tick(2.5f);
        Check(f.Bot.Stops==1&&f.Bot.Requests==2,"取消后返程行走不被每帧反复停止");
        f=new Fixture();f.Interact();f.Confirm();BotCommandBroker.AllowExecute=false;f.Tick(1.9f);
        Check(f.Runner.Stage==ItemTaskStage.Paused&&BotCommandBroker.Stage==CommandStage.Paused,"已完成任务的返程也让路扫描");
        BotCommandBroker.AllowExecute=true;f.Tick(2);f.Tick(2.1f);
        Check(f.Runner.Stage==ItemTaskStage.Complete&&f.Target.Sync.Attempts==1,"扫描结束继续返程，不重新执行已完成拾取");
        f=new Fixture();f.Tick(1);BotCommandBroker.AllowExecute=false;f.Tick(1.1f);
        Check(f.Runner.Stage==ItemTaskStage.Paused&&f.Target.Sync.Attempts==0,"扫描在接近阶段暂停任务");
        BotCommandBroker.AllowExecute=true;f.Tick(2);Check(f.Runner.Stage==ItemTaskStage.Interacting,"扫描结束恢复原指定目标");
        f=new Fixture();BotCommandBroker.AllowAcquire=false;f.Tick(1);f.Runner.Cancel(false);f.Tick(2);
        Check(f.Runner.Stage==ItemTaskStage.Cancelled&&BotCommandBroker.Releases==0&&f.Cleanup==1,"等待权限时取消释放预留，不释放别人的令牌");
        f=new Fixture();f.Tick(1);BotCommandBroker.Lease=false;f.Tick(1.1f);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"失效令牌停止任务");
        f=new Fixture();f.Tick(1);f.Bot.Agent.Owner.IsBot=false;f.Tick(1.1f);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Bot.Stops==0,"真人替换机器人后不继续操控");
        f=new Fixture();f.Tick(1);Builder.CurrentFloor=new();f.Tick(1.1f);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"关卡更换不沿用目标");
        f=new Fixture();f.Tick(2);f.Tick(1.5f);Check(f.Runner.Stage==ItemTaskStage.Failed,"时间回退清理任务");
        f=new Fixture();f.Tick(1);SNetwork.SNet.IsMaster=false;f.Tick(1.1f);Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"失去房主权限不再修改物品");
        f=new Fixture();f.Target.CourseNode.m_zone.DimensionIndex=1;f.Tick(1);Check(f.Runner.Stage==ItemTaskStage.Failed,"跨维度物品拒绝");
        f=new Fixture();f.Target.CourseNode.m_zone.Layer=new();f.Tick(1);Check(f.Runner.Stage==ItemTaskStage.Failed,"跨关卡分层物品拒绝");
        f=new Fixture(ItemTaskKind.Return);f.Tick(1);f.Tick(1.1f);Check(f.Runner.Stage==ItemTaskStage.Complete&&f.Target.Sync.Attempts==0,"独立返回不触发物品交互");
        f=new Fixture();var nativeShare=new PlayerBotActionShareResourcePack{DescBase=new(){Terminated=false}};f.Bot.Actions.Add(nativeShare);f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Waiting&&!f.Runner.OwnsControl,"等待已有原版补给动作完成，不抢库存");
        nativeShare.DescBase.Terminated=true;f.Tick(1.1f);Check(f.Runner.OwnsControl,"原版补给结束后再取得控制");
        nativeShare.DescBase.Terminated=false;f.Tick(1.2f);Check(f.Runner.Stage==ItemTaskStage.Paused&&!f.Runner.OwnsControl,"原版库存动作存在时暂停，避免同时操作背包");
        nativeShare.DescBase.Terminated=true;f.Tick(1.3f);Check(f.Runner.Stage==ItemTaskStage.Interacting&&f.Runner.OwnsControl,"库存动作结束后恢复原任务");
        f=new Fixture(); f.Target.container=new();f.Target.container.m_core.Status=eResourceContainerStatus.Closed;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"指定拾取不能穿过未打开容器拿走物品");
        f=new Fixture();f.Target.container=new();f.Interact();f.Target.container.m_core.Status=eResourceContainerStatus.Closed;f.Confirm();
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"交互期间容器关闭也不能提交拾取");
        f=new Fixture(explicitCommand:false);f.Tick(1);f.Tick(1.1f);
        Check(BotCommandBroker.LastKind==CommandKind.Collect&&BotCommandBroker.Warps==0&&f.Bot.Requests==1,"自动任务以较低优先级行走，不沿用明确命令传送");
        f=new Fixture();f.Interact();f.Confirm();BotCommandBroker.Resume=true;BotCommandBroker.ResumeAt=new Vector3(80);f.Tick(1.9f);
        Check(f.Bot.Agent.Position.x==80&&BotCommandBroker.Stage==CommandStage.Returning,"明确拾取返回原扫描而非领队，使用返程阶段");
        f.Tick(2);Check(f.Runner.Stage==ItemTaskStage.Complete&&BotCommandBroker.Releases==1,"扫描返程完成后释放一次令牌");
        f=new Fixture();f.Bot.Agent.Position=new Vector3(8.3f);f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Interacting&&BotCommandBroker.Warps==0&&f.Bot.Requests==0,"2.2米手长内直接开始交互，不短距离传送");
        f=new Fixture();f.Bot.Agent.Position=new Vector3(7.7f);BotCommandBroker.CloseEnough=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Moving&&f.Bot.Requests==1&&BotCommandBroker.Warps==0&&f.Target.Sync.Attempts==0,"超过2.2米但不足8米时行走接近，不传送");
        f.Tick(1.1f);Check(f.Bot.Requests==1&&f.Bot.Stops==0,"短距离行走不反复取消／重新请求");
        f.Bot.Agent.Position=new Vector3(9);f.Tick(1.2f);Check(f.Runner.Stage==ItemTaskStage.Interacting&&f.Results==0,"到达后才交互，任务入队不回传成功");
        f.Tick(1.8f);f.Tick(1.9f);Check(f.Results==1&&f.Result==true,"实际同步确认后回传一次成功");
        f.Runner.Abort("返程失败");Check(f.Results==1&&f.Result==true,"返程失败不会把已经拿到物品改判失败");
        f=new Fixture();f.Runner.Abort("取消测试");Check(f.Results==1&&f.Result==false,"未获得控制就失败也释放预约并回传");
        f=new Fixture();f.Interact();Physics.Blocked=true;f.Confirm();Check(f.Runner.Stage==ItemTaskStage.Moving&&f.Target.Sync.Attempts==0,"交互中出现遮挡时重新接近，不穿墙提交拾取");
        Physics.Blocked=false;f.Tick(1.9f);f.Tick(2.5f);f.Tick(2.6f);Check(f.Target.Sync.Attempts==1&&f.Runner.Stage==ItemTaskStage.Returning,"遮挡消失后重新计时，仍只提交一次拾取");
        f=new Fixture();f.Interact();f.Bot.Agent.Position=new Vector3(5);f.Tick(1.4f);
        Check(f.Runner.Stage==ItemTaskStage.Moving&&f.Target.Sync.Attempts==0,"交互中漂移先重新接近，不冒认拾取成功");
        f.Bot.Agent.Position=new Vector3(8.5f);f.Tick(1.5f);f.Tick(2.1f);f.Tick(2.2f);
        Check(f.Target.Sync.Attempts==1&&f.Runner.Stage==ItemTaskStage.Returning,"重新站定后计时，位置漂移不造成重复提交");
        Check(ItemArrivalPolicy.Interactable(2.19f,0,0,false)&&!ItemArrivalPolicy.Interactable(2.21f,0,0,false),"默认手长边界2.2米");
        f=new Fixture();f.Tick(1);f.Target.transform.position=new Vector3(40);f.Tick(1.1f);
        Check(f.Runner.Stage==ItemTaskStage.Moving&&f.Bot.Requests==1&&BotCommandBroker.Warps==1,"同一物品位置变动时重新规划接近点，不再次传送或停在旧目标");
        Check(ItemArrivalPolicy.Interactable(1,1.8f,0,false)&&!ItemArrivalPolicy.Interactable(1,2.1f,0,false),"物品高度与水平交互范围分别限制");
        Check(!ItemArrivalPolicy.Interactable(float.NaN,0,0,false),"非法坐标不可交互");
        f=new Fixture();f.Interact();BotCommandBroker.AllowExecute=false;f.Confirm();Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"交互阶段突发失能也不继续提交拾取");
        f=new Fixture(); var currentNode=f.Bot.Agent.CourseNode;
        f.Target.CourseNode=null;f.Target.Sync.State.placement.node.Node=currentNode;
        Check(f.Session.Validate()==null,"物品CourseNode为空时使用当前同步位置节点");
        f.Interact();f.Confirm();Check(f.Target.Sync.LastNode==currentNode&&f.Target.Sync.Attempts==1,"提交拾取也使用解析出的同步节点");
        f=new Fixture();f.Target.CourseNode=null;f.Target.container=new();f.Target.container.m_core.SpawnNode=f.Bot.Agent.CourseNode;
        Check(f.Session.Validate()==null,"柜内物品使用当前容器节点补充");
        f=new Fixture();f.Target.CourseNode=null;Check(f.Session.Validate().Contains("尚未就绪"),"未知节点不猜测机器人所在维度");
        f=new Fixture();f.Target.Sync.State.placement.node.Node=new(){m_zone=new(){DimensionIndex=1}};
        Check(f.Session.Validate().Contains("维度"),"有效同步节点跨维度时不可退回旧字段节点放行");
        f=new Fixture();f.Target.Sync.State.placement.node.Node=new(){m_zone=new(){Layer=new UnityEngine.Object()}};
        Check(f.Session.Validate().Contains("分层"),"真实跨层不因新解析方法绕过限制");
        Console.WriteLine("PASS: "+checks+" 项实际任务调度／适配／反射桥源码断言；原生游戏、菜单和网络由模拟接口替代，未游戏实测。");
    }
    static void LocalCapacityTests()
    {
        var adapter=new StackCompatibility(()=>new(1,true,StackRuntime.LocalProvider,null,float.MaxValue,1,true));
        var resource=adapter.Read(InventorySlot.ResourcePack,5);
        var consumable=adapter.Read(InventorySlot.Consumable,5);
        Check(resource.Enabled&&resource.Limit==float.MaxValue,"本地全量累计不受资源900上限限制");
        Check(consumable.Enabled&&consumable.Limit==float.MaxValue,"本地消耗品全量累计不受原最大值×8限制");
        Check(ItemTaskPolicy.CanPickup(new(4,2,60,2,880,resource.Enabled,resource.Limit))==null,"机器人允许44+3部分合并");
        Check(ItemTaskPolicy.CanPickup(new(4,2,60,2,900,resource.Enabled,resource.Limit))==null,"机器人原900满包仍能继续累计");
        Check(ItemTaskPolicy.Conserved(880+60,940)&&!ItemTaskPolicy.Conserved(880+60,900),"全量累计仍拒绝丢失40的结果");
        adapter=new StackCompatibility(()=>new(2,true,StackRuntime.LocalProvider,null,float.MaxValue,1,true));
        Check(!adapter.Read(InventorySlot.ResourcePack,5).Enabled,"重复实现仍拒绝合并");
        adapter=new StackCompatibility(()=>new(1,false,StackRuntime.LocalProvider,"同步补丁缺失",float.MaxValue,1,true));
        Check(!adapter.Read(InventorySlot.ResourcePack,5).Enabled,"未完整核验不启用新规则");
        adapter=new StackCompatibility(()=>new(1,true,StackRuntime.LocalProvider,null,float.NaN,8));
        Check(!adapter.Read(InventorySlot.ResourcePack,5).Enabled,"非法容量规则拒绝合并");
        var f=new Fixture(heldId:2,held:900,amount:60);StackTestRuntime.FullMerge=true;f.Interact();f.Confirm();
        Check(f.Session.Verify()&&f.Bag.Held[InventorySlot.ResourcePack].Instance==f.Old
            &&f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack==960,"实际任务验证保留旧物品的超限全量转移");
        f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack=940;
        Check(!f.Session.Verify(),"全量转移发生丢失仍不通过数量守恒");
        f=new Fixture(slot:InventorySlot.Consumable,heldId:2,held:40,amount:3);StackTestRuntime.FullMerge=true;f.Interact();f.Confirm();
        Check(f.Session.Verify()&&f.Bag.AmmoStorage.Counts[InventorySlot.Consumable].AmmoInPack==43,"实际消耗品任务允许超过原×8上限");
        f=new Fixture(kind:ItemTaskKind.PutBack,heldId:2,held:960);StackTestRuntime.FullMerge=true;f.Interact();f.Confirm();
        Check(f.Session.Verify()&&f.Old.Sync.Custom.ammo==960,"累计后的数量可以完整放回容器");
    }
    static void DiagnosticTests()
    {
        var f=new Fixture();ItemTaskAdapter.Messages.Clear();BotCommandBroker.AllowAcquire=false;
        f.Tick(1);f.Tick(1.1f);
        Check(ItemTaskAdapter.Messages.FindAll(x=>x.Contains("等待控制权限")).Count==1,"同一权限等待原因5秒内不重复刷屏");
        f.Tick(6.1f);Check(ItemTaskAdapter.Messages.FindAll(x=>x.Contains("等待控制权限")).Count==2,"持续等待保留低频诊断");
        f=new Fixture();ItemTaskAdapter.Messages.Clear();f.Bot.Actions.Add(new PlayerBotActionCarryExpeditionItem{DescBase=new PlayerBotActionBase.Descriptor()});
        f.Tick(1);Check(ItemTaskAdapter.Messages.Exists(x=>x.Contains("手持任务道具搬运")),"原版占用动作有明确原因");
        f=new Fixture();ItemTaskAdapter.Messages.Clear();BotCommandBroker.AllowExecute=false;f.Tick(1);
        Check(ItemTaskAdapter.Messages.Exists(x=>x.Contains("执行权限受阻")),"已获租约后执行拒绝有独立阶段诊断");
        f=new Fixture();Physics.BodyBlocked=true;
        Check(!ItemInteractionGeometry.Standing(f.Bot.Agent,f.Target.transform.position,f.Target,out _,out var reason)&&reason.Contains("身体碰撞=16"),"站位诊断区分身体碰撞");
        Physics.BodyBlocked=false;Physics.Blocked=true;
        Check(!ItemInteractionGeometry.Standing(f.Bot.Agent,f.Target.transform.position,f.Target,out _,out reason)&&reason.Contains("视线遮挡=16"),"站位诊断区分视线遮挡");
        Physics.Blocked=false;
        Check(ItemInteractionGeometry.Standing(f.Bot.Agent,f.Target.transform.position,f.Target,out _,out reason)&&reason==null,"正常站位不改变原判定");
        BotCommandBroker.AllowAcquire=false;f.Bot.Agent.IsInBioscan=true;
        Check(!BotTaskBridge.AutoAvailable(f.Bot,out reason)&&reason.Contains("参与扫描"),"自动拾取扫描保护报告当前扫描原因");
        f=new Fixture();f.Tick(1);ItemTaskAdapter.Messages.Clear();f.Runner.Abort("数量守恒核验失败");
        Check(ItemTaskAdapter.Messages.Exists(x=>x.Contains("任务失败")&&x.Contains("数量守恒")),"终止原因进入诊断日志");
    }
    static void StandaloneTests()
    {
        var f=new Fixture(heldId:2,held:30,amount:40);StackTestRuntime.Standalone=true;f.Interact();f.Confirm();f.Finish();
        Check(f.Runner.Stage==ItemTaskStage.Complete&&f.Session.Verify()&&f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack==70,"独立实际补丁生效且Core关闭，合并70并核验总量");
        f=new Fixture(heldId:2,held:90,amount:30);StackTestRuntime.Standalone=true;f.Interact();f.Confirm();
        Check(f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack==100&&f.Old.Sync.Custom.ammo==20&&f.Session.Verify(),"独立资源容量100，溢出20保留在世界且守恒");
        f=new Fixture(heldId:2,held:100);StackTestRuntime.Standalone=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"独立资源已满拒绝，不声称无限堆叠");
        f=new Fixture(slot:InventorySlot.Consumable,heldId:2,held:20,amount:50);f.Target.ItemDataBlock.ConsumableAmmoMax=64;StackTestRuntime.Standalone=true;f.Interact();f.Confirm();
        Check(f.Bag.AmmoStorage.Counts[InventorySlot.Consumable].AmmoInPack==64&&f.Old.Sync.Custom.ammo==6&&f.Session.Verify(),"消耗品读取实际扩大的容量64，保留余量6");
        f=new Fixture(slot:InventorySlot.Consumable,heldId:2,held:5,amount:4);StackTestRuntime.Standalone=true;f.Interact();f.Target.ItemDataBlock.ConsumableAmmoMax=4;f.Tick(1.7f);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"提交前容量变小，拒绝旧判断");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;f.Interact();StackTestRuntime.Standalone=false;f.Tick(1.7f);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"途中独立补丁移除，提交前重新识别并拒绝");
        f=new Fixture(heldId:2,held:30);f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"只有程序集类型没有实际前缀补丁，不误认加载");
        f=new Fixture(heldId:2,held:30);TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=true;StackTestRuntime.CorePatched=false;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"Core开关打开但未挂补丁，不误认有效");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;TheArchive.Core.FeaturesAPI.FeatureManager.Enabled=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"独立和Core重复生效，拒绝双重合并");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;StackTestRuntime.ChangedCode=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"同名修改版指纹不符，拒绝假定旧容量");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;StackTestRuntime.WrongOwner=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"错误补丁所有者拒绝");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;StackTestRuntime.Unknown=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"未知额外堆叠补丁拒绝，不任意选择一个容量");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;StackTestRuntime.CoreReadable=false;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"Core补丁在场但开关不可读，不能确认单一实现");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;StackTestRuntime.Duplicate=true;f.Tick(1);
        Check(f.Runner.Stage==ItemTaskStage.Failed&&f.Target.Sync.Attempts==0,"同一独立前缀重复挂载也拒绝");
        f=new Fixture(heldId:2,held:30);StackTestRuntime.Standalone=true;f.Interact();f.Confirm();f.Bag.AmmoStorage.Counts[InventorySlot.ResourcePack].AmmoInPack+=20;
        Check(!f.Session.Verify(),"实际结果凭空增加数量仍不通过守恒核验");
        f=new Fixture(heldId:2,held:90,amount:30);StackTestRuntime.Standalone=true;f.Interact();f.Confirm();f.Old.Sync.Custom.ammo=0;
        Check(!f.Session.Verify(),"溢出余量被吞掉仍不通过守恒核验");
        f=new Fixture();StackTestRuntime.Standalone=true;StackTestRuntime.ChangedCode=true;f.Interact();f.Confirm();
        Check(f.Session.Verify(),"空槽正常拿取不需要堆叠能力，不因未知容量误阻止");
    }
}
