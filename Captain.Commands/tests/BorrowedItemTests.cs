using System;
using Player;
using LevelGeneration;
using UnityEngine;
using PreLuna.Captain.Tasks;
using PreLuna.CoopBotsCustom.Commands;
static class BorrowedItemTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    sealed class Permit:IControlCommandPermit {
        internal int Borrows,Holds;internal ItemTaskStage Stage;
        internal bool Identity=true; public bool IdentityValid=>Identity;
        public bool Acquire(out long token){Borrows++;token=777;return true;}
        public bool Report(ItemTaskStage stage){Stage=stage;return true;}
        public void Hold()=>Holds++;
    }
    static int Main(){try{
        Check(BotTaskBridge.Bind(),"反射仲裁接口绑定");Time.time=0;Builder.CurrentFloor=new();
        var bot=new PlayerAIBot();var bag=new PlayerBackpack();PlayerBackpackManager.Bags[bot.Agent.Owner.Pointer]=bag;
        var target=new ItemInLevel{Data=new(){slot=InventorySlot.ResourcePack,itemID_gearCRC=77}};
        target.Sync=new(){Item=target,Custom=new(){ammo=40},State=new(){status=ePickupItemStatus.PlacedInLevel}};
        var permit=new Permit();int cleanup=0;bool? result=null;
        BotCommandBroker.Acquires=BotCommandBroker.Releases=0;BotCommandBroker.AllowExecute=true;BotCommandBroker.Lease=true;
        var session=new ItemTaskSession(bot,ItemTaskKind.Pickup,InventorySlot.ResourcePack,target,null,"借用接管",()=>cleanup++,onResult:success=>result=success,controlPermit:permit);
        Check(session.Validate()==null,"复用原背包／目标检查");
        var runner=new ItemTaskRunner(ItemTaskKind.Pickup,session,0,false);
        Time.time=.01f;runner.Tick(Time.time);Check(permit.Borrows==1&&BotCommandBroker.Acquires==0,"真实拾取适配器借用接管票据而非申请第二租约");
        Check(runner.Stage==ItemTaskStage.Interacting&&permit.Stage==ItemTaskStage.Interacting,"交互阶段报告通过接管共享权限");
        Time.time=1;runner.Tick(1);Check(target.Sync.Attempts==1&&target.Sync.LastOwner==bot.Agent.Owner,"真实提交仍使用机器人Owner");
        Check(permit.Stage==ItemTaskStage.Verifying&&runner.Stage==ItemTaskStage.Verifying,"提交后保持原有核验阶段");
        Time.time=1.1f;runner.Tick(1.1f);Check(runner.Stage==ItemTaskStage.Complete&&result==true,"原数量守恒核验完成");
        Check(permit.Holds==1&&BotCommandBroker.Releases==0,"完成交还共享权限而不释放接管租约");
        Check(bot.Agent.Position.x==0&&bot.Requests==0&&target.Sync.Attempts==1,"原地恢复微调，不返程、不重复提交");
        Check(ItemTaskAdapter.Messages.Exists(s=>s.Contains("交回接管微调")),"完成提示对应接管模式");
        // Read the production movement descriptor authorization through its native parent chain.
        target=new ItemInLevel{Data=new(){slot=InventorySlot.ResourcePack,itemID_gearCRC=78}};target.transform.position=new(10);
        target.Sync=new(){Item=target,Custom=new(){ammo=20},State=new(){status=ePickupItemStatus.PlacedInLevel}};bag.Held.Clear();bag.AmmoStorage.Counts.Clear();
        session=new(bot,ItemTaskKind.Pickup,InventorySlot.ResourcePack,target,null,"移动授权",()=>{},controlPermit:permit);
        ItemTaskAdapter.TeleportEnabled=false;Check(session.Acquire()&&session.Validate()==null,"目标接近继续借用控制权限");
        Time.time=2;Check(session.Move(false),"建立原有旅行请求");
        var travel=(PlayerBotActionBase.Descriptor)typeof(ItemTaskSession).GetField("travel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(session);
        var child=new PlayerBotActionBase.Descriptor{ParentActionBase=new(){DescBase=travel}};
        Check(session.OurAction(travel)&&session.OurAction(child)&&!session.OurAction(new PlayerBotActionBase.Descriptor()),"生产拾取适配器只授权自己的旅行及子动作");
        session.StopOwnMovement();Check(!session.OurAction(travel)&&!session.OurAction(child),"结束后不能用旧描述符继续行动");
        // Exercise the production adapter in the existing atomic interaction window after an exit.
        bag.Held.Clear();bag.AmmoStorage.Counts.Clear();target=new(){Data=new(){slot=InventorySlot.ResourcePack,itemID_gearCRC=79}};
        target.Sync=new(){Item=target,Custom=new(){ammo=20},State=new(){status=ePickupItemStatus.PlacedInLevel}};permit=new();cleanup=0;
        session=new(bot,ItemTaskKind.Pickup,InventorySlot.ResourcePack,target,null,"退出原子拾取",()=>{if(cleanup==0)cleanup++;},controlPermit:permit);
        runner=new(ItemTaskKind.Pickup,session,3,false);Time.time=3;runner.Tick(3);runner.Cancel(false);
        Time.time=4;runner.Tick(4);Time.time=4.1f;runner.Tick(4.1f);
        Check(runner.Finished&&target.Sync.Attempts==1&&permit.Holds==1&&cleanup==1,"实际拾取原子阶段取消只提交一次，核验后交还权限和预约："+runner.Stage+" submits="+target.Sync.Attempts+" holds="+permit.Holds+" cleanup="+cleanup);
        Time.time=5;runner.Tick(5);Check(target.Sync.Attempts==1&&cleanup==1,"取消后的额外更新不重复提交或清理");
        target=new(){Data=new(){slot=InventorySlot.ResourcePack,itemID_gearCRC=80}};target.transform.position=new(10);
        target.Sync=new(){Item=target,Custom=new(){ammo=20},State=new(){status=ePickupItemStatus.PlacedInLevel}};bag.Held.Clear();bag.AmmoStorage.Counts.Clear();permit=new();
        session=new(bot,ItemTaskKind.Pickup,InventorySlot.ResourcePack,target,null,"身份失效",()=>{},controlPermit:permit);
        Check(session.Acquire()&&session.Move(false),"建立生产旅行以核验失效清理");int stops=bot.Stops;permit.Identity=false;session.StopOwnMovement();
        Check(!session.Valid&&bot.Stops==stops&&target.Sync.Attempts==0,"身份失效后生产拾取适配器不操作可能重加的机器人或提交拾取");
        Console.WriteLine("PASS: "+checks+" 生产拾取适配器共享接管权限／机器人提交／数量核验／原地交接检查（原生替身）");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
