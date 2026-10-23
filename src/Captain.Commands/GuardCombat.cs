using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Enemies;
using HarmonyLib;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks {
    // Reuse the existing visible-target cache and weapon chooser. No movement or attack requests.
    internal static class GuardCombat {
        sealed class Choice { public PlayerAIBot Bot; public GuardDutySpec Duty; public EnemyAgent Enemy; public float Since,Next; }
        static readonly Dictionary<IntPtr,Choice> choices=new();
        static readonly List<IntPtr> expired=new(3);static float cleanupAt;
        static MethodInfo state,findSight;static FieldInfo sight,count,enemy,visible,selected,selectedID,selectedAt,assignedAt,selectDue,optionDue;
        internal static bool Available{get;private set;}
        internal static void Install(Harmony patches){
            try{
                var type=KnownTypes.Get("PreLuna.CoopBotsCustom","PreLuna.CoopBotsCustom.Combat.CombatAdapter");
                var select=type==null?null:AccessTools.Method(type,"SelectTarget",new[]{typeof(RootPlayerBotAction)});
                state=type==null?null:AccessTools.Method(type,"State",new[]{typeof(PlayerAIBot)});
                var s=state?.ReturnType;findSight=s==null?null:AccessTools.Method(type,"FindSight",new[]{s,typeof(EnemyAgent)});
                sight=s==null?null:AccessTools.Field(s,"Sight");count=s==null?null:AccessTools.Field(s,"Count");
                var t=findSight?.ReturnType;enemy=t==null?null:AccessTools.Field(t,"Enemy");visible=t==null?null:AccessTools.Field(t,"Visible");
                selected=s==null?null:AccessTools.Field(s,"Selected");selectedID=s==null?null:AccessTools.Field(s,"SelectedID");
                selectedAt=s==null?null:AccessTools.Field(s,"SelectedAt");assignedAt=s==null?null:AccessTools.Field(s,"AssignedAt");
                selectDue=s==null?null:AccessTools.Field(s,"SelectDue");optionDue=s==null?null:AccessTools.Field(s,"OptionDue");
                if(select==null||findSight==null||sight==null||!typeof(IList).IsAssignableFrom(sight.FieldType)||count?.FieldType!=typeof(int)
                    ||enemy?.FieldType!=typeof(EnemyAgent)||visible?.FieldType!=typeof(bool)||selected?.FieldType!=typeof(EnemyAgent)||selectedID?.FieldType!=typeof(IntPtr)
                    ||selectedAt?.FieldType!=typeof(float)||assignedAt?.FieldType!=typeof(float)||selectDue?.FieldType!=typeof(float)||optionDue?.FieldType!=typeof(float))
                    throw new MissingMethodException("现有可见敌人缓存／武器评分接口不匹配");
                patches.Patch(select,postfix:new HarmonyMethod(typeof(GuardCombat),nameof(SelectTarget)){priority=Priority.Last-10});Available=true;
            }catch(Exception e){Available=false;Plugin.Logger.LogWarning("[守卫战斗接口] 使用原版自主战斗："+e.GetBaseException().Message);}
        }
        static float Score(GuardDutySpec duty,EnemyAgent target){
            if(!FocusOrders.Attackable(target)||!SearchTraversal.InScope(target.CourseNode,duty.Anchor.Node,1))return float.MinValue;
            var delta=target.Position-duty.Anchor.Point;
            var victim=duty.Kind==GuardDutyKind.Cover?target.AI.Target?.m_agent?.TryCast<PlayerAgent>():null;
            float cosine=delta.sqrMagnitude<.01f?1:Vector3.Dot(duty.Anchor.Direction,delta.normalized);
            return GuardPolicy.Score(duty.Kind,SearchTraversal.InScope(target.CourseNode,duty.Anchor.Node,0),
                victim!=null&&duty.Receiver!=null&&victim.Pointer==duty.Receiver.Pointer,duty.ReceiverInBound,delta.magnitude,cosine);
        }
        static void SelectTarget(RootPlayerBotAction __0){
            var bot=__0?.m_bot;if(!Available||bot==null)return;
            try{
                var duty=MovementOrders.Guard(bot);
                if(duty==null){choices.Remove(bot.Pointer);return;}
                if(FocusOrders.Owns(bot)||ItemTaskAdapter.Busy(bot)||SingleKillAdapter.Owns(bot)||ResourceUseAdapter.Owns(bot)
                    ||SharedMeleeBridge.Owns(bot))return;
                var desc=__0.m_attackAction;if(desc==null||desc.IsTerminated())return;
                var s=state.Invoke(null,new object[]{bot});if(s==null||sight.GetValue(s) is not IList list)return;
                choices.TryGetValue(bot.Pointer,out var old);
                if(old?.Duty==duty&&Time.time<old.Next){
                    if(old.Enemy==null)return;
                    var fresh=Score(duty,old.Enemy)==float.MinValue?null:findSight.Invoke(null,new[]{s,(object)old.Enemy});
                    if(fresh!=null&&(bool)visible.GetValue(fresh)&&bot.HasLineOfFireToObject(bot.Agent.EyePosition,old.Enemy.gameObject)){Commit(s,desc,old);return;}
                }
                EnemyAgent best=null;float bestScore=float.MinValue;
                int n=Math.Min(128,Math.Min(list.Count,(int)count.GetValue(s)));
                for(int i=0;i<n;i++){
                    var entry=list[i];if(entry==null||!(bool)visible.GetValue(entry))continue;
                    var candidate=enemy.GetValue(entry) as EnemyAgent;float score=Score(duty,candidate);
                    if(score<=bestScore)continue;
                    var fresh=findSight.Invoke(null,new[]{s,(object)candidate});
                    if(fresh==null||!(bool)visible.GetValue(fresh)||!bot.HasLineOfFireToObject(bot.Agent.EyePosition,candidate.gameObject))continue;
                    best=candidate;bestScore=score;
                }
                if(best==null){choices[bot.Pointer]=new(){Bot=bot,Duty=duty,Next=Time.time+.2f};return;}
                if(old?.Duty==duty&&old.Enemy!=null){
                    float score=Score(duty,old.Enemy);var fresh=score==float.MinValue?null:findSight.Invoke(null,new[]{s,(object)old.Enemy});
                    bool valid=fresh!=null&&(bool)visible.GetValue(fresh)&&bot.HasLineOfFireToObject(bot.Agent.EyePosition,old.Enemy.gameObject);
                    if(GuardPolicy.Hold(valid,Time.time-old.Since,score,bestScore)){best=old.Enemy;bestScore=score;}
                }
                if(old?.Duty!=duty||old.Enemy?.Pointer!=best.Pointer){old=new(){Bot=bot,Duty=duty,Enemy=best,Since=Time.time};choices[bot.Pointer]=old;optionDue.SetValue(s,0f);}
                old.Next=Time.time+.2f;Commit(s,desc,old);
            }catch(Exception e){choices.Remove(bot.Pointer);Available=false;Plugin.Logger.LogWarning("[守卫战斗接口] 已停止附加选敌，保留自主战斗："+e);}
        }
        static void Commit(object s,PlayerBotActionAttack.Descriptor desc,Choice choice){
            selected.SetValue(s,choice.Enemy);selectedID.SetValue(s,choice.Enemy.Pointer);selectedAt.SetValue(s,choice.Since);
            assignedAt.SetValue(s,Time.time);selectDue.SetValue(s,Time.time+.2f);desc.TargetAgent=choice.Enemy;
        }
        internal static void Tick(){
            if(Time.time<cleanupAt)return;cleanupAt=Time.time+1;expired.Clear();
            foreach(var pair in choices)try{if(!MovementOrders.HasDuty(pair.Value.Bot))expired.Add(pair.Key);}
                catch(Exception e){expired.Add(pair.Key);Plugin.Logger.LogWarning("[守卫战斗接口] 清理失效缓存："+e.GetBaseException().Message);}
            foreach(var id in expired)choices.Remove(id);expired.Clear();
        }
        internal static void Reset(){choices.Clear();expired.Clear();cleanupAt=0;}
    }
}




