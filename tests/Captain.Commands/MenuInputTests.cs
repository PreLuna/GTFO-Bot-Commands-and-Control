using System;
using System.Collections.Generic;
using System.Reflection;
using PreLuna.Captain.Tasks;
namespace LevelGeneration { class ItemInLevel { } }
enum eGameStateName { Lobby, InLevel }
enum eFocusState { FPS }
enum InputAction { None = 0, Move = 1, Use = 6, Fire = 8, Reload = 11, Melee = 12, Weapon = 23, Flashlight = 32, Chat = 56, Menu = 64 }
static class GameStateManager { public static eGameStateName CurrentStateName = eGameStateName.InLevel; }
class InputMapper { }
class PlayerInventoryLocal { }
class PUI_CommunicationButton { public Player.CommunicationNode Node; }
class PUI_CommunicationMenu
{
    public bool m_active; public IntPtr Pointer = new(1); public Player.CommunicationMenu m_menu = new();
    public int Selections, RenderedButtons, NativeToolCalls, m_lastActiveButtonIndex; public bool FailRender; public Action BeforeRender;
    public Player.CommunicationNode Title;
    public Il2CppSystem.Collections.Generic.List<PUI_CommunicationButton> m_buttons = new();
    public bool EmptyNativeTools, BypassChooseHook;
    public void SetActive(bool value) {
        MenuInputTests.Call("BeforeActive", this, value);
        m_active = value; m_menu.m_controller.Reset();
        if(value) {
            BeforeRender?.Invoke();
            if(FailRender) { MenuInputTests.Call("ActiveFinalizer",this,value,new ArgumentOutOfRangeException()); return; }
            TryUpdateCategoryButtons();
        }
    }
    public void SetHighlight(int i) { }
    public void MoveNext(int i) {
        Selections++;
        if(!BypassChooseHook && !(bool)MenuInputTests.Call("ChooseButton",this,i))return;
        var node=m_buttons[i].Node;
        m_menu.MoveNext(node);
        if(node.IsLastNode){NativeToolCalls++;SetActive(false);return;}
        TryUpdateCategoryButtons();
        if(m_lastActiveButtonIndex<0)SetActive(false);else SetTitleTextFromCategory(node);
    }
    public void TryUpdateCategoryButtons() {
        RenderedButtons=0;m_buttons.Clear();
        foreach(var node in m_menu.CurrentNode.m_ChildNodes){
            int count=node.Script==Player.CommunicationNode.ScriptType.UseTool?(EmptyNativeTools?0:3):1;
            for(int i=0;i<count;i++)m_buttons.Add(new(){Node=node});
            RenderedButtons+=count;
        }
        m_lastActiveButtonIndex=RenderedButtons-1;
    }
    public void SetTitleTextFromCategory(Player.CommunicationNode node){Title=node;}
    public void NativeSelectTool(int index) {
        if(!m_active || index>=RenderedButtons)throw new Exception("Native dynamic tool button missing");
        MoveNext(index);
    }
}
namespace Il2CppSystem.Collections.Generic { class List<T> : System.Collections.Generic.List<T> { } }
namespace Player
{
    class CommunicationNode { static long next=1; public IntPtr Pointer = new(next++); public enum ScriptType {None,Call,UseTool,CarryMissionObject} public ScriptType Script; public uint TextId; public bool IsLastNode; public Il2CppSystem.Collections.Generic.List<CommunicationNode> m_ChildNodes = new(); }
    class Controller { public CommunicationNode m_rootNode; public CommunicationMenu Menu; public void Reset(){Menu.CurrentNode=m_rootNode;} }
    class CommunicationMenu {
        public CommunicationNode CurrentNode; public Controller m_controller;
        public CommunicationMenu(){m_controller=new Controller {Menu=this};}
        public void MoveNext(CommunicationNode node){CurrentNode=node;}
    }
}
namespace UnityEngine
{
    static class Time { public static int frameCount = 100; }
    enum KeyCode { Escape = 27, Alpha1 = 49, Keypad1 = 257, F6 = 287 }
    static class Input { public static HashSet<KeyCode> Down = new(); public static bool GetKeyDown(KeyCode key) => Down.Contains(key); }
}
namespace HarmonyLib
{
    class Harmony { public Harmony(string name) { } public void Patch(object method, HarmonyMethod prefix = null, HarmonyMethod finalizer=null) { } public void UnpatchSelf() { } }
    class HarmonyMethod { public int priority; public HarmonyMethod(Type t, string m) { } }
    static class AccessTools { public static object Method(Type t, string m, Type[] args = null) => new(); }
    static class Priority { public const int First = 800, Last=0; }
}
namespace PreLuna.Captain { static class Plugin { public const string Guid = "test"; public static Logger Logger = new(); } class Logger { public void LogInfo(string text) { } public void LogWarning(string text){} } }
namespace PreLuna.Captain.Tasks {
    static class AutoPickupAdapter { public static bool CanAnswer = true; }
    static class ItemTaskAdapter {
        public static bool ControlMenuRequired;
        public static int Feedbacks; public static void Feedback(string message){Feedbacks++;}
        public static LevelGeneration.ItemInLevel MenuTarget, LastTarget;
        public static Player.CommunicationNode PreparedRoot, Saved; public static PUI_CommunicationMenu Owner; public static int Dispatches;
        public static bool PrepareMenu(PUI_CommunicationMenu menu){
            if(!AutoPickupAdapter.CanAnswer)return false;
            Owner=menu; Saved=menu.m_menu.m_controller.m_rootNode; menu.m_menu.m_controller.m_rootNode=PreparedRoot; return true;
        }
        public static void ReleaseMenu(PUI_CommunicationMenu expected=null){
            if(expected!=null&&Owner?.Pointer!=expected.Pointer)return;
            if(Owner!=null)Owner.m_menu.m_controller.m_rootNode=Saved; Owner=null; Saved=null; MenuTarget=null;
        }
        public static bool Dispatch(uint id,LevelGeneration.ItemInLevel target=null,bool captured=false){Dispatches++;LastTarget=target;return true;}
        public static Action CaptureDispatch(uint id){var target=MenuTarget;return()=>Dispatch(id,target,true);}
    }
}
static class MenuInputTests
{
    static int count;
    static void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
    public static object Call(string name, params object[] args) => typeof(CaptainMenuInput).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static Player.CommunicationNode Node(uint id,string text="")=>new(){TextId=id};
    static void Pagination() {
        for(int capacity=2;capacity<=9;capacity++){
            CaptainMenuTree.Reset();
            var stock=Node(77); for(uint i=0;i<10;i++)stock.m_ChildNodes.Add(Node(100+i));
            var root=Node(CaptainMenuTree.RootId);
            for(uint slot=0;slot<4;slot++) {
                var bot=Node(CaptainMenuTree.RootId+16+slot*16);
                for(uint command=1;command<=7;command++)bot.m_ChildNodes.Add(new(){TextId=bot.TextId+command,IsLastNode=true});
                root.m_ChildNodes.Add(bot);
            }
            var original=Node(CaptainMenuTree.OriginalId);original.m_ChildNodes=stock.m_ChildNodes;root.m_ChildNodes.Add(original);
            uint next=CaptainMenuTree.PageStart;CaptainMenuTree.Paginate(root,capacity,Node,ref next);
            var visited=new HashSet<Player.CommunicationNode>();var ids=new HashSet<uint>();var queue=new Queue<Player.CommunicationNode>();queue.Enqueue(root);
            while(queue.Count>0){var node=queue.Dequeue();if(!visited.Add(node)||!CaptainMenuTree.Owned(node.TextId))continue;
                Check(node.m_ChildNodes.Count<=capacity,"所有队长页面遵守实际按钮容量 "+capacity);
                if(node.IsLastNode)ids.Add(node.TextId);foreach(var child in node.m_ChildNodes)queue.Enqueue(child);
                if(CaptainMenuTree.Page(node.TextId)&&node.m_ChildNodes.Count==0)
                    Check(CaptainMenuTree.Target(node)!=node,"返回第一页使用导航映射，原生节点图没有回环");
            }
            Check(ids.Count==28,"分页不丢失四个机器人共28条指令");
            Check(stock.m_ChildNodes.Count==10&&stock.m_ChildNodes[3].TextId==103,"原版与Chatter根节点和固定顺序不变");
        }
    }
    static void Main()
    {
        var controlled = CaptainMenuPlan.Build(new[]{new CaptainMenuBot(2,200,"当前接管机器人",true)}, "机器人中键物品", "", "", true,false,false,
            openTarget:new("机器人中键普通门",null,false,0),controlMode:true);
        var controlledNodes=new Queue<CaptainMenuPlanNode>();controlledNodes.Enqueue(controlled);var controlledLeaves=new List<CaptainMenuLeaf>();
        while(controlledNodes.Count>0){var node=controlledNodes.Dequeue();if(node.Leaf!=null)controlledLeaves.Add(node.Leaf);foreach(var child in node.Children)controlledNodes.Enqueue(child);}
        Check(controlled.Label.Contains("接管"),"Q沿用队长菜单并显示接管上下文");
        Check(controlledLeaves.Count==5&&controlledLeaves.Exists(l=>l.Handler==1)&&controlledLeaves.Exists(l=>l.Handler==10)
            &&controlledLeaves.Exists(l=>l.Handler==9)&&controlledLeaves.Exists(l=>l.Handler==43)&&controlledLeaves.Exists(l=>l.Handler==55),"第四批菜单保留拾取开启，增加击杀、集火和退出驻守");
        Check(controlledLeaves.TrueForAll(l=>l.Slot==2&&l.Owner==200),"执行人固定当前机器人，不出现其他机器人或全队提交");
        Check(controlledLeaves.FindAll(l=>l.Handler==1||l.Handler==10).TrueForAll(l=>l.Blocked==null),"有效机器人标记可以从原有菜单提交");
        AutoPickupAdapter.CanAnswer=false;ItemTaskAdapter.ControlMenuRequired=true;
        Check(!(bool)Call("BeforeActive",new PUI_CommunicationMenu(),true),"接管菜单构建失败时禁止回退到本体准星原版菜单");
        AutoPickupAdapter.CanAnswer=true;ItemTaskAdapter.ControlMenuRequired=false;
        Framework();
        Pagination();
        var killPlan=CaptainMenuPlan.Build(new[]{new CaptainMenuBot(1,11,"弱伤害",true,KillBlocked:"单人伤害不足：12/40"),
            new CaptainMenuBot(2,12,"驻守机器人",true,KillNote:"临时借调，保留原任务"),new CaptainMenuBot(3,13,"倒地",false)},
            "","","",false,false,false,"沉睡目标",true);
        var killQueue=new Queue<CaptainMenuPlanNode>();killQueue.Enqueue(killPlan);var killLeaves=new List<CaptainMenuPlanNode>();
        while(killQueue.Count>0){var node=killQueue.Dequeue();if(node.Leaf?.Handler==9)killLeaves.Add(node);foreach(var child in node.Children)killQueue.Enqueue(child);}
        Check(killLeaves.Count==3,"指定击杀保留每名机器人的独立资格行，不新增未实现全队入口");
        Check(killLeaves[0].Leaf.Blocked=="单人伤害不足：12/40"&&killLeaves[0].Label.Contains("12/40"),"真实菜单显示单人伤害拒绝原因");
        Check(killLeaves[1].Leaf.Blocked==null&&killLeaves[1].Label.Contains("临时借调"),"忙于可暂停职责的机器人仍可提交借调");
        Check(killLeaves[2].Leaf.Blocked.Contains("倒地"),"倒地机器人保持不可参与");
        var stock=Node(12); var root=Node(CaptainMenuTree.RootId); var bot=Node(CaptainMenuTree.RootId+16);
        bot.m_ChildNodes.Add(new(){TextId=bot.TextId+1,IsLastNode=true}); root.m_ChildNodes.Add(bot);
        var original=Node(CaptainMenuTree.OriginalId); root.m_ChildNodes.Add(original);
        var menu=new PUI_CommunicationMenu();menu.m_menu.m_controller.m_rootNode=stock;ItemTaskAdapter.PreparedRoot=root;
        var chosenItem=new LevelGeneration.ItemInLevel();ItemTaskAdapter.MenuTarget=chosenItem;
        menu.BeforeRender=()=>Check(CaptainMenuInput.Blocks(InputAction.Weapon),"生成按钮前已经获得输入所有权");
        menu.SetActive(true);
        Check(menu.m_menu.CurrentNode==root&&CaptainMenuInput.Active,"首次打开直接显示队长首页");
        foreach(int a in new[]{6,8,11,12,23,24,25,26,27,28,29,30,31,32,65,66,67})Check(CaptainMenuInput.Blocks((InputAction)a),"屏蔽物品操作 "+a);
        foreach(int a in new[]{1,2,3,4,5,9,10,55,56,63,64})Check(!CaptainMenuInput.Blocks((InputAction)a),"保留移动视角聊天和通讯 "+a);
        Check(!(bool)Call("InventoryInput"),"阻止原生背包直接读取切枪和手电按键");
        UnityEngine.Input.Down.Add(UnityEngine.KeyCode.Alpha1);
        Check(!(bool)Call("Select",menu)&&menu.Selections==1&&menu.m_menu.CurrentNode==bot,"数字选择机器人一次");
        Call("Select",menu);Check(menu.Selections==1,"同一帧重复调用不再选择");
        UnityEngine.Time.frameCount++;
        Call("Select",menu);
        Check(ItemTaskAdapter.Dispatches==1&&!menu.m_active,"末级指令直接分发一次并关闭");
        Check(ItemTaskAdapter.LastTarget==chosenItem,"菜单关闭后仍把打开时锁定的物品传给命令");
        Check(menu.m_menu.CurrentNode==stock&&menu.m_menu.m_controller.m_rootNode==stock,"关闭前归还原版根节点且重置为原版节点");
        Check(CaptainMenuInput.Blocks(InputAction.Weapon),"原版已重置节点仍保留关闭帧保护");
        UnityEngine.Time.frameCount++;Check(CaptainMenuInput.Blocks(InputAction.Flashlight),"关闭下一帧继续消费输入");
        UnityEngine.Time.frameCount++;Check(!CaptainMenuInput.Blocks(InputAction.Weapon)&&(bool)Call("InventoryInput"),"消费完成恢复原生背包操作");
        UnityEngine.Input.Down.Clear();menu.SetActive(true);menu.m_menu.CurrentNode=original;
        Check(CaptainMenuInput.Active&&CaptainMenuInput.Blocks(InputAction.Weapon),"原版通讯子页仍属于同一输入会话");
        Check((bool)Call("Select",menu)&&(bool)Call("Highlight"),"原版语音子页继续使用原版选择与高亮");
        menu.SetActive(false);UnityEngine.Time.frameCount+=3;
        menu.FailRender=true;menu.SetActive(true);
        Check(!menu.m_active&&!CaptainMenuInput.Active&&menu.m_menu.m_controller.m_rootNode==stock,"打开异常关闭菜单并恢复原版树");
        UnityEngine.Time.frameCount+=3;Check(!CaptainMenuInput.Blocks(InputAction.Weapon),"异常清理不会永久屏蔽输入");
        menu.FailRender=false;AutoPickupAdapter.CanAnswer=false;menu.BeforeRender=null;menu.SetActive(true);
        Check(!CaptainMenuInput.Active&&!CaptainMenuInput.Blocks(InputAction.Weapon),"聊天、终端及失能期间不建立输入会话");
        AutoPickupAdapter.CanAnswer=true;CaptainMenuInput.Reset();
        FrameworkNavigation();
        ReservedViewKey();
        Console.WriteLine($"PASS: {count} 项实际输入会话/分页断言；Unity原生UI和库存未执行。");
    }
    static void ReservedViewKey()
    {
        CaptainMenuInput.Reset(); UnityEngine.Input.Down.Clear(); UnityEngine.Time.frameCount+=3;
        var stock=Node(77); var root=Node(CaptainMenuTree.RootId);
        var menu=new PUI_CommunicationMenu();menu.m_menu.m_controller.m_rootNode=stock;ItemTaskAdapter.PreparedRoot=root;
        menu.SetActive(true); UnityEngine.Time.frameCount++;
        UnityEngine.Input.Down.Add(UnityEngine.KeyCode.F6);
        Check(!(bool)Call("Select",menu)&&menu.m_active&&menu.Selections==0,"F6 留给第三人称，不再硬编码关闭队长菜单或选择指令");
        UnityEngine.Input.Down.Clear();UnityEngine.Input.Down.Add(UnityEngine.KeyCode.Escape);UnityEngine.Time.frameCount++;
        Check(!(bool)Call("Select",menu)&&!menu.m_active&&!CaptainMenuInput.Active,"Esc 仍关闭菜单并释放队长会话");
        Check(menu.m_menu.m_controller.m_rootNode==stock,"Esc 关闭后恢复原版通讯树");
        UnityEngine.Input.Down.Clear();UnityEngine.Time.frameCount+=3;
        Check(!CaptainMenuInput.Blocks(InputAction.Weapon),"关闭保护帧结束后恢复装备输入");
        CaptainMenuInput.Reset();
    }
    static void Framework()
    {
        var guards=CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,11,"bot0",true),new CaptainMenuBot(1,12,"bot1",false)},
            "","","",true,true,true,guards:new(new[]{new CaptainMenuGuardPoint(0,"当前位置",null),new CaptainMenuGuardPoint(1,"房间中键","无标记")},
            new(2,"门旁站位",null),new[]{new CaptainMenuGuardCover(0,11,"bot0",null),new CaptainMenuGuardCover(2,22,"host",null)}));
        var room=guards.Children[3].Children[3];
        Check(room.Children[0].Children[0].Children[0].Leaf is {Handler:52,GuardPoint:0,GuardRadius:2,Slot:0,Owner:11,Blocked:null},"守卫房间：任务、真实站位、半径、机器人");
        Check(room.Children[1].Children[2].Children[0].Leaf is {Handler:52,GuardRadius:5,Blocked:"无标记"},"失效房间站位有明确拒绝原因");
        var cover=guards.Children[4].Children[2];
        Check(cover.Children[0].Children[0].Children[0].Leaf.Blocked=="不能掩护自己","不能选自己掩护自己");
        Check(cover.Children[1].Children[1].Children[0].Leaf is {Handler:53,ReceiverSlot:2,ReceiverOwner:22,GuardRadius:3,Blocked:null},"掩护绑定队友身份、半径与执行人");
        Check(cover.Children[1].Children[0].Children[1].Leaf.Blocked!=null,"倒地机器人不能接受掩护");
        Check(guards.Children[4].Children[3].Children[0].Children[2].Children[0].Leaf is {Handler:54,GuardPoint:2,GuardRadius:5},"入口防守先标记再定半径最后选人");
        void NoPlaceholders(CaptainMenuPlanNode node){Check(!node.Label.Contains("【待实现】")&&!node.Label.Contains("【预览】"),"不展开自动／全队伪执行页面");foreach(var child in node.Children)NoPlaceholders(child);}
        NoPlaceholders(guards);
        var expected = new[] {8,6,0,12,7}; int commands=0;
        var codes=new HashSet<string>();
        for(int i=0;i<CaptainCommandCatalog.Groups.Length;i++) {
            Check(CaptainCommandCatalog.Groups[i].Commands.Length==expected[i],"五组指令完整对应规划");
            foreach(var spec in CaptainCommandCatalog.Groups[i].Commands) {commands++;Check(codes.Add(spec.Code),"任务代码唯一");}
        }
        Check(commands==33,"移除地面放下，保留放回容器和原版部署入口");
        var rescueMenu = CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,11,"bot0",true),new CaptainMenuBot(1,12,"bot1",false)},
            "item","box","point",true,true,true,receivers:new[]{new CaptainMenuReceiver(2,13,"倒地玩家",false,false,true),
                new CaptainMenuReceiver(3,14,"存活玩家",true),new CaptainMenuReceiver(1,12,"倒地机器人",false,true,true)});
        var rescue = rescueMenu.Children[3].Children[6];
        Check(rescue.Children.Count==3,"指定救援仅列真实倒地队友及返回");
        Check(rescue.Children[0].Children[0].Leaf is {Handler:39,Slot:0,Owner:11,ReceiverSlot:2,ReceiverOwner:13,Blocked:null},"先选救援目标，再选执行机器人，身份完整绑定");
        Check(rescue.Children[0].Children[1].Leaf is {Handler:39,Blocked:not null},"倒地机器人不能承担救援");
        Check(rescue.Children[1].Children[0].Leaf.ReceiverSlot==1,"倒地机器人也是合法救援对象");
        var allocation = rescueMenu.Children[3].Children[7];
        Check(allocation.Children[0].Leaf is {Handler:40,Slot:-1}&&allocation.Children[1].Leaf is {Handler:41,Slot:-1},"多人分配与取消是全局队列入口，无多余选人层");
        CaptainMenuPlanNode TaskNode(CaptainMenuPlanNode root,string code) {
            foreach(var group in root.Children)foreach(var task in group.Children)if(task.Children.Exists(n=>n.Leaf?.Code==code)||task.Children.Exists(n=>n.Children.Exists(x=>x.Leaf?.Code==code)))return task;
            throw new Exception("missing task "+code);
        }
        var tools = CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,11,"工具包机器人",true,SupplyPack:"工具补给",SupplyItem:601,HasToolPack:true),
            new CaptainMenuBot(1,12,"普通弹药包机器人",true,SupplyPack:"弹药包",SupplyItem:602),new CaptainMenuBot(2,13,"倒地工具包机器人",false,HasToolPack:true)},
            "item","box","point",true,true,true,tools:new[]{new CaptainMenuToolTarget(7,"携带工具：玩家0",null),new CaptainMenuToolTarget(8,"已部署炮台：玩家1","满弹")},
            combat:new("敌人53",null,"不是沉睡目标"),melee:new("2/2就绪",true,true,null));
        var toolMenu=TaskNode(tools,"W10");
        Check(toolMenu.Children[0].Label.StartsWith("携带工具")&&toolMenu.Children[1].Label.StartsWith("已部署炮台"),"携带工具与炮台分别列为目标");
        Check(toolMenu.Children[0].Children.Count==2&&toolMenu.Children[0].Children[0].Leaf is {Handler:42,Slot:0,Owner:11,SupplyItem:601,ToolTargetId:7,Blocked:null},"先指定工具再选择工具补给包机器人，锁定包和目标");
        Check(toolMenu.Children[1].Children[0].Leaf is {Blocked:"满弹"},"满弹目标不能提交补给");
        var focus=TaskNode(tools,"B01");Check(focus.Children[0].Leaf is {Handler:43,Slot:0,Blocked:null}&&focus.Children[3].Leaf is {Handler:44,Slot:-1},"集火单人和全队独立入口");
        var autonomous=TaskNode(tools,"B06");Check(autonomous.Children[0].Leaf is {Handler:45}&&autonomous.Children[3].Leaf is {Handler:46,Slot:-1},"恢复自主战斗解除对应单人／全队集火");
        var shared=TaskNode(tools,"B02");Check(shared.Children[0].Children[0].Leaf is {Handler:47,Blocked:"不是沉睡目标"}&&shared.Children[0].Children[3].Leaf is {Handler:48},"协同准备选择机器人且沉睡要求明确");
        Check(shared.Children[1].Leaf is {Handler:49,Slot:-1,Blocked:null}&&shared.Children[2].Leaf is {Handler:50,Blocked:null}&&shared.Children[3].Leaf is {Handler:51},"执行取消查看绑定共用会话，不再选一遍机器人");
        var notReady=CaptainMenuPlan.Build(Array.Empty<CaptainMenuBot>(),"","","",false,false,false);
        Check(TaskNode(notReady,"B01").Children[0].Leaf.Blocked!=null,"没有目标不能集火");
        Check(TaskNode(notReady,"B02").Children[1].Leaf.Blocked!=null&&TaskNode(notReady,"B02").Children[2].Leaf.Blocked!=null,"没有会话不能执行或取消");
        foreach(var removed in new[]{"W02","W04","W05","W06","S02","S08","B05"})Check(!codes.Contains(removed),"删除重复或不需要的入口 "+removed);
        for(int bots=0;bots<=3;bots++) for(int capacity=2;capacity<=9;capacity++) {
            var choices=new List<CaptainMenuBot>();
            for(int i=0;i<bots;i++)choices.Add(new(i,100+i,"机器人"+i,i!=1));
            var plan=CaptainMenuPlan.Build(choices,"弹药包 #1","箱柜A","(1,2,3)",true,true,true);
            Check(plan.Children.Count==6&&plan.Children[0].Label=="物资"&&plan.Children[4].Label=="战斗","首页按任务分组，常用任务另设入口");
            var map=new Dictionary<uint,Player.CommunicationNode>();var links=new List<CaptainMenuPlanNode>();var leaves=new List<CaptainMenuLeaf>();
            Player.CommunicationNode Make(CaptainMenuPlanNode value) {
                var node=Node(value.Id);node.IsLastNode=value.Leaf!=null;
                Check(map.TryAdd(value.Id,node),"所有节点ID唯一");
                if(value.Leaf!=null) {leaves.Add(value.Leaf);Check(value.Children.Count==0,"只有最终机器人提交节点是末级");}
                if(value.BackTo!=0)links.Add(value);
                foreach(var child in value.Children)node.m_ChildNodes.Add(Make(child));return node;
            }
            CaptainMenuTree.Reset();var root=Make(plan);
            foreach(var back in links){Check(map.ContainsKey(back.BackTo),"返回目标存在");CaptainMenuTree.Link(back.Id,map[back.BackTo]);}
            var seen=new HashSet<Player.CommunicationNode>();var queue=new Queue<Player.CommunicationNode>();queue.Enqueue(root);
            uint page=CaptainMenuTree.PageStart;CaptainMenuTree.Paginate(root,capacity,Node,ref page);
            int found=0;while(queue.Count>0){var node=queue.Dequeue();if(!seen.Add(node))continue;
                Check(node.m_ChildNodes.Count<=capacity,"完整菜单在2至9个按钮容量下均可浏览");
                if(node.IsLastNode)found++;foreach(var child in node.m_ChildNodes)queue.Enqueue(child);
                if(CaptainMenuTree.Navigation(node.TextId))Check(CaptainMenuTree.Target(node)!=node,"返回页使用导航映射");
            }
            Check(found==leaves.Count,"分页后所有已实现和规划指令均可达");
            foreach(var leaf in leaves){
                if(leaf.Code=="S06")Check(leaf.Handler==28&&leaf.Slot==-1&&leaf.Owner==0,"S06只查询，不提交机器人动作");
                if((leaf.Code.StartsWith("S")&&leaf.Code!="S01"&&leaf.Code!="S03"&&leaf.Code!="S04"&&leaf.Code!="S05"&&leaf.Code!="S06"&&leaf.Code!="S07")||(leaf.Code.StartsWith("B")&&leaf.Code!="B08"&&leaf.Code!="B07"&&leaf.Code!="B01"&&leaf.Code!="B02"&&leaf.Code!="B06"&&leaf.Code!="B03"&&leaf.Code!="B04")||leaf.Code=="D07")Check(leaf.Handler==0,"规划任务不会冒用已有动作");
                if(leaf.Code is "A05" or "A06")Check(leaf.Handler is >=31 and <=34&&leaf.Slot==-1&&leaf.Owner==0,"独立导航绑定本地显示，无机器人执行层");
                if(leaf.Code=="B08"&&leaf.Slot>=0)Check(leaf.Handler==9,"指定击杀绑定独立近战处理器");
                if(leaf.Slot>=0)Check(leaf.Owner==100+leaf.Slot,"提交绑定机器人拥有者身份，不能仅绑定槽位");
                if(leaf.Handler!=0&&leaf.Slot==1&&leaf.Handler!=8&&leaf.Handler!=14&&leaf.Handler!=15)Check(leaf.Blocked!=null,"倒地机器人不能接受实际任务");
                if(leaf.Handler==8&&leaf.Slot==1)Check(leaf.Blocked==null,"倒地机器人的任务仍可只读查询");
            }
        }
        var invalid=CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,7,"bot",true)},"未识别","未识别","未识别",false,false,false);
        void Invalid(CaptainMenuPlanNode node){if(node.Leaf is {Handler:1 or 2 or 3 or 7})Check(node.Leaf.Blocked!=null,"无有效准星目标时不允许提交世界交互");foreach(var child in node.Children)Invalid(child);}
        Invalid(invalid);
        var kill=invalid.Children[4].Children[4];
        Check(kill.Label.StartsWith("指定击杀: ")&&kill.Children.Count==2,"单人击杀直达唯一机器人与返回，没有自动或全队入口");
        Check(kill.Children[0].Leaf is {Handler:9,Blocked:not null},"没有有效沉睡目标时拒绝提交击杀");
        var validKill=CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,7,"bot",true),new CaptainMenuBot(1,8,"down",false)},"item","box","ground",true,true,true,"敌人#12",true).Children[4].Children[4];
        Check(validKill.Children.Count==3&&validKill.Children[0].Leaf is {Handler:9,Blocked:null},"识别沉睡目标后可选择单个存活机器人");
        Check(validKill.Children[1].Leaf.Blocked!=null,"单人击杀不会发送给倒地机器人");
        var openBots=new[]{new CaptainMenuBot(0,10,"普通机器人",true,true,0),new CaptainMenuBot(1,11,"熔锁器机器人",true,true,2),new CaptainMenuBot(2,12,"倒地",false,true,3)};
        CaptainMenuPlanNode Open(CaptainMenuOpenTarget target)=>CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,openTarget:target).Children[0].Children[3];
        var noMark=Open(null);Check(noMark.Children[0].Leaf is {Handler:10,Blocked:not null},"无中键标记不能开箱");
        var hack=Open(new("储物柜#42（入侵锁）",null,false,2));
        Check(hack.Children.Count==2&&hack.Children[0].Leaf.Slot==1,"入侵锁只列出存活且余量足够的机器人，没有自动或全队选项");
        var noTool=Open(new("箱子",null,false,4));Check(noTool.Children[0].Leaf.Blocked!=null,"没有足量熔锁器明确提示，不显示普通机器人");
        var plain=Open(new("普通门",null,false,0));Check(plain.Children.Count==3,"无锁显示全部可行动机器人");
        var special=Open(new("警报门","不支持警报门",false,0));Check(special.Children.Count==2&&special.Children[0].Leaf.Blocked=="不支持警报门","特殊门不生成可执行选人页");
        var pickup=invalid.Children[0].Children[0];
        Check(pickup.Label.StartsWith("指定拿取: ")&&pickup.Children[0].Leaf?.Handler==1,"指定拿取显示物品名并直达选人，没有重复确认层");
        var resourceBots=new[]{new CaptainMenuBot(1,101,"Bishop",true,ResourceMode:"仅允许紧急使用",SupplyPack:"弹药包"),new CaptainMenuBot(2,102,"倒地",false),new CaptainMenuBot(3,103,"无包",true,HasSupplyPack:false)};
        var receivers=new[]{new CaptainMenuReceiver(0,200,"真人",true),new CaptainMenuReceiver(1,101,"Bishop",true),new CaptainMenuReceiver(2,102,"倒地",false),new CaptainMenuReceiver(3,103,"另一个机器人",true)};
        var resourcePlan=CaptainMenuPlan.Build(resourceBots,"item","box","ground",true,true,true,receivers:receivers);
        var modes=resourcePlan.Children[0].Children[6];var combatModes=resourcePlan.Children[4].Children[6];
        for(int mode=0;mode<4;mode++) {
            Check(modes.Children[mode].Children[0].Leaf is {Handler:14,Slot:1}&&modes.Children[mode].Children[0].Leaf.ResourceMode==mode,"物资策略绑定模式与机器人，不冒用补给动作");
            Check(modes.Children[mode].Children[1].Leaf.Blocked==null,"倒地机器人允许设置策略");
            Check(modes.Children[mode].Children[3].Leaf is {Handler:14,Slot:-2},"全部策略选项真正接入，不是待实现自动选人");
            Check(combatModes.Children[mode].Children[0].Leaf.Handler==14&&combatModes.Children[mode].Children[0].Leaf.ResourceMode==mode,"战斗组共用相同处理器和模式");
        }
        var give=resourcePlan.Children[0].Children[4];
        var followReceivers=new[]{new CaptainMenuReceiver(0,200,"房主",true),new CaptainMenuReceiver(2,201,"倒地真人",false),new CaptainMenuReceiver(3,202,"机器人",true,IsBot:true)};
        var movementPlan=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,receivers:followReceivers,position:new("中键位置(1,2,3)",null));
        var followTask=movementPlan.Children[3].Children[0];
        Check(!followTask.Label.Contains("待实现")&&followTask.Children.Count==3,"跟随只列真人目标和返回，不列机器人");
        Check(followTask.Children[0].Children[0].Leaf is {Handler:35,ReceiverSlot:0,ReceiverOwner:200,Slot:0,Owner:10},"跟随先选真人目标再选执行机器人，双方身份固定");
        Check(followTask.Children[1].Children[0].Leaf.Blocked=="跟随对象已经倒地，请先救援","倒地跟随对象不能提交");
        Check(followTask.Children[0].Children.Count==openBots.Length+1,"跟随没有未接通的自动／全队占位选项");
        var moveTask=movementPlan.Children[3].Children[1];
        Check(moveTask.Label.Contains("中键位置")&&!moveTask.Label.Contains("待实现")&&moveTask.Children[0].Leaf is {Handler:36,Slot:0,Blocked:null},"前往中键位置直接选人，无重复确认层");
        Check(moveTask.Children[2].Leaf.Blocked!=null,"倒地机器人不能执行前往");
        var missingMark=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true).Children[3].Children[1];
        Check(missingMark.Children[0].Leaf.Blocked!=null,"未标记位置明确拒绝，不使用菜单打开时准星偷偷替代");
        var staleMark=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,position:new("位置","已过期")).Children[3].Children[1];
        Check(staleMark.Children[0].Leaf.Blocked=="已过期","失效位置的失败说明保留");
        var noHumans=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,receivers:new[]{followReceivers[2]}).Children[3].Children[0];
        Check(noHumans.Children[0].Leaf.Blocked=="没有可选的真人队友","无真人跟随目标时提供原因");
        var station=movementPlan.Children[3].Children[2];
        Check(station.Label.Contains("中键位置")&&!station.Label.Contains("待实现")&&station.Children[0].Leaf is {Handler:37,Blocked:null},"驻守直接使用中键坐标再选人");
        Check(station.Children.Count==openBots.Length+1,"驻守没有额外确认或假自动分配");
        var pauseTask=movementPlan.Children[3].Children[8];
        Check(!pauseTask.Label.Contains("待实现")&&pauseTask.Children.Count==3,"暂停与继续两个真实操作加返回");
        Check(pauseTask.Children[0].Children[0].Leaf is {Handler:38,SearchPause:true,Owner:10},"暂停保存机器人身份且不提交取消");
        Check(pauseTask.Children[1].Children[0].Leaf is {Handler:38,SearchPause:false,Owner:10},"继续与暂停具有独立操作值");
        Check(pauseTask.Children[0].Children[2].Leaf.Blocked!=null,"倒地机器人无法手动控制原版交互");
        var noStationMark=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true).Children[3].Children[2];
        Check(noStationMark.Children[0].Leaf.Blocked!=null,"驻守缺少合法中键目标时拒绝");
        Check(give.Children[0].Label=="补给对象：真人"&&give.Children[0].Children[0].Leaf is {Handler:16,Slot:1,Owner:101,ReceiverSlot:0,ReceiverOwner:200},"先选补给对象再选执行者，双方身份锁定");
        Check(give.Children[0].Children[1].Leaf.Blocked!=null&&give.Children[0].Children[2].Leaf.Blocked=="没有可用资源包","倒地和没有资源包的执行者明确阻止");
        Check(give.Children[2].Children[0].Leaf.Blocked=="倒地队友需要先救援","不能把补给当作复活");
        Check(resourcePlan.Children[0].Children[7].Children[1].Leaf is {Handler:15,Blocked:null},"携带报告是只读功能，倒地也可查");
        var board=new TaskBoardSnapshot{At=20};
        board.Rows.Add(new(51,0,10,"Bishop","指定拿取","弹药包",TaskBoardState.Returning,"返回中","操作已核验",1,19));
        board.Rows.Add(new(52,1,11,"Hackett","开箱／开门","门2",TaskBoardState.Failed,"失败","开启未确认",2,18));
        var boardPlan=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,taskBoard:board);
        var taskView=boardPlan.Children[3].Children[11];
        Check(taskView.Label=="查看队长任务"&&taskView.Children[0].Label.StartsWith("正在执行"),"任务看板先按状态分组，无机器人确认层");
        var returning=taskView.Children.Find(n=>n.Label.StartsWith("返回中"));
        Check(returning.Children[0].Leaf is {Handler:13,Slot:-1,RecordId:51},"只读详情绑定任务记录，未派机器人执行");
        var failedPage=taskView.Children.Find(n=>n.Label.StartsWith("最近失败"));
        Check(failedPage.Children[0].Leaf.RecordId==52,"已结束任务仍可查看");
        Check(boardPlan.Children[5].Children[2].Children[4].Children[0].Leaf.RecordId==51,"常用任务和行动组复用同一份只读快照");
        var search=invalid.Children[1].Children[0];
        foreach(var scope in search.Children){if(scope.BackTo!=0)continue;
            Check(scope.Children[0].Leaf?.Slot==-1,"空已知物资列表只读提示，不选择机器人");
        }
        var roomSupplies=new CaptainSupplyScope("当前房间");
        var collectScope=new CaptainSupplyScope("当前房间");
        for(int i=1;i<=5;i++)collectScope.Results.Add(new(i,"物品"+i,null,101,4,"弹药包"));
        collectScope.Results.Add(new(1,"重复世界身份",null,101,4,"弹药包"));
        collectScope.Results.Add(new(6,"关闭柜子内", "柜子未开",102,5,"荧光棒"));
        collectScope.Results.Add(new(7,"重物",null,103,8,"任务物品"));
        collectScope.Results.Add(new(8,"同名不同类型",null,104,4,"弹药包"));
        var collect=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,supplies:new[]{collectScope}).Children[0].Children[2];
        Check(collect.Label=="按类型收集"&&collect.Children[0].Children.Count==3,"收集已接入，只按实际类型分组，不列关闭柜子和搬运物品");
        var collectType=collect.Children[0].Children[0];
        Check(collectType.Label.Contains("已知可选 5 件")&&collectType.Children.Count==4,"同一目标不重复计数，数量1／3／5与返回页齐全");
        var collectLeaf=collectType.Children[2].Children[0].Leaf;
        Check(collectLeaf is {Handler:17,CollectScope:0,CollectType:101,CollectSlot:4,CollectLimit:5,Slot:0,Owner:10},"范围→实际类型→数量→选人，最终提交锁定全部参数");
        Check(collectType.Children[2].Children[2].Leaf.Blocked!=null,"收集不交给倒地机器人");
        Check(collect.Children[0].Children[1].Label.Contains("#104"),"同名不同数据ID不合并收集类型");
        roomSupplies.Results.Add(new(71,"弹药包 AMMOPACK_123",null));
        roomSupplies.Results.Add(new(72,"任务物品 KEY_123","使用原版任务指令"));
        var supplyMenu=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,supplies:new[]{roomSupplies}).Children[1].Children[0];
        Check(!supplyMenu.Label.Contains("待实现"),"已知物资查询已接入");
        var results=supplyMenu.Children[0];
        Check(results.Children[0].Label.Contains("AMMOPACK_123")&&results.Children[1].Label.Contains("KEY_123"),"同类型/同编号部分相同物品保持独立结果");
        var details=results.Children[0].Children[0].Leaf;
        Check(details is {Handler:11,Slot:-1,SupplyId:71},"查看详情没有机器人，也不会占任务");
        var pick=results.Children[0].Children[1];
        Check(pick.Children[0].Leaf is {Handler:12,SupplyId:71,Slot:0,Owner:10,Blocked:null},"从结果拿取绑定具体物品及机器人拥有者");
        Check(pick.Children[2].Leaf.Blocked!=null,"物资列表不能下令倒地机器人");
        Check(results.Children[1].Children[1].Children[0].Leaf.Blocked=="使用原版任务指令","任务物品可以查看，但不冒充普通拾取");
        roomSupplies.Results.Add(new(73,"机器人搜索发现",null,BotDiscovered:true));
        var searchPlan=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,supplies:new[]{roomSupplies}).Children[1];
        var explore=searchPlan.Children[1];
        Check(!explore.Label.Contains("待实现") && explore.Children[1].Children[2].Children[0].Leaf is {Handler:18,SearchScope:1,SearchFilter:2,Slot:-1,Owner:0},"即时搜索按范围、类型提交，不绑定机器人");
        Check(explore.Children[0].Children[0].Children.Count==2&&explore.Children[0].Children[0].Children[0].Leaf.Blocked==null,"机器人状态不影响只读搜寻，无多余选人页");
        var searchResults=searchPlan.Children[5].Children[0];
        Check(searchResults.Children.Count==2 && searchResults.Children[0].Children[0].Leaf is {Handler:11,SupplyId:73},"搜索结果只含机器人发现；玩家查询结果不冒充搜索");
        Check(searchResults.Children[0].Children[1].Children[0].Leaf is {Handler:12,SupplyId:73},"搜索结果复用具体物品拾取，不重新找同名物品");
        Check(searchPlan.Children.Count==7&&searchPlan.Children[6].BackTo!=0,"即时搜索不再保留暂停／继续入口");
        var terminalSearch=searchPlan.Children[2];
        Check(!terminalSearch.Label.Contains("待实现")&&terminalSearch.Children.Count==3,"终端搜寻只提供房间／Zone和返回");
        Check(terminalSearch.Children[0].Children[0].Leaf is {Handler:23,SearchScope:0,Slot:-1,Owner:0,SearchPause:false},"终端查询只选范围，无选人或物资类型层");
        Check(terminalSearch.Children[1].Children[0].Leaf is {Handler:23,SearchScope:1,Blocked:null}&&terminalSearch.Children[1].Children.Count==2,"Zone终端查询可直接提交");
        var noBotSearch=CaptainMenuPlan.Build(Array.Empty<CaptainMenuBot>(),"item","box","ground",true,true,true).Children[1];
        Check(noBotSearch.Children[1].Children[0].Children[0].Children[0].Leaf is {Handler:18,Slot:-1,Blocked:null},"无机器人也可执行物资统计");
        Check(noBotSearch.Children[2].Children[0].Children[0].Leaf is {Handler:23,Slot:-1,Blocked:null},"无机器人也可执行终端统计");
        Check(noBotSearch.Children[3].Children[1].Children[2].Children[0].Leaf is {Handler:24,SearchScope:1,SearchFilter:2,Slot:-1,Blocked:null},"容器搜寻只选范围和状态，无机器人也可提交");
        var interactionSearch=noBotSearch.Children[4];
        Check(interactionSearch.Children.Count==3&&interactionSearch.Children[0].Children.Count==5,"交互点按房间／Zone和四种类型组织");
        Check(interactionSearch.Children[1].Children[3].Children[0].Leaf is {Handler:28,SearchScope:1,SearchFilter:3,Slot:-1,Owner:0,Blocked:null},"任务交付点即时查询，无选人或交付动作");
        var pointResults=new[]{new CaptainInteractionResult(17,"安全门 #17"),new(18,"任务交付点"),new(0,"达到显示上限",true)};
        var pointPage=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,interactions:pointResults).Children[1].Children[5].Children[1];
        Check(pointPage.Label.Contains("交互点")&&pointPage.Children[0].Children.Count==4,"交互点结果含详情、重新标记、独立导航和返回，无任务交互");
        Check(pointPage.Children[0].Children[0].Leaf is {Handler:29,InteractionId:17,Slot:-1,Owner:0}&&pointPage.Children[0].Children[1].Leaf is {Handler:30,InteractionId:17,Slot:-1,Owner:0},"详情及标记绑定唯一结果，完全不选机器人");
        Check(pointPage.Children[2].Leaf is {Handler:29,InteractionId:0,Blocked:not null},"上限提示不当作交互目标");
        var containerResults=new[]{new CaptainContainerResult(11,"物资箱 #11"),new(12,"入侵柜",NeededMelters:2),new(13,"物理锁",NeedsMelee:true),
            new(14,"已开启","该容器已开启"),new(0,"显示上限",Notice:true)};
        var containerGroup=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,terminals:Array.Empty<CaptainTerminalResult>(),containers:containerResults).Children[1].Children[5].Children[2];
        Check(containerGroup.Children[0].Children[0].Leaf is {Handler:25,ContainerId:11,Slot:-1}&&containerGroup.Children[0].Children[1].Leaf is {Handler:26,ContainerId:11,Slot:-1},"容器详情和标记绑定唯一对象，不选机器人");
        Check(containerGroup.Children[0].Children[2].Children.Count==3&&containerGroup.Children[0].Children[2].Children[0].Leaf is {Handler:27,Slot:0,ContainerId:11},"无锁仅列全部可行动机器人和返回，没有自动选人");
        Check(containerGroup.Children[1].Children[2].Children.Count==2&&containerGroup.Children[1].Children[2].Children[0].Leaf is {Handler:27,Slot:1,ContainerId:12},"入侵锁只显示足量熔锁器的机器人");
        Check(containerGroup.Children[2].Children[2].Children.Count==3,"物理锁使用近战，未要求熔锁器");
        Check(containerGroup.Children[3].Children[2].Children[0].Leaf is {Handler:27,Slot:-1,Blocked:not null},"已开容器没有可执行开启提交");
        Check(containerGroup.Children[4].Leaf is {Handler:25,Blocked:not null,ContainerId:0},"限额提示不可作为容器目标");
        var lowTools=CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,10,"缺乏工具",true,HasMelee:false)},"item","box","ground",true,true,true,containers:new[]{new CaptainContainerResult(5,"锁柜",NeededMelters:1)}).Children[1].Children[5].Children[1];
        Check(lowTools.Children[0].Children[2].Children[0].Leaf is {Handler:27,Blocked:not null},"无人可解锁时明确拒绝，不列错误执行者");
        var terminalResults=new[]{new CaptainTerminalResult(8,"终端 TERMINAL_123（就绪）"),new CaptainTerminalResult(9,"终端 TERMINAL_456（密码保护）"),new CaptainTerminalResult(0,"数量达到上限",true)};
        var withTerminals=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,supplies:new[]{roomSupplies},terminals:terminalResults).Children[1].Children[5];
        var terminalPage=withTerminals.Children[1];
        Check(terminalPage.Label.Contains("终端")&&terminalPage.Children.Count==4,"终端结果独立成组，和物资拿取分开");
        Check(terminalPage.Children[0].Children[0].Leaf is {Handler:21,TerminalId:8,Slot:-1,SupplyId:0},"终端详情只读，绑定终端结果身份，无机器人参数");
        Check(terminalPage.Children[0].Children[1].Leaf is {Handler:22,TerminalId:8,Slot:-1},"重新标记直接定位，不派任务或拿取");
        Check(terminalPage.Children[1].Children.Count==4&&terminalPage.Children[2].Leaf.Blocked!=null,"终端含独立导航，没有拾取入口，限额提示不能执行");
        var emptyTerminals=CaptainMenuPlan.Build(Array.Empty<CaptainMenuBot>(),"item","box","ground",true,true,true,terminals:Array.Empty<CaptainTerminalResult>()).Children[1].Children[5].Children[1];
        Check(emptyTerminals.Children[0].Leaf is {Handler:21,Slot:-1,Blocked:not null},"没有终端结果时显示提示，无需选择机器人");
        var duplicateSupplies=new CaptainSupplyScope("当前Zone");duplicateSupplies.Results.AddRange(roomSupplies.Results);
        var remoteSupplies=new CaptainSupplyScope("已到访的其他Zone");remoteSupplies.Results.Add(new(999,"远处物资",null));
        var navigationPlan=CaptainMenuPlan.Build(Array.Empty<CaptainMenuBot>(),"item","box","ground",true,true,true,
            supplies:new[]{roomSupplies,duplicateSupplies,remoteSupplies},terminals:terminalResults,containers:containerResults,interactions:pointResults);
        var routeChooser=navigationPlan.Children[3].Children[4];
        Check(!routeChooser.Label.Contains("待实现")&&routeChooser.Children.Count==5,"独立导航无需人机，按四种已知目标分类及返回");
        Check(routeChooser.Children[0].Children.Count==roomSupplies.Results.Count+1,"当前房间和Zone物资去重，排除其他已到访Zone");
        Check(routeChooser.Children[0].Children[0].Leaf is {Handler:31,SupplyId:71,Slot:-1,Owner:0,Blocked:null},"导航目标直接绑定物资身份，没有选人层");
        Check(routeChooser.Children[1].Children[0].Leaf is {Handler:31,TerminalId:8,SupplyId:0},"终端导航保持类型及准确编号");
        Check(routeChooser.Children[2].Children[3].Leaf is {Handler:31,ContainerId:14,Blocked:null},"已打开柜子仍可作为导航目标，不套用开启限制");
        Check(routeChooser.Children[3].Children[0].Leaf is {Handler:31,InteractionId:17},"交互点导航只定位，不变成任务交付");
        var routeControl=navigationPlan.Children[3].Children[5];
        Check(routeControl.Children[0].Leaf is {Handler:32,Slot:-1}&&routeControl.Children[1].Leaf is {Handler:33,Slot:-1}
            &&routeControl.Children[2].Leaf is {Handler:34,Slot:-1},"查看／重算／取消导航独立于机器人任务取消");
        Check(results.Children[0].Children[2].Leaf is {Handler:31,SupplyId:71,Slot:-1},"物资结果可直接发起导航，拾取原入口保留");
        Check(terminalPage.Children[0].Children[2].Leaf is {Handler:31,TerminalId:8},"终端结果直达路线");
        Check(containerGroup.Children[0].Children[3].Leaf is {Handler:31,ContainerId:11},"容器结果直达路线，已有开箱选人未改");
        Check(pointPage.Children[0].Children[2].Leaf is {Handler:31,InteractionId:17},"交互点结果直达当前可达侧路线");
        for(int typeMode=0;typeMode<2;typeMode++)
        for(int capacity=2;capacity<=9;capacity++) {
            var large=new CaptainSupplyScope("当前房间");var largeZone=new CaptainSupplyScope("当前区域 Zone");
            var scopes=new List<CaptainSupplyScope>{large,largeZone};
            for(int i=1;i<=80;i++) {
                uint typeId=(uint)(typeMode==0?i%16+1:i);
                var supply=new CaptainSupplyResult(i,"物品 "+i,null,typeId,4,"类型 "+typeId,BotDiscovered:true);large.Results.Add(supply);largeZone.Results.Add(supply);
                var visited=new CaptainSupplyScope("Zone "+i);visited.Results.Add(supply);scopes.Add(visited);
            }
            var maxBoard=new TaskBoardSnapshot();
            for(int i=1;i<=64;i++)maxBoard.Rows.Add(new(i,0,10,"Bot","任务","目标",i<=32?TaskBoardState.Executing:TaskBoardState.Complete,"阶段","原因",0,1));
            var manyTerminals=new List<CaptainTerminalResult>();for(int i=1;i<=64;i++)manyTerminals.Add(new(i,"TERMINAL_"+i));manyTerminals.Add(new(0,"终端结果达到上限",true));
            var manyContainers=new List<CaptainContainerResult>();for(int i=1;i<=64;i++)manyContainers.Add(new(i,"BOX_"+i));manyContainers.Add(new(0,"容器显示上限",Notice:true));
            var manyPoints=new List<CaptainInteractionResult>();for(int i=1;i<=64;i++)manyPoints.Add(new(i,"交互点_"+i));manyPoints.Add(new(0,"交互点显示上限",true));
            var largePlan=CaptainMenuPlan.Build(openBots,"item","box","ground",true,true,true,supplies:scopes,taskBoard:maxBoard,receivers:receivers,terminals:manyTerminals,containers:manyContainers,interactions:manyPoints);
            var ids=new HashSet<uint>();int expectedLarge=0;
            Player.CommunicationNode MakeLarge(CaptainMenuPlanNode p) {
                Check(ids.Add(p.Id)&&p.Id<CaptainMenuTree.NodeEnd,"80件物资、80个已到访区域节点不冲突、不越界");
                var n=Node(p.Id);n.IsLastNode=p.Leaf!=null;if(n.IsLastNode)expectedLarge++;
                foreach(var child in p.Children)n.m_ChildNodes.Add(MakeLarge(child));return n;
            }
            var largeRoot=MakeLarge(largePlan);uint largePage=CaptainMenuTree.PageStart;
            CaptainMenuTree.Reset();CaptainMenuTree.Paginate(largeRoot,capacity,Node,ref largePage);
            int actual=0;var queue=new Queue<Player.CommunicationNode>();queue.Enqueue(largeRoot);
            while(queue.Count>0){var n=queue.Dequeue();Check(n.m_ChildNodes.Count<=capacity,"大列表分页不超按钮容量");if(n.IsLastNode)actual++;foreach(var child in n.m_ChildNodes)queue.Enqueue(child);}
            Check(actual==expectedLarge&&largePage<CaptainMenuTree.PageEnd,"最大列表所有结果可达且分页ID不越界");
        }
        var nativeRoot=Node(123);var nativeTools=Node(456);nativeTools.Script=Player.CommunicationNode.ScriptType.UseTool; nativeTools.IsLastNode=true;
        var nativeCategory=Node(455); nativeCategory.m_ChildNodes.Add(nativeTools); nativeRoot.m_ChildNodes.Add(nativeCategory);
        Check(CaptainMenuTree.FindNativeTools(nativeRoot)==nativeCategory,"按UseTool子模板识别原版工具页面，不进入模板叶节点");
        Check(CaptainMenuTree.FindNativeTools(nativeTools)==null,"工具模板本身不是工具页面");
        Check(CaptainMenuTree.FindNativeTools(Node(789))==null,"找不到原版入口时返回空，不假造部署菜单");
        var repeatedRoot=Node(700); var unrelated=Node(701); var repeatedCategory=Node(701); repeatedCategory.m_ChildNodes.Add(nativeTools);
        repeatedRoot.m_ChildNodes.Add(unrelated); repeatedRoot.m_ChildNodes.Add(repeatedCategory);
        Check(CaptainMenuTree.FindNativeTools(repeatedRoot)==repeatedCategory,"相同文字ID的不同节点不误去重");
        repeatedCategory.m_ChildNodes.Add(repeatedRoot);
        Check(CaptainMenuTree.FindNativeTools(repeatedRoot)==repeatedCategory,"回环不会无限遍历");
        CaptainMenuTree.Reset();
    }
    static void FrameworkNavigation()
    {
        CaptainMenuTree.Reset();
        var plan=CaptainMenuPlan.Build(new[]{new CaptainMenuBot(0,88,"bot",true)},"弹药包#1","柜子","坐标",true,true,true);
        var map=new Dictionary<uint,Player.CommunicationNode>();var links=new List<CaptainMenuPlanNode>();
        Player.CommunicationNode Make(CaptainMenuPlanNode p) {
            var n=Node(p.Id);n.IsLastNode=p.Leaf!=null;map.Add(p.Id,n);
            if(p.BackTo!=0)links.Add(p);foreach(var child in p.Children)n.m_ChildNodes.Add(Make(child));return n;
        }
        var root=Make(plan);foreach(var link in links)CaptainMenuTree.Link(link.Id,map[link.BackTo]);
        uint page=CaptainMenuTree.PageStart;CaptainMenuTree.Paginate(root,9,Node,ref page);
        var stock=Node(5);var menu=new PUI_CommunicationMenu();menu.m_menu.m_controller.m_rootNode=stock;ItemTaskAdapter.PreparedRoot=root;
        menu.SetActive(true);menu.BypassChooseHook=true;menu.MoveNext(2);
        Check(!menu.m_active&&menu.RenderedButtons==0,"复现0.5.25：直接MoveNext未被拦截，空部署组导致菜单立即关闭");
        menu.BypassChooseHook=false;
        ItemTaskAdapter.MenuTarget=new();int before=ItemTaskAdapter.Dispatches;menu.SetActive(true);
        void Key(int index){UnityEngine.Input.Down.Clear();UnityEngine.Input.Down.Add((UnityEngine.KeyCode)(49+index));UnityEngine.Time.frameCount++;Call("Select",menu);}
        Key(0);Check(menu.m_menu.CurrentNode==root.m_ChildNodes[0],"实际输入：先选择物资任务组");
        Key(0);Check(!menu.m_menu.CurrentNode.IsLastNode,"实际输入：指定拿取直接进入选人页，不立即提交");
        var task=menu.m_menu.CurrentNode;
        Check(ItemTaskAdapter.Dispatches==before,"到选人之前无任务提交");
        Key(menu.m_menu.CurrentNode.m_ChildNodes.Count-1);Check(menu.m_menu.CurrentNode==root.m_ChildNodes[0],"实际输入：拿取选人页返回物资组");
        Key(0);Key(0);Check(ItemTaskAdapter.Dispatches==before+1&&!menu.m_active,"实际输入：最终选择机器人只提交一次并关闭");
        menu.SetActive(true);var nativeTools=Node(222);nativeTools.Script=Player.CommunicationNode.ScriptType.UseTool; nativeTools.IsLastNode=true;
        var nativePage=Node(223);nativePage.m_ChildNodes.Add(nativeTools);
        CaptainMenuTree.Link(plan.Children[2].Id,nativePage);Key(2);
        Check(menu.m_active&&menu.m_menu.CurrentNode==nativePage&&menu.RenderedButtons==3,"进入原版工具父页后动态按钮生成且菜单保持打开");
        Check(menu.Title==nativePage,"原版工具页标题同步");
        menu.MoveNext(2);Check(menu.m_active&&menu.NativeToolCalls==0,"原生Update当帧再次调用MoveNext，不能误选工具按钮");
        Check(!(bool)Call("Select",menu),"进入工具页当帧不把同一个数字键再次交给新按钮");
        UnityEngine.Time.frameCount++;UnityEngine.Input.Down.Clear();
        Check((bool)Call("Select",menu),"工具页交给原版选择逻辑，动态按钮不按模板数量截断");
        Check(CaptainMenuInput.Blocks(InputAction.Weapon)&&CaptainMenuInput.Blocks(InputAction.Flashlight),"原版工具页仍保留会话输入隔离");
        before=ItemTaskAdapter.Dispatches;menu.NativeSelectTool(2);
        Check(menu.NativeToolCalls==1&&ItemTaskAdapter.Dispatches==before,"第三个动态按钮调用原版指令，不调用队长物品分发");
        Check(menu.m_menu.m_controller.m_rootNode==stock&&!menu.m_active,"原版工具指令关闭后归还原版根节点");
        menu.SetActive(false);
        menu.SetActive(true);menu.m_menu.MoveNext(nativeTools);menu.TryUpdateCategoryButtons();
        Check(menu.m_active&&menu.RenderedButtons==0&&menu.m_lastActiveButtonIndex==-1,"复现旧低层跳转：刷新隐藏全部按钮，通讯仍打开但列表为空");
        menu.SetActive(false);
        CaptainMenuTree.Reset(); CaptainMenuTree.Link(plan.Children[2].Id,nativeTools);
        menu.SetActive(true); int warnings=ItemTaskAdapter.Feedbacks;Key(2);
        Check(menu.m_active&&menu.m_menu.CurrentNode==root,"零按钮页面返回首页，不能直接退出通讯");
        Check(ItemTaskAdapter.Feedbacks==warnings+1&&CaptainMenuInput.Active,"无可用按钮时显示原因并保留可操作菜单");
        menu.SetActive(false);CaptainMenuTree.Reset();CaptainMenuTree.Link(plan.Children[2].Id,nativePage);
        menu.SetActive(true);UnityEngine.Time.frameCount++;UnityEngine.Input.Down.Clear();
        // Match native Update: no CheckSelectionInput call before MoveNext.
        menu.MoveNext(2);
        Check(menu.m_active&&menu.m_menu.CurrentNode==nativePage&&menu.RenderedButtons==3,"原生Update直接MoveNext部署入口，绕过输入helper仍可生成工具页");
        menu.MoveNext(2);Check(menu.m_active&&menu.NativeToolCalls==1,"直接更新路径重复选键不会执行新页面的部署按钮");
        UnityEngine.Time.frameCount++;before=ItemTaskAdapter.Dispatches;menu.MoveNext(2);
        Check(!menu.m_active&&menu.NativeToolCalls==2&&ItemTaskAdapter.Dispatches==before,"直接更新路径第三个动态工具按钮仍由原版执行");
        menu.SetActive(true);UnityEngine.Time.frameCount++;menu.EmptyNativeTools=true;warnings=ItemTaskAdapter.Feedbacks;menu.MoveNext(2);
        Check(menu.m_active&&menu.m_menu.CurrentNode==root&&ItemTaskAdapter.Feedbacks==warnings+1,"实际没有可部署工具时保留首页并明确反馈");
        menu.EmptyNativeTools=false;menu.SetActive(false);
        UnityEngine.Input.Down.Clear();UnityEngine.Time.frameCount+=3;CaptainMenuInput.Reset();CaptainMenuTree.Reset();
    }
}


