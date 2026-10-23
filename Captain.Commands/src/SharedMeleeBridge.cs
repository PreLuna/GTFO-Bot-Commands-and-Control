using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Enemies;
using Gear;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks {
    internal sealed record SharedMeleeView(object Session,string Label,bool Ready,string Blocked);
    internal static class SharedMeleeBridge {
        static Type sync,chatter;static FieldInfo session,target,source,members,count,announced,released,recorderBot;
        static MethodInfo before,own,ready,swing,cancel;static PropertyInfo chatterPlayer;static FieldInfo chatterTarget;
        static HashSet<IntPtr> selection;static bool preparing;static float warningAt;
        internal static bool Available{get;private set;}
        static object Current=>session?.GetValue(null);
        internal static bool HasState=>Available&&Current!=null;
        static bool Host=>SNet.IsMaster&&GameStateManager.CurrentStateName==eGameStateName.InLevel&&Builder.CurrentFloor!=null;
        internal static bool Owns(PlayerAIBot bot)=>Available&&bot!=null&&(bool)own.Invoke(null,new object[]{bot});
        internal static void Install(Harmony patches){
            try{
                sync=KnownTypes.Get("PreLuna.CoopBotsCustom","PreLuna.CoopBotsCustom.Reliability.SyncMelee");
                chatter=KnownTypes.Get("ChatterReborn","ChatterReborn.Components.AutoCommunicator");
                if(sync==null||chatter==null)throw new MissingMethodException("缺少更好的AI／ChatterReborn协同接口");
                session=AccessTools.Field(sync,"session");var t=session?.FieldType;
                target=t==null?null:AccessTools.Field(t,"Target");source=t==null?null:AccessTools.Field(t,"Source");members=t==null?null:AccessTools.Field(t,"Members");
                count=t==null?null:AccessTools.Field(t,"Count");announced=t==null?null:AccessTools.Field(t,"Announced");released=t==null?null:AccessTools.Field(t,"Released");
                before=AccessTools.Method(sync,"BeforeCountdown",new[]{typeof(object)});own=AccessTools.Method(sync,"Owns",new[]{typeof(PlayerAIBot)});
                ready=AccessTools.Method(sync,"Ready");swing=AccessTools.Method(sync,"PlayerSwing",new[]{typeof(MeleeWeaponFirstPerson)});cancel=AccessTools.Method(sync,"Cancel",new[]{typeof(string),typeof(bool)});
                var eligible=AccessTools.Method(sync,"Eligible");var recorder=eligible?.GetParameters()[0].ParameterType;
                recorderBot=recorder==null?null:AccessTools.Field(recorder,"m_bot");
                chatterPlayer=AccessTools.Property(chatter,"LocalPlayer");chatterTarget=AccessTools.Field(chatter,"m_targetAgent");
                if(target==null||source==null||members==null||count==null||announced==null||released==null||before==null||own==null||ready==null||swing==null||cancel==null||eligible==null||recorderBot==null||chatterPlayer==null||chatterTarget==null)
                    throw new MissingMethodException("协同字段／参与者接口不匹配");
                patches.Patch(eligible,postfix:new HarmonyMethod(typeof(SharedMeleeBridge),nameof(FilterEligibility)){priority=Priority.Last});Available=true;
            }catch(Exception e){Available=false;Plugin.Logger.LogWarning("[协同菜单] 未启用菜单桥接，E键原系统保留："+e.GetBaseException().Message);}
        }
        static void FilterEligibility(object __0,ref bool __result){
            if(!__result||!Host||recorderBot==null)return;
            var bot=recorderBot.GetValue(__0) as PlayerAIBot;
            if(bot==null||ItemTaskAdapter.Busy(bot)||SingleKillAdapter.Owns(bot)||ResourceUseAdapter.Owns(bot)||ToolSupplyAdapter.Owns(bot)
                ||preparing&&(selection==null||!selection.Contains(bot.Pointer)))__result=false;
        }
        static object Communicator(PlayerAgent player){
            var find=typeof(UnityEngine.Object).GetMethods(BindingFlags.Static|BindingFlags.Public)
                .Single(m=>m.Name=="FindObjectsOfType"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0).MakeGenericMethod(chatter);
            var list=find.Invoke(null,null) as IEnumerable;if(list==null)return null;
            foreach(var item in list)if((chatterPlayer.GetValue(item) as PlayerAgent)?.Pointer==player.Pointer)return item;
            return null;
        }
        internal static SharedMeleeView Capture(){
            try{return CaptureCore();}catch(Exception error){if(Time.time>=warningAt){warningAt=Time.time+10;Plugin.Logger.LogWarning("[协同菜单] 状态暂不可读："+error.GetBaseException().Message);}return new(Current,"协同状态暂不可读",false,"请先取消或等待原系统更新，不重建攻击会话");}
        }
        static SharedMeleeView CaptureCore(){
            if(!Available)return new(null,"协同接口未就绪",false,"缺少匹配的更好的AI／ChatterReborn");
            var s=Current;if(s==null)return new(null,"尚未准备协同近战",false,"请先靠近沉睡目标，选择参与机器人并准备");
            int total=(int)count.GetValue(s),n=0;var roster=members.GetValue(s) as Array;var names=new List<string>(3);
            for(int i=0;roster!=null&&i<Math.Min(total,roster.Length);i++){
                var member=roster.GetValue(i);if(member==null)continue;
                var bot=AccessTools.Field(member.GetType(),"Bot")?.GetValue(member) as PlayerAIBot;
                names.Add(bot?.Agent?.PlayerName??"未知机器人");if((bool)ready.Invoke(null,new[]{member}))n++;
            }
            bool sent=(bool)released.GetValue(s),done=(bool)announced.GetValue(s)&&!sent&&n==total&&total>0;
            return new(s,(sent?"已挥击":"就绪 "+n+"/"+total)+"；成员："+string.Join("、",names),done,done?null:sent?"本次协同已经执行":"机器人尚未全部站稳并蓄满力");
        }
        internal static void Prepare(EnemyAgent enemy,IReadOnlyList<CaptainMenuBot> candidates){
            if(!Available||!Host||!SingleKillAdapter.Sleeping(enemy)){ItemTaskAdapter.Feedback("协同需要房主、匹配接口和沉睡目标");return;}
            if(Current!=null){ItemTaskAdapter.Feedback("已有E键／菜单共用的协同任务；请查看状态或先取消");return;}
            var player=PlayerManager.GetLocalPlayerAgent();if(player?.Alive!=true||!ItemRegion.SameSpace(player,enemy.CourseNode)||!SearchTraversal.InScope(enemy.CourseNode,player.CourseNode,1)||Vector3.Distance(player.Position,enemy.Position)>4){ItemTaskAdapter.Feedback("请靠近沉睡敌人4米以内再准备");return;}
            var communication=Communicator(player);if(communication==null){ItemTaskAdapter.Feedback("未找到本地Chatter协同组件，请等待其初始化；不另建攻击流程");return;}
            selection=new();foreach(var choice in candidates){
                var agent=PlayerManager.Current?.GetPlayerAgentInSlot(choice.Slot);var bot=agent?.Owner?.IsBot==true?agent.GetComponent<PlayerAIBot>():null;
                if(bot==null||!agent.Alive||agent.Owner.Pointer.ToInt64()!=choice.Owner||ItemTaskAdapter.Busy(bot)||ResourceUseAdapter.Owns(bot)||SingleKillAdapter.Owns(bot))continue;
                MovementOrders.YieldDuty(bot);FocusOrders.RestoreQuiet(bot);selection.Add(bot.Pointer);
            }
            if(selection.Count==0){selection=null;ItemTaskAdapter.Feedback("没有符合条件的所选机器人，原任务保留");return;}
            var saved=chatterTarget.GetValue(communication);preparing=true;
            try{chatterTarget.SetValue(communication,enemy);before.Invoke(null,new[]{communication});}
            finally{chatterTarget.SetValue(communication,saved);preparing=false;selection=null;}
            var s=Current;ItemTaskAdapter.Feedback(s!=null&&(target.GetValue(s) as EnemyAgent)?.Pointer==enemy.Pointer?"已进入现有E键协同准备："+Capture().Label:"现有协同系统未接受准备；检查跟随领队、扫描／倒地、近战装备与距离");
        }
        internal static void Execute(object expected){
            var view=Capture();var player=PlayerManager.GetLocalPlayerAgent();
            if(!Host||expected==null||!ReferenceEquals(expected,view.Session)||(source.GetValue(expected) as PlayerAgent)?.Pointer!=player?.Pointer){ItemTaskAdapter.Feedback("协同任务已替换／取消，请重开菜单");return;}
            if(!view.Ready){ItemTaskAdapter.Feedback(view.Blocked);return;}
            var weapon=player.Inventory?.WieldedItem?.TryCast<MeleeWeaponFirstPerson>();
            if(weapon==null){ItemTaskAdapter.Feedback("请先拿出近战武器并朝向原目标；菜单不会替玩家挥击");return;}
            swing.Invoke(null,new object[]{weapon});
            ItemTaskAdapter.Feedback((bool)released.GetValue(expected)?"已通过现有E键协同释放检查，机器人一起挥击；玩家攻击仍由本人输入":"现有协同检查未释放攻击，请重新查看状态");
        }
        internal static void Cancel(object expected){
            if(!Host||!Available||expected==null||!ReferenceEquals(expected,Current)){ItemTaskAdapter.Feedback("没有匹配的协同任务，请重开菜单");return;}
            cancel.Invoke(null,new object[]{"队长菜单取消协同近战",true});ItemTaskAdapter.Feedback("已取消E键／菜单共用的协同任务");
        }
        internal static void Report()=>ItemTaskAdapter.Feedback(Capture().Label);
    }
}
