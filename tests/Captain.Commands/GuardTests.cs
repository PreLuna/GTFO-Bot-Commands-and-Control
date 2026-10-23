using System;
using System.Reflection;
using Player;
using Enemies;
using UnityEngine;
using PreLuna.Captain.Tasks;
using AI=PreLuna.CoopBotsCustom.Combat.CombatAdapter;
static class GuardTests {
 static int checks;static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
 static PlayerAIBot bot;static PlayerAgent host;static RootPlayerBotAction root;
 static void Reset(){
  FocusOrders.Reset();GuardCombat.Reset();AI.States.Clear();Time.time=0;BuilderReset();PlayerManager.Current=new();
  host=new(){PlayerSlotIndex=0,PlayerName="host",Owner=new(){IsBot=false},TargetLookDir=Vector3.forward};PlayerManager.Current.Slots[0]=host;
  bot=new(){Agent=new(){PlayerSlotIndex=1,PlayerName="bot"}};bot.Agent.Bot=bot;PlayerManager.Current.Slots[1]=bot.Agent;
  root=new(){m_bot=bot};bot.m_rootAction=new(){ActionBase=root};bot.Agent.Inventory=bot.Inventory;
  MovementOrders.Active=null;MovementOrders.Lease=true;MovementOrders.Issues=0;BotTaskBridge.Auto=false;BotTaskBridge.Permit=true;
  Physics.Capsule=false;PlayerAIBot.Snap=true;PlayerAIBot.Offset=default;AIGraph.AIG_CourseNode.Found=true;AIGraph.AIG_CourseNode.Next=new();
  ItemTaskAdapter.Blocked.Clear();PreLuna.CoopBotsCustom.Reliability.SyncMelee.session=null;
 }
 static void BuilderReset()=>LevelGeneration.Builder.CurrentFloor=new();
 static EnemyAgent Enemy(float x,float z,int room=1,int mode=1)=>new(){Position=new(x,0,z),AI=new(){Mode=mode},CourseNode=new(){Room=room}};
 static MarkedPosition Point()=>new(default,new(),host.CourseNode,LevelGeneration.Builder.CurrentFloor.Pointer,host.Owner.Pointer,Time.time,Vector3.forward);
 static void Cache(params EnemyAgent[] enemies){var s=new PreLuna.CoopBotsCustom.Combat.StateData{Count=enemies.Length,SightAt=Time.time};foreach(var e in enemies)s.Sight.Add(new(){Enemy=e});AI.States[bot.Pointer]=s;}
 static void Select()=>typeof(GuardCombat).GetMethod("SelectTarget",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root});
 static void FocusSelect()=>typeof(FocusOrders).GetMethod("SelectTarget",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root});
 static void Policy(){
  Check(GuardPolicy.Radius(float.NaN)==3&&GuardPolicy.Radius(500)==3,"无效半径回落到3米");
  foreach(float r in new[]{2f,3f,5f}){Check(GuardPolicy.Radius(r)==r,"支持三档半径");Check(GuardPolicy.InBound(true,r,1.25f,r),"边界包含");Check(!GuardPolicy.InBound(true,r+.01f,0,r),"水平越界");Check(!GuardPolicy.InBound(false,0,0,r),"不能越房间");}
  Check(!GuardPolicy.InBound(true,0,2,3)&&!GuardPolicy.InBound(true,float.NaN,0,3),"高度与异常坐标拒绝");
  Check(GuardPolicy.Score(GuardDutyKind.Room,false,false,true,2,1)==float.MinValue,"其他房间不附加选敌");
  Check(GuardPolicy.Score(GuardDutyKind.Entrance,true,false,true,2,-1)==float.MinValue,"入口背后无附加优先");
  Check(GuardPolicy.Score(GuardDutyKind.Cover,true,true,false,2,1)==float.MinValue,"队友离开范围不追踪");
  Check(GuardPolicy.Hold(true,.2f,10,500)&&!GuardPolicy.Hold(false,.2f,10,500),"稳定窗口只保护有效敌人");
  Check(GuardPolicy.Hold(true,2,100,110)&&!GuardPolicy.Hold(true,2,100,200),"明显收益才更新目标");
 }
 static void Targets(){
  Reset();var snapshot=GuardTargets.Capture(host,Point());Check(snapshot.Points.Count==3&&snapshot.Covers.Count==2,"捕获当前房间、标记和两名队友固定点");
  GuardTargets.Issue(bot,snapshot,52,0,5,-1,0);Check(MovementOrders.Active.Kind==GuardDutyKind.Room&&MovementOrders.Active.Radius==5,"守卫固定房间下达");
  GuardTargets.Issue(bot,snapshot,54,2,2,-1,0);Check(MovementOrders.Active.Kind==GuardDutyKind.Entrance&&MovementOrders.Active.Anchor.Direction.z==1,"入口共享标记朝向");
  int n=MovementOrders.Issues;GuardTargets.Issue(bot,snapshot,54,0,3,-1,0);Check(MovementOrders.Issues==n,"入口不接受房间参数冒用");
  GuardTargets.Issue(bot,snapshot,53,-1,3,0,host.Owner.Pointer.ToInt64());var duty=MovementOrders.Active;
  Check(duty.Kind==GuardDutyKind.Cover&&duty.Anchor.Point.z==-1.5f,"掩护捕获队友身后固定点");
  host.Position=new(5,0,0);Check(duty.Anchor.Point.z==-1.5f,"队友移动不重算站位");
  host.Position=new(15,0,0);Check(!duty.ReceiverInBound&&duty.Valid,"离开半径保持身份、不追踪");host.Position=default;
  host.Alive=false;Check(!duty.ReceiverInBound&&duty.Valid,"倒地交还救援不删除固定点");host.Alive=true;
  n=MovementOrders.Issues;GuardTargets.Issue(bot,snapshot,53,-1,3,1,bot.Agent.Owner.Pointer.ToInt64());Check(MovementOrders.Issues==n,"不能掩护自己");
  PlayerManager.Current.Slots[0]=new(){Owner=new(){IsBot=false}};Check(!duty.Valid,"队友槽位替换使身份失效");PlayerManager.Current.Slots[0]=host;
  n=MovementOrders.Issues;Time.time=121;GuardTargets.Issue(bot,snapshot,52,0,3,-1,0);Check(MovementOrders.Issues==n,"旧菜单快照过期拒绝");
  Reset();var mark=Point();mark.Node.Room=2;snapshot=GuardTargets.Capture(host,mark);Check(snapshot.Menu.Rooms[1].Blocked!=null&&!snapshot.Points.ContainsKey(1),"房间守卫拒绝其他房间站位");
  Check(snapshot.Menu.Entrance.Blocked==null,"入口允许当前Zone的标记，仍有独立固定房间");
  Physics.Capsule=true;snapshot=GuardTargets.Capture(host,null);Check(snapshot.Menu.Rooms[0].Blocked!=null&&snapshot.Covers.Count==0,"站位被几何遮挡不下达");
  Reset();AIGraph.AIG_CourseNode.Next.Room=3;snapshot=GuardTargets.Capture(host,null);Check(snapshot.Menu.Rooms[0].Blocked!=null,"导航吸附不能跨房间");
 }
 static void Combat(){
  Reset();Check(GuardCombat.Available,"真实守卫缓存接口已绑定");var front=Enemy(0,5);var otherRoom=Enemy(0,2,2);var sleeping=Enemy(0,1,1,4);Cache(front,otherRoom,sleeping);
  MovementOrders.Active=new(GuardDutyKind.Room,Point(),3);Select();Check(root.m_attackAction.TargetAgent==front,"只优先当前房间已激怒可见敌人");Check(bot.Requests==0,"选敌不创建攻击／移动动作");
  Check(!BotTaskBridge.Auto&&root.m_attackAction.TargetAgent==front,"自己的驻守租约不误判为不能战斗");
  var state=AI.States[bot.Pointer];float since=state.SelectedAt;Time.time=.2f;state.SightAt=Time.time;Select();Check(state.SelectedAt==since,"原选择时间不会逐帧重置武器窗口");
  Time.time=2;state.SightAt=0;root.m_attackAction.TargetAgent=otherRoom;Select();Check(root.m_attackAction.TargetAgent==otherRoom,"过期扫描缓存不重新找敌人");
  state.SightAt=Time.time;state.Sight[0].Visible=false;Select();Check(root.m_attackAction.TargetAgent==otherRoom,"不可见不强制选敌");state.Sight[0].Visible=true;bot.Line=false;Select();Check(root.m_attackAction.TargetAgent==otherRoom,"无视线不穿墙");bot.Line=true;
  ItemTaskAdapter.Blocked.Add(bot.Pointer);Select();Check(root.m_attackAction.TargetAgent==otherRoom,"交互保护");ItemTaskAdapter.Blocked.Clear();
  MovementOrders.Lease=false;Select();Check(root.m_attackAction.TargetAgent==otherRoom,"暂停／扫描让位释放租约后不改选敌");MovementOrders.Lease=true;
  BotTaskBridge.Permit=false;Select();Check(root.m_attackAction.TargetAgent==otherRoom,"紧急仲裁拒绝时不改选敌");BotTaskBridge.Permit=true;
  Reset();var threat=Enemy(0,5);var incidental=Enemy(0,2);threat.AI.Target.m_agent=host;Cache(threat,incidental);MovementOrders.Active=new(GuardDutyKind.Cover,Point(),3,host);Select();Check(root.m_attackAction.TargetAgent==threat,"掩护优先攻击队友的敌人");
  host.Position=new(20,0,0);root.m_attackAction.TargetAgent=incidental;Select();Check(root.m_attackAction.TargetAgent==incidental,"队友离开不无限追敌");
  Reset();var behind=Enemy(0,-2);var ahead=Enemy(0,5);Cache(behind,ahead);MovementOrders.Active=new(GuardDutyKind.Entrance,Point(),3);Select();Check(root.m_attackAction.TargetAgent==ahead,"入口前方优先，保留原版背后自保");
  FocusOrders.Issue(bot,behind);FocusSelect();Select();Check(FocusOrders.Owns(bot)&&root.m_attackAction.TargetAgent==behind,"明确集火优先于入口守卫，自己的驻守租约可战斗");
  FocusOrders.RestoreQuiet(bot);Select();Check(root.m_attackAction.TargetAgent==ahead,"解除集火恢复固定职责选敌");
  ahead.Alive=false;root.m_attackAction.TargetAgent=behind;Select();Check(root.m_attackAction.TargetAgent==behind,"敌人死亡后不保留附加目标");
  GuardCombat.Reset();MovementOrders.Active=null;Select();Check(root.m_attackAction.TargetAgent==behind,"生命周期重置不改其他原版目标");
 }
 static void Main(){FocusOrders.Install(new());GuardCombat.Install(new());SharedMeleeBridge.Install(new());Policy();Targets();Combat();Console.WriteLine("PASS "+checks+" guard policy/target/combat adapter checks; movement uses separately tested StationRunner. Native game not executed.");}
}
