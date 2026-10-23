using System;
using System.Collections.Generic;
using System.Reflection;
using Enemies;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks {
    internal static partial class FocusOrders {
        sealed class Entry {
            public PlayerAIBot Bot;public EnemyAgent Target;public IntPtr Floor,Agent,Owner;public int Slot,EnemyID;
            public string Name,Label;public bool Committed;public float SelectionSince;public readonly object Report=new();
            public bool Controlled; public AIGraph.AIG_CourseNode Origin;
            public Func<bool> Occupied, ControlIdentity;
        }
        static readonly Dictionary<IntPtr,Entry> active=new();
        static readonly List<Entry> snapshot=new(3);
        static MethodInfo state,findSight;static FieldInfo visible;static FieldInfo selected,selectedID,selectedAt,assignedAt,selectDue,optionDue;
        internal static bool Available{get;private set;}
        internal static bool HasState=>active.Count!=0;
        internal static bool Owns(PlayerAIBot bot)=>bot!=null&&active.ContainsKey(bot.Pointer);
        static bool Host=>SNet.IsMaster&&GameStateManager.CurrentStateName==eGameStateName.InLevel&&Builder.CurrentFloor!=null;
        internal static EnemyAgent Target(PlayerAgent source){
            if(source==null||!Physics.Raycast(source.EyePosition,source.TargetLookDir,out var hit,100,
                LayerManager.MASK_ENEMY_DAMAGABLE|LayerManager.MASK_ENEMY|LayerManager.MASK_DEFAULT|LayerManager.MASK_PLAYER_INTERACT_BLOCKERS,QueryTriggerInteraction.Collide)||hit.collider==null)return null;
            return hit.collider.GetComponentInParent<EnemyAgent>()??hit.collider.GetComponentInParent<Dam_EnemyDamageLimb>()?.m_base?.Owner;
        }
        internal static bool Attackable(EnemyAgent enemy)=>enemy?.Alive==true&&enemy.AI!=null&&(int)enemy.AI.Mode==1&&enemy.Damage!=null&&!enemy.Damage.IsImortal;
        internal static void Install(Harmony patches){
            try{
                var type=KnownTypes.Get("PreLuna.CoopBotsCustom","PreLuna.CoopBotsCustom.Combat.CombatAdapter");
                var select=type==null?null:AccessTools.Method(type,"SelectTarget",new[]{typeof(RootPlayerBotAction)});
                state=type==null?null:AccessTools.Method(type,"State",new[]{typeof(PlayerAIBot)});
                var s=state?.ReturnType; findSight=s==null?null:AccessTools.Method(type,"FindSight",new[]{s,typeof(EnemyAgent)});visible=findSight==null?null:AccessTools.Field(findSight.ReturnType,"Visible");
                selected=s==null?null:AccessTools.Field(s,"Selected");selectedID=s==null?null:AccessTools.Field(s,"SelectedID");
                selectedAt=s==null?null:AccessTools.Field(s,"SelectedAt");assignedAt=s==null?null:AccessTools.Field(s,"AssignedAt");
                selectDue=s==null?null:AccessTools.Field(s,"SelectDue");optionDue=s==null?null:AccessTools.Field(s,"OptionDue");
                if(findSight==null||visible?.FieldType!=typeof(bool)||select==null||selected?.FieldType!=typeof(EnemyAgent)||selectedID?.FieldType!=typeof(IntPtr)||selectedAt==null||assignedAt==null||selectDue==null||optionDue==null)
                    throw new MissingMethodException("现有武器评分适配接口不匹配");
                patches.Patch(select,postfix:new HarmonyMethod(typeof(FocusOrders),nameof(SelectTarget)){priority=Priority.Last});Available=true;
            }catch(Exception e){Available=false;Plugin.Logger.LogWarning("[集火接口] 未启用集火入口："+e.GetBaseException().Message);}
        }
        internal static string Status(PlayerAIBot bot)=>bot!=null&&active.TryGetValue(bot.Pointer,out var e)?"集火 "+e.Label:null;
        static bool Identity(Entry e)=>e.ControlIdentity?.Invoke()!=false&&Host&&Builder.CurrentFloor.Pointer==e.Floor&&PlayerManager.Current?.GetPlayerAgentInSlot(e.Slot)?.Pointer==e.Agent
            &&e.Bot.Agent.Owner?.Pointer==e.Owner&&PlayerManager.Current.GetPlayerAgentInSlot(e.Slot).GetComponent<PlayerAIBot>()?.Pointer==e.Bot.Pointer;
        static bool Valid(Entry e)=>Identity(e)&&e.Bot.Agent.Alive&&Attackable(e.Target)&&e.Target.GetInstanceID()==e.EnemyID
            &&ItemRegion.SameSpace(e.Bot.Agent,e.Target.CourseNode)&&SearchTraversal.InScope(e.Target.CourseNode,e.Origin??PlayerManager.GetLocalPlayerAgent()?.CourseNode,1);
        static bool Protected(PlayerAIBot bot)=>ItemTaskAdapter.Busy(bot)||SingleKillAdapter.Owns(bot)||ResourceUseAdapter.Owns(bot)||SharedMeleeBridge.Owns(bot)
            ||!MovementOrders.CombatAvailable(bot);
        internal static void Issue(PlayerAIBot bot,EnemyAgent target){
            if(!Available||!Host||bot?.Agent?.Alive!=true||bot.Agent.Owner?.IsBot!=true||!Attackable(target)){
                ItemTaskAdapter.Feedback("集火需要现有战斗评分接口、存活机器人和已激怒敌人；沉睡目标使用指定击杀／协同近战");return;}
            if(!ItemRegion.SameSpace(bot.Agent,target.CourseNode)||!SearchTraversal.InScope(target.CourseNode,PlayerManager.GetLocalPlayerAgent()?.CourseNode,1)){
                ItemTaskAdapter.Feedback("集火目标必须位于当前Zone同层同维度");return;}
            if(active.TryGetValue(bot.Pointer,out var old)){
                if(old.Target.Pointer==target.Pointer){ItemTaskAdapter.Feedback("相同集火目标已登记，不重复取消攻击");return;}
                Clear(old,TaskBoardState.Cancelled,"新集火目标替换旧目标");
            }
            var e=new Entry{Bot=bot,Target=target,Floor=Builder.CurrentFloor.Pointer,Agent=bot.Agent.Pointer,Owner=bot.Agent.Owner.Pointer,
                Slot=bot.Agent.PlayerSlotIndex,EnemyID=target.GetInstanceID(),Name=bot.Agent.PlayerName,Label="敌人 #"+target.EnemyDataID};
            active.Add(bot.Pointer,e);Report(e,TaskBoardState.Waiting,"等待原版攻击窗口","保留原版攻击优先级；扫描、救援与明确交互优先");
            ItemTaskAdapter.Feedback(e.Name+"：已登记集火 "+e.Label+"；沿用两把武器评分、弹药与换弹");
        }
        static void Report(Entry e,TaskBoardState status,string stage,string reason){
            TaskBoard.Journal.Record(e.Report,e.Slot,e.Owner.ToInt64(),e.Name,"集火指定敌人",e.Label,status,stage,reason,Time.time);
        }
        static void SelectTarget(RootPlayerBotAction __0){
            if(!Available||!HasState||__0?.m_bot==null||!active.TryGetValue(__0.m_bot.Pointer,out var e))return;
            if(e.Controlled)return;
            Commit(e,__0.m_attackAction);
        }
        static void Commit(Entry e,PlayerBotActionAttack.Descriptor desc){
            try{
                if(!Valid(e)){Clear(e,e.Target?.Alive==false?TaskBoardState.Complete:TaskBoardState.Failed,"目标死亡／失效，解除手动目标");return;}
                if((e.Controlled?e.Occupied?.Invoke()==true:Protected(e.Bot))||desc==null||desc.IsTerminated()||!e.Bot.HasLineOfFireToObject(e.Bot.Agent.EyePosition,e.Target.gameObject)){
                    e.Committed=false;Report(e,TaskBoardState.Waiting,"等待可攻击窗口","扫描／救援／交互优先；无视线时不穿墙射击，不重发攻击动作");return;
                }
                var s=state.Invoke(null,new object[]{e.Bot});if(s==null)return;
                var sight=findSight.Invoke(null,new[]{s,(object)e.Target});if(sight==null||!(bool)visible.GetValue(sight)){e.Committed=false;Report(e,TaskBoardState.Waiting,"等待现有探测结果","目标尚未进入现有战斗评分的有效视野；保留原版攻击");return;}
                if(!e.Committed){e.Committed=true;e.SelectionSince=Time.time;optionDue.SetValue(s,0f);}
                selectedAt.SetValue(s,e.SelectionSince);
                selected.SetValue(s,e.Target);selectedID.SetValue(s,e.Target.Pointer);assignedAt.SetValue(s,Time.time);selectDue.SetValue(s,Time.time+.2f);
                desc.TargetAgent=e.Target;
                Report(e,TaskBoardState.Executing,"原版攻击指定目标","仅接管目标身份，现有武器／弹药／换弹逻辑与攻击优先级不变");
            }catch(Exception error){Plugin.Logger.LogWarning("[集火接口] "+error);Clear(e,TaskBoardState.Failed,"集火接口异常，恢复自主战斗");}
        }
        static void Clear(Entry e,TaskBoardState outcome,string reason){
            if(!active.TryGetValue(e.Bot.Pointer,out var current)||!ReferenceEquals(current,e))return;
            active.Remove(e.Bot.Pointer);
            try{if(Identity(e)){
                var s=state?.Invoke(null,new object[]{e.Bot});if(s!=null){selected.SetValue(s,null);selectedID.SetValue(s,IntPtr.Zero);selectDue.SetValue(s,0f);optionDue.SetValue(s,0f);}
                var root=e.Bot.m_rootAction?.ActionBase?.TryCast<RootPlayerBotAction>();
                if(root?.m_attackAction?.TargetAgent?.Pointer==e.Target?.Pointer)root.m_attackAction.TargetAgent=null;
            }}catch(Exception error){Plugin.Logger.LogWarning("[集火接口] 清理："+error.GetBaseException().Message);}
            Report(e,outcome,"解除手动目标",reason);
        }
        internal static void RestoreQuiet(PlayerAIBot bot){if(bot!=null&&active.TryGetValue(bot.Pointer,out var e))Clear(e,TaskBoardState.Cancelled,"明确指令解除手动集火");}
        internal static void Restore(PlayerAIBot bot){if(bot!=null&&active.TryGetValue(bot.Pointer,out var e))Clear(e,TaskBoardState.Cancelled,"玩家恢复自主战斗");ItemTaskAdapter.Feedback("已解除所选机器人的集火目标；既有扫描、救援和物品任务保留");}
        internal static void RestoreAll(){snapshot.Clear();snapshot.AddRange(active.Values);foreach(var e in snapshot)Clear(e,TaskBoardState.Cancelled,"全队恢复自主战斗");snapshot.Clear();ItemTaskAdapter.Feedback("已解除全队集火目标");}
        internal static void Tick(){snapshot.Clear();snapshot.AddRange(active.Values);foreach(var e in snapshot)try{if(!e.Controlled&&!Valid(e))Clear(e,e.Target?.Alive==false?TaskBoardState.Complete:TaskBoardState.Failed,"目标死亡、失效或机器人／关卡改变，恢复自主战斗");}catch(Exception error){Plugin.Logger.LogWarning("[集火接口] 生命周期："+error.GetBaseException().Message);Clear(e,TaskBoardState.Failed,"对象读取失效，解除手动目标");}snapshot.Clear();}
        internal static void Reset(){snapshot.Clear();snapshot.AddRange(active.Values);foreach(var e in snapshot)Clear(e,TaskBoardState.Cancelled,"关卡／房主状态重置");active.Clear();snapshot.Clear();}
    }
}



