using System;
using System.Linq;
using PreLuna.Captain.Tasks;
static class TaskBoardTests
{
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Main(){
        var journal=new TaskBoardJournal();var identity=new object();
        void Record(TaskBoardState state,string stage,string reason,float now)=>journal.Record(identity,1,100,"Bishop","指定拿取","弹药包 @ (1,2,3)",state,stage,reason,now);
        Check(journal.Capture(0).Rows.Count==0,"初始无伪任务");
        Record(TaskBoardState.Waiting,"等待权限","等待现有物品同步",1);
        var before=journal.Capture(2);var id=before.Rows.Single().Id;
        Record(TaskBoardState.Waiting,"等待权限","等待现有物品同步",2);
        Check(journal.Count==1&&journal.Capture(3).Rows.Single().Updated==1,"重复报告不新增记录或修改时间");
        Record(TaskBoardState.Executing,"接近目标","开始执行",3);
        var moving=journal.Capture(4).Rows.Single();
        Check(moving.Id==id&&moving.Started==1&&moving.Updated==3,"阶段更新保留同一身份与起始时间");
        Check(before.Rows.Single().State==TaskBoardState.Waiting,"菜单快照不跟随后台写入而改变");
        Check(moving.Detail(4).Contains("3.0 秒")&&moving.Detail(4).Contains("弹药包"),"详情包括目标、原因及任务持续时间");
        Record(TaskBoardState.Paused,"任务暂停","让出受保护动作",4);
        Check(journal.Capture(5).Rows.Single().State==TaskBoardState.Paused,"记录真实暂停，不制造暂停动作");
        Record(TaskBoardState.Returning,"返回中","操作核验完成",5);
        Check(journal.Capture(6).Rows.Single().State==TaskBoardState.Returning,"物品核验后返程仍不是最终完成");
        Record(TaskBoardState.Failed,"失败","操作完成但返程超时",35);
        Record(TaskBoardState.Complete,"完成","迟到回调",40);
        var failed=journal.Capture(90).Rows.Single();
        Check(failed.State==TaskBoardState.Failed&&failed.Reason.Contains("返程超时"),"最终结果不能被迟到完成回调覆盖");
        Check(failed.Detail(90).Contains("34.0 秒"),"历史时长在结束时冻结");
        var replacement=new object();journal.Record(replacement,1,200,"新机器人","开门","门1",TaskBoardState.Waiting,"等待","等待权限",41);
        journal.Record(replacement,1,201,"冒用身份","开门","门2",TaskBoardState.Executing,"移动","wrong",42);
        Check(journal.Capture(43).Rows.Single(r=>!r.Terminal).Owner==200,"槽位重用不能覆盖原任务拥有者");
        journal.Record(new object(),0,0,"bad","bad","bad",TaskBoardState.Executing,"bad","bad",1);
        journal.Record(new object(),0,1,"bad","bad","bad",TaskBoardState.Executing,"bad","bad",float.NaN);
        Check(journal.Count==2,"拒绝无身份或无效时间");
        journal.Reset();Check(journal.Count==0,"关卡重置清除活动及历史");
        var earlier=new object();var later=new object();
        journal.Record(earlier,0,1,"A","任务","目标",TaskBoardState.Executing,"执行","执行",1);
        journal.Record(later,0,1,"A","任务","目标",TaskBoardState.Complete,"完成","较早完成",2);
        journal.Record(earlier,0,1,"A","任务","目标",TaskBoardState.Complete,"完成","最后完成",3);
        Check(journal.Capture(4).Rows[0].Reason=="最后完成","完成排序使用真实结束时间而非任务创建编号");
        journal.Reset();
        journal.Record(identity,1,100,"Bishop","新任务","目标",TaskBoardState.Executing,"移动","新局",50);
        Check(journal.Capture(51).Rows.Single().Id!=id,"重置后旧菜单编号不复用");
        journal.Reset();var first=new object();
        journal.Record(first,0,1,"A","任务","目标",TaskBoardState.Complete,"完成","完成",1);
        for(int i=0;i<50;i++)journal.Record(new object(),0,1,"A","任务","目标",TaskBoardState.Complete,"完成","完成",i+2);
        Check(journal.Count==32&&journal.Capture(100).Rows[0].Updated==51,"最近结果有界并按最新完成显示");
        journal.Record(first,0,1,"A","任务","目标",TaskBoardState.Complete,"完成","迟到重复",100);
        Check(journal.Count==32&&!journal.Capture(101).Rows.Any(r=>r.Reason.Contains("迟到")),"淘汰历史不因迟到报告重新加入");
        for(int i=0;i<40;i++)journal.Record(new object(),0,1,"A","任务","目标",TaskBoardState.Waiting,"等待","等待",102);
        Check(journal.Count==64,"活动记录也有上限，不挤掉已有任务");
        Console.WriteLine("PASS: "+checks+" 项生产任务日志／快照／身份／历史容量断言；无Unity调用。");
    }
}
