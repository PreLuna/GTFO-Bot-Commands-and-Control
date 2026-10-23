using System;
using System.Collections.Generic;
using Gear;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks {
    internal sealed class ToolSupplyTarget {
        internal int Id, Slot; internal long Owner; internal IntPtr Item, Agent;
        internal PlayerAgent Player; internal SentryGunInstance Sentry;
        internal string Label, Blocked;
        internal Vector3 Position => Sentry!=null?Sentry.transform.position:Player.Position;
        internal AIGraph.AIG_CourseNode Node => Sentry!=null?Sentry.CourseNode:Player.CourseNode;
    }
    internal static class ToolSupplyAdapter {
        sealed record Entry(Session Session, ToolSupplyRunner Runner);
        static readonly Dictionary<IntPtr,Entry> active=new();
        static readonly List<Entry> snapshot=new(3);
        static Session applying;
        static bool Host=>SNet.IsMaster&&GameStateManager.CurrentStateName==eGameStateName.InLevel&&Builder.CurrentFloor!=null;
        internal static bool HasState=>active.Count>0;
        internal static bool Owns(PlayerAIBot bot)=>bot!=null&&active.ContainsKey(bot.Pointer);
        internal static string Status(PlayerAIBot bot)=>bot!=null&&active.TryGetValue(bot.Pointer,out var e)?"工具补给·"+Text(e.Runner.Stage):null;
        static string Text(ToolSupplyStage stage)=>stage switch {
            ToolSupplyStage.Moving=>"接近工具",ToolSupplyStage.Equipping=>"装备工具补给包",ToolSupplyStage.Applying=>"补给中",
            ToolSupplyStage.Verifying=>"核验扣包与增弹",ToolSupplyStage.Complete=>"完成",ToolSupplyStage.Cancelled=>"取消",_=>"失败"};
        internal static bool ToolPack(PlayerAIBot bot)=>ReadPack(bot,out _,out _);
        static bool ReadPack(PlayerAIBot bot,out ItemEquippable item,out float amount){
            item=null;amount=0;
            if(bot?.Backpack?.AmmoStorage==null||!bot.Backpack.TryGetBackpackItem(InventorySlot.ResourcePack,out var held))return false;
            item=held.Instance?.TryCast<ItemEquippable>();amount=bot.Backpack.AmmoStorage.GetInventorySlotAmmo(InventorySlot.ResourcePack)?.AmmoInPack??0;
            return item!=null&&amount>0&&float.IsFinite(amount)&&PlayerBackpackManager.TryGetItemInLevelFromItemData(item.Get_pItemData(),out var world)
                &&world?.TryCast<ResourcePackPickup>()?.m_packType==eResourceContainerSpawnType.AmmoTool;
        }
        static Item Tool(PlayerAgent player){
            return player?.Owner!=null&&PlayerBackpackManager.TryGetBackpack(player.Owner,out var bag)&&bag!=null
                &&!bag.IsDeployed(InventorySlot.GearClass)&&bag.TryGetBackpackItem(InventorySlot.GearClass,out var item)?item.Instance:null;
        }
        static float Ammo(ToolSupplyTarget target){
            if(target.Sentry!=null)return target.Sentry.Ammo;
            if(!PlayerBackpackManager.TryGetBackpack(target.Player.Owner,out var bag)||bag?.AmmoStorage==null)return float.NaN;
            var ammo=bag.AmmoStorage.GetInventorySlotAmmo(InventorySlot.GearClass);
            return ammo==null?float.NaN:ammo.AmmoInPack+bag.AmmoStorage.GetClipAmmoFromSlot(InventorySlot.GearClass)*ammo.CostOfBullet;
        }
        static bool Needed(ToolSupplyTarget target)=>target.Sentry!=null?target.Sentry.NeedToolAmmo():target.Player.NeedToolAmmo();
        static bool Current(ToolSupplyTarget target){
            var current=PlayerManager.Current?.GetPlayerAgentInSlot(target.Slot);
            if(current?.Pointer!=target.Agent||current.Owner?.Pointer.ToInt64()!=target.Owner)return false;
            if(target.Sentry!=null)return target.Sentry.Pointer==target.Item&&target.Sentry.Alive&&target.Sentry.m_isDeployed
                &&target.Sentry.Owner?.Pointer==current.Pointer;
            return current.Alive&&Tool(current)?.Pointer==target.Item;
        }
        internal static List<ToolSupplyTarget> Capture(){
            var results=new List<ToolSupplyTarget>(8);var origin=PlayerManager.GetLocalPlayerAgent()?.CourseNode;
            for(int slot=0;slot<4;slot++){
                var player=PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if(player?.Owner==null||!PlayerBackpackManager.TryGetBackpack(player.Owner,out var bag)||bag==null)continue;
                if(player.Alive&&Tool(player) is Item item&&SearchTraversal.InScope(player.CourseNode,origin,1)){
                    var t=new ToolSupplyTarget{Id=results.Count+1,Slot=slot,Player=player,Agent=player.Pointer,Owner=player.Owner.Pointer.ToInt64(),Item=item.Pointer};
                    t.Label="携带工具："+player.PlayerName+"／"+(Plugin.Catalog?.TranslateItems(item.PublicName)??item.PublicName);
                    t.Blocked=Needed(t)?null:"该工具不需要／不接受工具补给";results.Add(t);
                }
                if(bag.TryGetDeployedItem(InventorySlot.GearClass,out var deployed)&&deployed?.TryCast<SentryGunInstance>() is SentryGunInstance sentry
                    &&sentry.Alive&&sentry.m_isDeployed&&SearchTraversal.InScope(sentry.CourseNode,origin,1)){
                    var t=new ToolSupplyTarget{Id=results.Count+1,Slot=slot,Player=player,Agent=player.Pointer,Owner=player.Owner.Pointer.ToInt64(),Item=sentry.Pointer,Sentry=sentry};
                    t.Label="已部署炮台："+player.PlayerName+"／"+(Plugin.Catalog?.TranslateItems(sentry.PublicName)??sentry.PublicName)+"（"+(sentry.AmmoMaxCap>0?sentry.Ammo/sentry.AmmoMaxCap*100:0).ToString("0")+"%）";
                    t.Blocked=Needed(t)?null:"炮台弹药已满／不能补给";results.Add(t);
                }
            }return results;
        }
        internal static void Install(Harmony patches){
            patches.Patch(AccessTools.Method(typeof(PlayerAgent),"GiveAmmoRel"),prefix:new HarmonyMethod(typeof(ToolSupplyAdapter),nameof(RedirectAmmo)){priority=Priority.First+40});
        }
        // This authorization is visible only inside our single native ApplyPackBot call.
        internal static bool Authorizes(PlayerAgent source,PlayerAgent receiver,ItemEquippable item)
            =>applying!=null&&applying.Authorizes(source,receiver,item);
        static bool RedirectAmmo(PlayerAgent __instance,PlayerAgent __0,float __1,float __2,float __3){
            var s=applying;if(s==null||s.Target.Sentry==null||__instance?.Pointer!=s.Target.Player.Pointer||__0?.Pointer!=s.Bot.Agent.Pointer)return true;
            var sentry=s.Target.Sentry;float before=sentry.Ammo;
            sentry.GiveAmmoRel(__0,__1,__2,__3);
            s.TargetIncreased|=float.IsFinite(sentry.Ammo)&&sentry.Ammo>before+.001f;
            if(float.IsFinite(sentry.Ammo))s.TargetPeak=Math.Max(s.TargetPeak,sentry.Ammo);
            var network=sentry.m_syncComp?.TryCast<SentryGunInstance_Sync>();
            if(network==null)throw new InvalidOperationException("炮台可靠同步组件已失效；本次不会再扣资源重试");
            network.ForceReliableAmmoUpdate(sentry.Ammo);
            return false;
        }
        internal static void Issue(PlayerAIBot bot,ToolSupplyTarget target,long expectedPack){
            if(!Host||bot?.Agent?.Alive!=true||bot.Agent.Owner?.IsBot!=true||target==null||!Current(target)){ItemTaskAdapter.Feedback("工具补给目标／机器人已失效，请重开菜单");return;}
            if(target.Sentry!=null&&target.Sentry.m_syncComp?.TryCast<SentryGunInstance_Sync>()==null){ItemTaskAdapter.Feedback("炮台可靠同步组件尚未就绪，拒绝消耗资源");return;}
            if(Owns(bot)){ItemTaskAdapter.Feedback("同一机器人已有工具补给，不增加使用次数");return;}
            if(ItemTaskAdapter.BusyReason(bot)!=null||!ReadPack(bot,out var pack,out _)||pack.Pointer.ToInt64()!=expectedPack||!Needed(target)){
                ItemTaskAdapter.Feedback("工具补给拒绝：已有任务、工具已满或携带的工具补给包已改变");return;}
            foreach(var entry in active.Values)if(entry.Session.Target.Item==target.Item){ItemTaskAdapter.Feedback("该工具已有一次补给预约");return;}
            if(!ItemRegion.SameSpace(bot.Agent,target.Node)||!SearchTraversal.InScope(target.Node,PlayerManager.GetLocalPlayerAgent()?.CourseNode,1)){
                ItemTaskAdapter.Feedback("工具补给仅支持当前Zone同层同维度");return;}
            var route=RouteTraversal.Build(bot.Agent,target.Node,target.Position,null,bot.Agent.CourseNode,out var reason);
            if(Vector3.Distance(bot.Agent.Position,target.Position)>2&&(route==null||route.WaitingForDoor)){
                ItemTaskAdapter.Feedback("工具补给通路不可用："+(reason??"请先打开普通门"));return;}
            MovementOrders.YieldDuty(bot);AutoPickupAdapter.CancelPrompt(bot);
            if(!BotTaskBridge.Acquire(bot,out long token)){ItemTaskAdapter.Feedback("工具补给仲裁未放行，请稍后重试");return;}
            var session=new Session(bot,target,pack,token);
            active.Add(bot.Pointer,new(session,new ToolSupplyRunner(session,Time.time)));
            ItemTaskAdapter.Feedback(bot.Agent.PlayerName+"：已登记给 "+target.Label+" 补给一次");
        }
        internal static bool Cancel(PlayerAIBot bot){if(!Owns(bot))return false;active[bot.Pointer].Runner.Cancel();return true;}
        internal static void Tick(){
            snapshot.Clear();snapshot.AddRange(active.Values);
            foreach(var e in snapshot)try{e.Runner.Tick(Time.time);}catch(Exception error){Plugin.Logger.LogWarning("[工具补给] "+error);e.Runner.Abort("工具补给检查异常，不重复消耗");}
            snapshot.Clear();
        }
        internal static void Reset(){snapshot.Clear();snapshot.AddRange(active.Values);foreach(var e in snapshot)e.Runner.Abort("关卡／房主状态重置");active.Clear();snapshot.Clear();applying=null;}
        sealed class Session:IToolSupplySession{
            internal readonly PlayerAIBot Bot;internal readonly ToolSupplyTarget Target;readonly ItemEquippable pack;
            readonly IntPtr floor,agent,owner;readonly int slot;readonly long token;readonly object ticket=new();readonly string name;
            PlayerBotActionTravel.Descriptor travel;PlayerBotActionEquipItem.Descriptor equip;
            bool applyingCall,applied,spent;internal bool TargetIncreased;float targetBefore,packBefore,packAfter;internal float TargetPeak;
            public Session(PlayerAIBot bot,ToolSupplyTarget target,ItemEquippable pack,long token){Bot=bot;Target=target;this.pack=pack;this.token=token;
                floor=Builder.CurrentFloor.Pointer;agent=bot.Agent.Pointer;owner=bot.Agent.Owner.Pointer;slot=bot.Agent.PlayerSlotIndex;name=bot.Agent.PlayerName;}
            bool Identity=>Host&&Builder.CurrentFloor.Pointer==floor&&PlayerManager.Current?.GetPlayerAgentInSlot(slot)?.Pointer==agent&&Bot.Agent.Owner?.Pointer==owner&&PlayerManager.Current.GetPlayerAgentInSlot(slot).GetComponent<PlayerAIBot>()?.Pointer==Bot.Pointer;
            public bool Valid=>Identity&&Bot.Agent.Alive&&Current(Target)&&ItemRegion.SameSpace(Bot.Agent,Target.Node)
                &&(applied||ReadPack(Bot,out var held,out _)&&held.Pointer==pack.Pointer);
            public bool Allowed=>Needed(Target)&&Bot.Agent.Damage?.GrabbedAtAll!=true&&Bot.TestDangerZones(Target.Position,AIDangerZone.SeverityEnum.Harmful,out _);
            public bool CanExecute=>BotTaskBridge.CanExecute(Bot,token);
            public bool Moving=>travel!=null&&!travel.IsTerminated();
            public bool Arrived {
                get {
                    if(Vector3.Distance(Bot.Agent.Position,Target.Position)>2.2f)return false;
                    if(!Physics.Linecast(Bot.Agent.EyePosition,Target.Position+Vector3.up*.5f,out var hit,LayerManager.MASK_DEFAULT|LayerManager.MASK_PLAYER_INTERACT_BLOCKERS,QueryTriggerInteraction.Ignore))return true;
                    if(hit.collider==null)return false;
                    return Target.Sentry!=null?hit.collider.GetComponentInParent<SentryGunInstance>()?.Pointer==Target.Item
                        :hit.collider.GetComponentInParent<PlayerAgent>()?.Pointer==Target.Agent;
                }
            }
            public bool Equipped=>Bot.Inventory?.WieldedItem?.Pointer==pack.Pointer&&!Bot.Inventory.IsEquiping;
            public bool Confirmed{get{float observed=Ammo(Target);if(float.IsFinite(observed))TargetPeak=Math.Max(TargetPeak,observed);return spent&&(TargetIncreased||float.IsFinite(observed)&&observed>targetBefore+.001f);}}
            public bool Move(){
                if(travel!=null&&!travel.IsTerminated())return true;
                var desired=Target.Position+(Bot.Agent.Position-Target.Position).normalized*1.4f;
                if(!PlayerAIBot.SnapPositionToNav(desired,out var point,.7f,4))return false;
                travel=new(Bot){Prio=100000f,Haste=.5f,DestinationType=PlayerBotActionTravel.Descriptor.DestinationEnum.Position,
                    DestinationPos=point,Radius=.4f,RadiusHeightTolerance=.7f,Persistent=false};
                return Bot.RequestAction(travel);
            }
            public bool Equip(){if(equip!=null&&!equip.IsTerminated())return true;equip=new(Bot){Item=pack,Haste=.5f,Prio=100000f};return Bot.RequestAction(equip);}
            internal bool Authorizes(PlayerAgent source,PlayerAgent receiver,ItemEquippable item)=>applyingCall&&Valid&&Arrived&&Equipped&&Allowed
                &&source?.Pointer==agent&&receiver?.Pointer==Target.Player.Pointer&&item?.Pointer==pack.Pointer;
            public bool Apply(){
                if(applied||!Valid||!Allowed||!Arrived||!Equipped||!ReadPack(Bot,out _,out packBefore))return false;
                targetBefore=Ammo(Target);TargetPeak=targetBefore;if(!float.IsFinite(targetBefore))return false;
                applied=true;applyingCall=true;applying=this;
                try{ResourcePackFirstPerson.ApplyPackBot(Bot.Agent,Target.Player,pack);}
                finally{applying=null;applyingCall=false;
                    var storage=Bot.Backpack?.AmmoStorage?.GetInventorySlotAmmo(InventorySlot.ResourcePack);
                    packAfter=storage?.AmmoInPack??0;spent=float.IsFinite(packAfter)&&packAfter<packBefore-.001f;
                }
                return true;
            }
            public void Report(ToolSupplyStage stage,string reason){
                BotTaskBridge.Report(Bot,token,stage is ToolSupplyStage.Applying or ToolSupplyStage.Verifying?ItemTaskStage.Interacting:ItemTaskStage.Moving);
                TaskBoard.Journal.Record(ticket,slot,owner.ToInt64(),name,"指定工具补给",Target.Label,TaskBoardState.Executing,Text(stage),reason,Time.time);
            }
            public void Finish(ToolSupplyStage stage,string reason){
                active.Remove(Bot.Pointer);
                if(applied){reason+=$"；资源单位 {packBefore:F2}→{packAfter:F2}，工具弹药 {targetBefore:F2}→{TargetPeak:F2}（峰值）";Plugin.Logger.LogInfo("[工具补给核验] "+name+" / "+Target.Label+" / "+reason);}
                try{if(Identity){if(equip!=null&&!equip.IsTerminated())Bot.StopAction(equip);if(travel!=null&&!travel.IsTerminated())Bot.StopAction(travel);}}
                finally{BotTaskBridge.Release(Bot,token,stage==ToolSupplyStage.Complete?ItemTaskStage.Complete:stage==ToolSupplyStage.Cancelled?ItemTaskStage.Cancelled:ItemTaskStage.Failed);
                    TaskBoard.Journal.Record(ticket,slot,owner.ToInt64(),name,"指定工具补给",Target.Label,stage==ToolSupplyStage.Complete?TaskBoardState.Complete:stage==ToolSupplyStage.Cancelled?TaskBoardState.Cancelled:TaskBoardState.Failed,
                        Text(stage),reason,Time.time);ItemTaskAdapter.Feedback(name+"："+reason);}
            }
        }
    }
}
