using System;
using System.Reflection;
using LevelGeneration;
using Player;
using UnityEngine;
using PreLuna.Captain.Tasks;
static class ItemTargetTests
{
    static int checks;
    static void Check(bool value,string text) { checks++;if(!value)throw new Exception(text); }
    static ItemInLevel Item(float x) {
        var item=new ItemInLevel {Data=new(){slot=InventorySlot.Consumable,itemID_gearCRC=30}};
        item.transform.position=new(x);item.Sync=new(){Item=item,State=new(){status=ePickupItemStatus.PlacedInLevel},Custom=new(){ammo=1}};
        item.PickupInteraction=new(){Root=new(),m_colliderToOwn=new(){bounds=new(){center=new(x)}}};
        item.PickupInteraction.Root.position=new(x);return item;
    }
    static void Reset() {UnityEngine.Object.All.Clear();ItemWorldIndex.Reset();Physics.Blocked=Physics.BodyBlocked=false;Physics.HitCollider=null;Physics.BodyTest=null;}
    static void Main() {
        Reset();var player=new PlayerAgent();var item=Item(2);
        item.PickupInteraction.IsSelected=true;item.PickupInteraction.Parents[typeof(ItemInLevel)]=item;
        player.Interaction.m_currentSelected=true;player.Interaction.m_bestSelectedInteract=item.PickupInteraction;
        Physics.Blocked=true;
        Check(ItemTargetResolver.Target(player)?.Pointer==item.Pointer,"原版选中对象优先，第一条广义射线被柜体命中不丢失物品");
        Check(ItemWorldIndex.Items.Count==1,"选中物品补登记，不依赖已有缓存");
        Reset();player=new();item=Item(2);item.PickupInteraction.IsSelected=true;
        item.PickupInteraction.Root.Children.Add(item);player.Interaction.m_currentSelected=true;player.Interaction.m_bestSelectedInteract=item.PickupInteraction;
        Check(ItemTargetResolver.Target(player)?.Pointer==item.Pointer,"独立交互根节点反查真实子物品");
        Reset();player=new();item=Item(10);Physics.HitCollider=item.PickupInteraction.m_colliderToOwn;
        Check(ItemTargetResolver.Target(player)?.Pointer==item.Pointer,"独立拾取碰撞体通过归属匹配识别");
        Check(ItemInteractionGeometry.Visible(player.EyePosition,item),"目标自己的碰撞体不当作墙");
        Reset();player=new();item=Item(10);Physics.HitCollider=new();Physics.Blocked=true;
        Check(ItemTargetResolver.Target(player)==null,"真实墙或柜门遮挡不能远距离指向拾取");
        Reset();player=new();item=Item(10);item.PickupInteraction.m_colliderToOwn.bounds=new(){center=new(){x=10,z=.2f}};
        Check(ItemTargetResolver.Target(player)?.Pointer==item.Pointer,"细小交互点在30厘米准星容差内可识别");
        item.PickupInteraction.m_colliderToOwn.bounds=new(){center=new(){x=10,z=.4f}};
        Check(ItemTargetResolver.Target(player)==null,"偏离准星的物品不误选");
        item.PickupInteraction.m_colliderToOwn.bounds=new(){center=new(10)};item.container=new();item.container.m_core.Status=eResourceContainerStatus.Closed;
        Check(ItemTargetResolver.Target(player)==null,"关闭容器中的物品不作为远距离候选");
        Reset();item=Item(10);item.gameObject.activeInHierarchy=false;
        Check(ItemWorldIndex.Items.Count==1,"首次登记包含当前隐藏的场景物品");
        item.gameObject.activeInHierarchy=true;player=new();Check(ItemTargetResolver.Target(player)?.Pointer==item.Pointer,"柜内物品重新启用后可以识别");
        typeof(ItemWorldIndex).GetMethod("Removed",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{item});
        Check(ItemWorldIndex.Items.Count==0,"物品销毁后移除缓存");
        Reset();player=new();item=Item(10);
        Check(ItemInteractionGeometry.Standing(player,ItemInteractionGeometry.Point(item),item,out var position),"可见侧面存在安全站位");
        Check(Math.Abs(position.x-8.5f)<.01f,"优先沿来路选择物品前1.5米，而非物品中心");
        Physics.BodyBlocked=true;
        Check(!ItemInteractionGeometry.Standing(player,ItemInteractionGeometry.Point(item),item,out _),"所有身体空间受阻时拒绝站位");
        Physics.BodyBlocked=false;Physics.Blocked=true;
        Check(!ItemInteractionGeometry.Standing(player,ItemInteractionGeometry.Point(item),item,out _),"所有候选视线遮挡时拒绝站位");
        Physics.Blocked=false; Physics.BodyTest=p=>Math.Abs(p.x-10)<.1f || Math.Abs(p.z)<.1f;
        Check(ItemInteractionGeometry.Standing(player,ItemInteractionGeometry.Point(item),item,out position),"正面与侧面被柜门占用时可选安全斜向站位");
        Check(Math.Abs(position.x-10)>.1f && Math.Abs(position.z)>.1f,"斜向站位确实绕过受阻的四个正交方向");
        Console.WriteLine("PASS: "+checks+" 项实际目标解析／交互几何／物品缓存源码断言；碰撞与对象层级由受控模型代替。");
    }
}
