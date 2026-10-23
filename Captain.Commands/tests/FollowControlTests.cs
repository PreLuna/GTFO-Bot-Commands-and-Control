using System;
using System.Collections.Generic;
using Player;
using UnityEngine;
using PreLuna.Captain.Tasks;
static class FollowControlTests
{
    static int checks;
    static void Check(bool value,string message) { checks++; if (!value) throw new Exception(message); }
    static void Main()
    {
        MovementOrders.Reset(); Time.time=0; LevelGeneration.Builder.CurrentFloor=new(); PlayerManager.Current=new();
        var local=new PlayerAgent { PlayerSlotIndex=0,Owner=new(){IsBot=false} };
        var previous=new PlayerAgent { PlayerSlotIndex=3,Owner=new(){IsBot=false} };
        var bot=new PlayerAIBot {Agent=new(){PlayerSlotIndex=1}}; bot.Agent.Bot=bot; bot.SyncValues.Leader=local;
        PlayerManager.Current.Slots[0]=local; PlayerManager.Current.Slots[1]=bot.Agent; PlayerManager.Current.Slots[3]=previous;
        BotTaskBridge.Permit=BotTaskBridge.Auto=true; ItemTaskAdapter.Blocked.Clear();
        MovementOrders.FollowPlayer(bot,previous); MovementOrders.Tick(); Check(bot.SyncValues.Leader==previous,"原跟随任务正常生效");
        var holds=new List<BorrowHold>(); MovementOrders.SuspendForBorrow(bot,holds); MovementOrders.SuspendFollowForControl(bot,holds);
        Time.time=1; MovementOrders.Tick();
        Check(bot.SyncValues.Leader==previous,"等待接管不改写原版领队");
        foreach(var hold in holds)hold.Restore(); holds.Clear(); MovementOrders.Tick();
        Check(bot.SyncValues.Leader==previous,"接管失败回滚恢复原跟随意图");
        MovementOrders.SuspendForBorrow(bot,holds); MovementOrders.SuspendFollowForControl(bot,holds);
        MovementOrders.KeepFollowPausedAfterControl(bot); foreach(var hold in holds)hold.Discard(); holds.Clear();
        PUI_CommunicationMenu.Current.ExecuteCmdCall(local,bot.Agent);
        for(int i=0;i<20;i++){Time.time+=.5f;MovementOrders.Tick();}
        Check(bot.SyncValues.Leader==local&&MovementOrders.Status(bot).Contains("暂停"),"退出召回后二十次检查不被旧跟随拉走");
        Check(MovementOrders.Control(bot,false),"Q继续入口识别保留的旧跟随"); MovementOrders.Tick();
        Check(bot.SyncValues.Leader==previous,"继续后恢复旧跟随对象");
        MovementOrders.SuspendForBorrow(bot,holds); MovementOrders.SuspendFollowForControl(bot,holds);
        MovementOrders.FollowPlayer(bot,local); foreach(var hold in holds)hold.Restore(); MovementOrders.Tick();
        Check(bot.SyncValues.Leader==local,"新命令替换后旧回滚不能覆盖新任务");
        MovementOrders.Reset(); Console.WriteLine("PASS: "+checks+" 生产MovementOrders接管暂停／退出跟随／继续与改派检查");
    }
}
