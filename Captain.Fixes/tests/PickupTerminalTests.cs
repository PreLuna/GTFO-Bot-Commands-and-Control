using System;
using System.Reflection;
using LevelGeneration;
using PreLuna.Captain;
using PreLuna.Captain.Tasks;
static class PickupTerminalTests
{
    static int checks;
    static void Check(bool ok,string reason) { checks++;if(!ok)throw new Exception(reason); }
    static object Hook(string name,params object[] args)=>typeof(SmartPingCompat).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static void Main()
    {
        foreach(bool danger in new[]{false,true}) foreach(bool asking in new[]{false,true})
            Check(AutoPickupPolicy.Decide(false,false,true,danger,asking,false,true,1,true)==PickupDecision.Wait,"未访问物品不能因危险或超时自动拿取");
        Check(AutoPickupPolicy.Decide(true,true,true,true,false,false,true,1,true)==PickupDecision.Wait,"玩家仍站在旁边不抢物品");
        Check(AutoPickupPolicy.Decide(true,false,false,true,false,false,true,1,true)==PickupDecision.Wait,"扫描或原子动作占用时不能收集");
        Check(AutoPickupPolicy.Decide(true,false,true,false,true,false,true,0,false)==PickupDecision.Ask,"安全时只询问");
        Check(AutoPickupPolicy.Decide(true,false,true,false,true,true,true,0,false)==PickupDecision.Wait,"未回答与未超时等待");
        Check(AutoPickupPolicy.Decide(true,false,true,false,true,true,true,-1,true)==PickupDecision.Decline,"N 优先于同帧超时");
        Check(AutoPickupPolicy.Decide(true,false,true,false,true,true,true,1,false)==PickupDecision.Take,"Y 立即同意");
        Check(AutoPickupPolicy.Decide(true,false,true,false,true,true,true,0,true)==PickupDecision.Take,"超时默认同意");
        Check(AutoPickupPolicy.Decide(true,false,true,false,true,true,false,1,true)==PickupDecision.Wait,"输入被占用时暂停答复与超时");
        Check(AutoPickupPolicy.Decide(true,false,true,true,true,true,false,0,false)==PickupDecision.Take,"询问中变危险，收集已访问物资");
        Check(AutoPickupPolicy.Decide(true,false,true,false,false,false,true,0,false)==PickupDecision.Take,"关闭询问后仍须已访问才能拾取");
        Check(TerminalAliasPolicy.Normalize("SM PACK")=="SMARTPING PACK","短命令与参数交给原 SmartPing");
        Check(TerminalAliasPolicy.Normalize(" sm\tPACK ")=="SMARTPING\tPACK ","大小写、前置空白及制表参数");
        Check(TerminalAliasPolicy.Normalize("SMALL PACK")=="SMALL PACK","不替换相似前缀命令");
        Check(TerminalAliasPolicy.Normalize("SM PACK",false)=="SM PACK","其他模组占用 SM 时保留命令");
        Check(TerminalAliasPolicy.Normalize("smartping PACK",false)=="SMARTPING PACK","完整命令独立保留");
        Check(TerminalAliasPolicy.Normalize(null)==null&&TerminalAliasPolicy.Normalize(" ")==" ","空输入保留");
        Check(TerminalAliasPolicy.Normalize("PING ITEM_123")=="PING ITEM_123","原版命令及参数不被改变");
        SmartPingCompat.Install();
        Check(Array.IndexOf(HarmonyLib.Harmony.Patches["SetupCommands"].after,"com.fridos.smartping")>=0,"命令表核对在原 SmartPing 注册之后");
        Check(Array.IndexOf(HarmonyLib.Harmony.Patches["ReceiveCommand"].before,"com.fridos.smartping")>=0,"SM 在原 SmartPing 的大小写敏感接收前展开");
        TerminalHelpTranslation.Apply("SMARTPING",null,_=>throw new Exception("null help warning"));
        Check(true,"空帮助文本跳过翻译，不调用重载比较");
        var help=new Localization.LocalizedText {UntranslatedText="original"};
        TerminalHelpTranslation.Apply("PING",help,null);Check(help.UntranslatedText=="original","原版 PING 帮助保持原值");
        foreach(var name in new[]{"SMARTPING","smartping","SmartPing"}) {
            help=new(){UntranslatedText="original"};TerminalHelpTranslation.Apply(name,help,null);
            Check(help.UntranslatedText.Contains("当前区域"),"有效帮助对象无需原生比较即可翻译 "+name);
        }
        int warnings=0;help=new(){UntranslatedText="original",FailWrite=true};
        TerminalHelpTranslation.Apply("SMARTPING",help,_=>warnings++);
        TerminalHelpTranslation.Apply("SMARTPING",help,_=>warnings++);
        Check(help.UntranslatedText=="original"&&warnings==1,"帮助 setter 失败保留原文本，仅警告一次而不中断注册");
        var cosmetic=new LG_ComputerTerminalCommandInterpreter();
        Check(cosmetic.AddCommand(TERM_Command.Unique1,"SMARTPING",help,TERM_CommandRule.Any)&&cosmetic.m_commandsPerString.ContainsKey("smartping"),"翻译失败仍执行原版命令注册");
        var terminal=new LG_ComputerTerminalCommandInterpreter(); terminal.m_commandsPerString["ping"]=TERM_Command.Ping;
        terminal.m_commandsPerEnum[TERM_Command.Ping]="PING"; Hook("Registered",terminal);
        Check(terminal.Added==1&&terminal.m_commandsPerString["sm"]==TERM_Command.Unique1&&terminal.m_commandsPerString["ping"]==TERM_Command.Ping,"缺失原注册时采用唯一空槽，保留 PING");
        Hook("Registered",terminal); Check(terminal.Added==1,"同一终端重复初始化不重复分配命令槽");
        object[] input={terminal,"sm PACK"}; Hook("Input",input);Check((string)input[1]=="SMARTPING PACK","输入阶段生成原插件能够接收的完整命令");
        object[] receive={terminal,"SM PACK"};Hook("Receive",receive);Check((string)receive[1]=="SMARTPING PACK","原插件接收前展开短命令");
        terminal=new();terminal.m_commandsPerString["smartping"]=TERM_Command.InvalidCommand;terminal.m_commandsPerEnum[TERM_Command.InvalidCommand]="SMARTPING";terminal.m_commandsPerString["sm"]=TERM_Command.InvalidCommand;Hook("Registered",terminal);
        Check(terminal.Added==1&&terminal.m_commandsPerString["sm"]==TERM_Command.Unique1&&terminal.m_commandsPerString["smartping"]==TERM_Command.Unique1&&!terminal.m_commandsPerEnum.ContainsKey(TERM_Command.InvalidCommand),"旧本体无效编号迁入唯一槽，修复完整命令与旧别名并清理本体的旧映射");
        terminal.m_commandsPerString["sm"]=TERM_Command.Ping; Hook("Registered",terminal);
        input=new object[]{terminal,"SM PACK"};Hook("Input",input);Check((string)input[1]=="SM PACK"&&terminal.m_commandsPerString["sm"]==TERM_Command.Ping,"其他模组占用别名时注册与输入阶段都不覆盖");
        terminal=new(){SlotAvailable=false};Hook("Registered",terminal);Check(!terminal.m_commandsPerString.ContainsKey("sm")&&terminal.Added==0,"命令槽用尽时记录问题并保留原版");
        terminal=new(){AddAllowed=false};Hook("Registered",terminal);Check(!terminal.m_commandsPerString.ContainsKey("sm"),"原生注册拒绝时不建立假别名");
        terminal=new();terminal.m_commandsPerString["smartping"]=TERM_Command.Unique1;Hook("Registered",terminal);Check(terminal.Added==0&&terminal.m_commandsPerString["sm"]==TERM_Command.Unique1,"已经正确注册的唯一槽不重复迁移");
        terminal=new(){AddAllowed=false};terminal.m_commandsPerString["smartping"]=TERM_Command.InvalidCommand;Hook("Registered",terminal);
        Check(terminal.m_commandsPerString["smartping"]==TERM_Command.InvalidCommand&&!terminal.m_commandsPerString.ContainsKey("sm"),"迁移失败恢复原本体映射，不产生假短入口");
        terminal=new(){SlotAvailable=false};terminal.m_commandsPerString["smartping"]=TERM_Command.InvalidCommand;Hook("Registered",terminal);
        Check(terminal.m_commandsPerString["smartping"]==TERM_Command.InvalidCommand&&terminal.Added==0,"无空槽时不删除原映射");
        terminal=new();terminal.m_commandsPerString["SMARTPING"]=TERM_Command.InvalidCommand;terminal.m_commandsPerString["SM"]=TERM_Command.InvalidCommand;
        terminal.m_commandsPerEnum[TERM_Command.InvalidCommand]="smartping";Hook("Registered",terminal);
        Check(terminal.m_commandsPerString["smartping"]==TERM_Command.Unique1&&terminal.m_commandsPerString["sm"]==TERM_Command.Unique1&&!terminal.m_commandsPerString.ContainsKey("SM"),"旧大写错误入口迁移到原生可读的小写命令表");
        terminal=new();terminal.m_commandsPerString["SMARTPING"]=TERM_Command.Unique1;terminal.m_commandsPerString["SM"]=TERM_Command.Ping;Hook("Registered",terminal);
        Check(terminal.m_commandsPerString["smartping"]==TERM_Command.Unique1&&terminal.m_commandsPerString["SM"]==TERM_Command.Ping&&!terminal.m_commandsPerString.ContainsKey("sm"),"旧大写别名被别人占用时保留占用，同时保证完整命令原生可读");
        terminal=new();terminal.AddCommand(TERM_Command.InvalidCommand,"SMARTPING",new Localization.LocalizedText(),TERM_CommandRule.Any);Hook("Registered",terminal);
        Check(terminal.m_commandsPerString.TryGetValue("sm".ToLowerInvariant(),out var parsed)&&parsed==TERM_Command.Unique1,"真实本体的小写注册经原版输入小写查表后可解析 SM");
        input=new object[]{terminal,"sm PACK"};Hook("Input",input);
        Check(((string)input[1]).StartsWith("SMARTPING",StringComparison.Ordinal),"传给原本体的字符串满足其区分大小写的 StartsWith 检查");
        Console.WriteLine("PASS: "+checks+" 项访问拾取／Y N／超时／终端别名策略检查；不代表原生游戏运行结果。");
    }
}

