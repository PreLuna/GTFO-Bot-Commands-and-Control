using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks {
    internal sealed class GuardDutySpec {
        internal readonly GuardDutyKind Kind;internal readonly MarkedPosition Anchor;internal readonly PlayerAgent Receiver;
        internal readonly int ReceiverSlot;internal readonly IntPtr ReceiverOwner,ReceiverAgent;
        internal readonly string Label;internal readonly float Radius;
        internal GuardDutySpec(GuardDutyKind kind,MarkedPosition point,float radius,PlayerAgent receiver=null){
            Kind=kind;Anchor=point;Radius=GuardPolicy.Radius(radius);Receiver=receiver;
            ReceiverSlot=receiver?.PlayerSlotIndex??-1;ReceiverOwner=receiver?.Owner?.Pointer??IntPtr.Zero;ReceiverAgent=receiver?.Pointer??IntPtr.Zero;
            Label=kind==GuardDutyKind.Room?"守卫房间":kind==GuardDutyKind.Entrance?"防守入口":"固定点掩护 "+receiver?.PlayerName;
        }
        internal bool Valid=>Kind!=GuardDutyKind.Cover||PlayerManager.Current?.GetPlayerAgentInSlot(ReceiverSlot)?.Pointer==ReceiverAgent
            &&Receiver?.Owner?.Pointer==ReceiverOwner;
        internal bool ReceiverInBound=>Kind!=GuardDutyKind.Cover||Valid&&Receiver.Alive&&SearchTraversal.InScope(Receiver.CourseNode,Anchor.Node,0)
            &&Vector3.Distance(Receiver.Position,Anchor.Point)<=Radius+5;
        internal bool Same(GuardDutySpec other)=>other!=null&&Kind==other.Kind&&ReceiverAgent==other.ReceiverAgent&&ReceiverOwner==other.ReceiverOwner&&Radius==other.Radius;
        internal string Detail=>"固定点半径 "+Radius+"米，不巡视；扫描／救援／明确交互优先"
            +(Kind==GuardDutyKind.Cover&&!ReceiverInBound?"；队友已倒地／离开掩护范围，保留固定点并交由原版救援与自保":"");
    }
    internal sealed class GuardTargetSnapshot {
        internal readonly Dictionary<int,MarkedPosition> Points=new();
        internal readonly Dictionary<int,GuardDutySpec> Covers=new();
        internal CaptainMenuGuardTargets Menu;
    }
    internal static class GuardTargets {
        static MarkedPosition Point(PlayerAgent source,Vector3 desired,AIG_CourseNode room,Vector3 direction){
            if(source?.Owner==null||room==null||Builder.CurrentFloor==null||!PlayerAIBot.SnapPositionToNav(desired,out var point,1,3)
                ||Vector3.Distance(point,desired)>1.5f||!AIG_CourseNode.TryGetCourseNode(source.DimensionIndex,point,1,out var node)
                ||!ItemRegion.SameSpace(source,node)||!SearchTraversal.InScope(node,room,0)
                ||Physics.CheckCapsule(point+Vector3.up*.35f,point+Vector3.up*1.4f,.25f,LayerManager.MASK_DEFAULT,QueryTriggerInteraction.Ignore))return null;
            direction.y=0;
            return new(point,node,source.CourseNode,Builder.CurrentFloor.Pointer,source.Owner.Pointer,Time.time,direction.sqrMagnitude>.01f?direction.normalized:Vector3.forward);
        }
        internal static GuardTargetSnapshot Capture(PlayerAgent source,MarkedPosition marked){
            var snapshot=new GuardTargetSnapshot();var rooms=new List<CaptainMenuGuardPoint>();var covers=new List<CaptainMenuGuardCover>();
            var here=source==null?null:Point(source,source.Position,source.CourseNode,source.TargetLookDir);
            if(here!=null)snapshot.Points[0]=here;
            rooms.Add(new(0,"当前房间·开菜单时的当前位置",here==null?"当前位置缺少可站立导航点":null));
            string markReason=PositionMarks.Reason(marked);
            if(markReason==null&&!SearchTraversal.InScope(marked.Node,source.CourseNode,0))markReason="守卫房间的中键站位必须位于当前房间";
            if(markReason==null)snapshot.Points[1]=marked;
            rooms.Add(new(1,"当前房间·中键标记站位",markReason));
            string entryReason=PositionMarks.Reason(marked);
            if(entryReason==null)snapshot.Points[2]=marked;
            var entrance=new CaptainMenuGuardPoint(2,"入口站位："+(marked?.Label??"请中键标记门旁地面")+"（标记时朝向）",entryReason);
            for(int slot=0;slot<4;slot++){
                var receiver=PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if(receiver?.Owner==null)continue;
                string reason=!receiver.Alive?"队友倒地，请先救援":!GuardCombat.Available?"掩护选敌接口未就绪":null;
                MarkedPosition point=null;
                if(reason==null&&!SearchTraversal.InScope(receiver.CourseNode,source?.CourseNode,1))reason="队友不在当前Zone同层同维度";
                if(reason==null){var direction=receiver.TargetLookDir;direction.y=0;if(direction.sqrMagnitude<.01f)direction=Vector3.forward;
                    point=Point(source,receiver.Position-direction.normalized*1.5f,receiver.CourseNode,direction);
                    if(point==null)reason="队友附近缺少安全固定站位，请换位置重开菜单";
                }
                if(point!=null)snapshot.Covers[slot]=new(GuardDutyKind.Cover,point,3,receiver);
                covers.Add(new(slot,receiver.Owner.Pointer.ToInt64(),receiver.PlayerName,reason));
            }
            snapshot.Menu=new(rooms,entrance,covers);return snapshot;
        }
        internal static void Issue(PlayerAIBot bot,GuardTargetSnapshot saved,int handler,int pointId,float radius,int receiverSlot,long receiverOwner){
            if(!SNet.IsMaster||GameStateManager.CurrentStateName!=eGameStateName.InLevel||saved==null){ItemTaskAdapter.Feedback("守卫目标快照已失效");return;}
            GuardDutySpec spec;
            if(handler==53){
                if(!saved.Covers.TryGetValue(receiverSlot,out var capture)||!capture.Valid||capture.ReceiverOwner.ToInt64()!=receiverOwner
                    ||capture.Receiver?.Alive!=true||bot?.Agent?.Pointer==capture.ReceiverAgent||!GuardCombat.Available){ItemTaskAdapter.Feedback("掩护队友身份／固定站位已改变或不能掩护自己，请重开菜单");return;}
                spec=new(GuardDutyKind.Cover,capture.Anchor,radius,capture.Receiver);
            }else{
                if(handler!=52&&handler!=54||!saved.Points.TryGetValue(pointId,out var point)||handler==52&&pointId is not (0 or 1)||handler==54&&pointId!=2){ItemTaskAdapter.Feedback("固定房间／入口目标无效，请重开菜单");return;}
                spec=new(handler==52?GuardDutyKind.Room:GuardDutyKind.Entrance,point,radius);
            }
            if(PositionMarks.Reason(spec.Anchor) is string reason){ItemTaskAdapter.Feedback(reason);return;}
            MovementOrders.GuardAt(bot,spec);
        }
    }
}