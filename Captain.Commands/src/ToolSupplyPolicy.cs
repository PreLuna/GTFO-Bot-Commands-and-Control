using System;
namespace PreLuna.Captain.Tasks {
    internal enum ToolSupplyStage { Moving, Equipping, Applying, Verifying, Complete, Failed, Cancelled }
    internal interface IToolSupplySession {
        bool Valid { get; } bool Allowed { get; } bool Arrived { get; } bool Equipped { get; }
        bool Moving { get; } bool CanExecute { get; } bool Confirmed { get; }
        bool Move(); bool Equip(); bool Apply(); void Report(ToolSupplyStage stage, string reason); void Finish(ToolSupplyStage stage, string reason);
    }
    internal sealed class ToolSupplyRunner {
        readonly IToolSupplySession session; readonly float created; float nextRequest, applyAt, verifyAt;
        int moves, equips; bool submitted, cancelled;
        public ToolSupplyStage Stage { get; private set; }
        public bool Finished => Stage >= ToolSupplyStage.Complete;
        public ToolSupplyRunner(IToolSupplySession session,float now){this.session=session;created=now;}
        public void Cancel()=>cancelled=true;
        public void Abort(string reason){if(!Finished)End(ToolSupplyStage.Failed,reason);}
        void End(ToolSupplyStage stage,string reason){Stage=stage;session.Finish(stage,reason);}
        public void Tick(float now){
            if(Finished)return;
            if(!float.IsFinite(now)||now<created||!session.Valid){End(ToolSupplyStage.Failed,"工具、资源包、机器人或关卡身份已改变");return;}
            if(submitted){
                if(session.Confirmed){End(ToolSupplyStage.Complete,"已核验资源包减少与指定工具弹药增加");return;}
                if(now-verifyAt>=3){End(ToolSupplyStage.Failed,"已调用原版补给，但资源消耗与工具增弹未同时确认；不重复消耗");return;}
                session.Report(Stage,"等待资源消耗及目标增弹同步");return;
            }
            if(cancelled){End(ToolSupplyStage.Cancelled,"取消工具补给，未再次消耗资源");return;}
            if(now-created>60){End(ToolSupplyStage.Failed,"工具补给60秒超时");return;}
            if(!session.CanExecute){End(ToolSupplyStage.Cancelled,"让位扫描／救援或其他高优先级任务");return;}
            if(!session.Allowed){End(ToolSupplyStage.Failed,"工具已满、已部署／回收状态改变、危险站位或资源不足");return;}
            if(Stage==ToolSupplyStage.Moving){
                if(session.Arrived){Stage=ToolSupplyStage.Equipping;nextRequest=now;}
                else if(!session.Moving&&now>=nextRequest){if(moves>=3){End(ToolSupplyStage.Failed,"三次接近请求未确认到达");return;}moves++;nextRequest=now+8;if(!session.Move()){End(ToolSupplyStage.Failed,"原版接近动作被拒绝");return;}}
            }
            if(Stage==ToolSupplyStage.Equipping){
                if(session.Equipped){Stage=ToolSupplyStage.Applying;applyAt=now;}
                else if(now>=nextRequest){if(equips>=3){End(ToolSupplyStage.Failed,"原版装备资源包未确认");return;}equips++;nextRequest=now+3;if(!session.Equip()){End(ToolSupplyStage.Failed,"原版装备资源包被拒绝");return;}}
            }
            if(Stage==ToolSupplyStage.Applying){
                if(!session.Arrived||!session.Equipped){End(ToolSupplyStage.Failed,"使用期间站位或资源包装备改变，未消耗资源");return;}
                if(now-applyAt>=2){submitted=true;verifyAt=now;Stage=ToolSupplyStage.Verifying;if(!session.Apply()){End(ToolSupplyStage.Failed,"原版补给授权失效，不重试");return;}}
            }
            session.Report(Stage,Stage==ToolSupplyStage.Applying?"补给等待2秒，随后单次应用":"接近／装备资源包");
        }
    }
}
