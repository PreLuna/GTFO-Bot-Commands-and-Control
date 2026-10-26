using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Player;
using UnityEngine;
using Il2CppInterop.Runtime.Attributes;

namespace PreLuna.HeadCamera
{
    public sealed class CameraRuntime : MonoBehaviour
    {
        public CameraRuntime(IntPtr pointer) : base(pointer) { }
        internal static CameraRuntime Current;
        readonly List<CameraTarget> targets = new(4);
        readonly RenderClock clock = new();
        readonly FailureCircuit failures = new();
        readonly CameraHud hud = new();
        readonly ViewSession session = new();
        readonly FrameRateMeter frameRate = new();
        readonly ToggleGesture gesture = new();
        readonly ControlFeedback feedback = new();
        NativeCapture capture;
        int mainThread;
        float nextTargets, nextDiagnostics;
        bool previouslyInLevel, allowed, stopped;
        string message;
        double elapsedTotal, elapsedMax;
        int captureCount;
        long lastLoggedAgent;
        bool? lastLoggedControlled;
        internal bool Opened => session.Opened;
        [HideFromIl2Cpp]
        internal CameraTarget? Selected { get; private set; }
        internal bool OnMainThread => Environment.CurrentManagedThreadId == mainThread;
        public void Awake() { mainThread = Environment.CurrentManagedThreadId; Current = this; HeadCameraApi.BindHudThread(); }
        [HideFromIl2Cpp]
        internal bool TryReadHudIdentity(out HeadHudIdentity identity)
        {
            identity = default;
            if (!OnMainThread || stopped) return false;
            bool controls = HeadCameraApi.Busy;
            long agent = 0, owner = 0; int instance = 0, slot = -1;
            if (Playing && Selected.HasValue && Plugin.OutputMode.Value != FeedOutput.TestPattern)
            {
                var selected = Selected.Value;
                var target = PlayerManager.Current?.GetPlayerAgentInSlot(selected.Slot);
                if (target != null && !target.IsBeingDestroyed && !target.IsBeingDespawned
                    && target.Pointer.ToInt64() == selected.AgentIdentity && target.Owner?.Pointer.ToInt64() == selected.OwnerIdentity)
                { agent = target.Pointer.ToInt64(); instance = target.GetInstanceID(); owner = target.Owner.Pointer.ToInt64(); slot = target.PlayerSlotIndex; }
            }
            identity = HudViewIdentityPolicy.Resolve(Playing, Opened, hud.IsVisible, Plugin.Enabled.Value,
                controls, HeadCameraApi.Enlarged, agent, instance, owner, slot);
            return true;
        }
        static bool Playing => GameStateManager.CurrentStateName == eGameStateName.InLevel;
        static bool InputAllowed => Playing && Application.isFocused && InputMapper.PlayerHasFocus
            && FocusStateManager.CurrentState == eFocusState.FPS && !PlayerChatManager.InChatMode;
        static bool DisplayAllowed => ViewPolicy.Display(Playing, Application.isFocused, InputAllowed, HeadCameraApi.Busy,
            FocusStateManager.CurrentState == eFocusState.ComputerTerminal);
        public void Update()
        {
            if (stopped) return;
            try
            {
                bool playing = Playing;
                if (!playing || !Plugin.Enabled.Value)
                {
                    if (previouslyInLevel || Opened || capture != null) ResetLevel(playing ? "摄像头配置关闭" : "离开关卡");
                    previouslyInLevel = false; allowed = false; return;
                }
                ObserveFocus();
                previouslyInLevel = true; allowed = DisplayAllowed;
                if (!allowed) { gesture.Reset(); hud.Hide(); frameRate.Reset(); return; }
                // Q owns input while its menu is open; keep rendering the controlled view.
                if (!InputAllowed) { gesture.Reset(); return; }
                bool pressed = Input.GetKeyDown(Plugin.ToggleKey.Value);
                {
                    var intent = gesture.Step(Time.unscaledTime, pressed, Opened, HeadCameraApi.Busy, Selected, HeadCameraApi.DoubleClickWindow);
                    if (intent == ToggleIntent.Open) { SetVisible(true); gesture.CaptureOpenedTarget(Selected); return; }
                    if (intent == ToggleIntent.Close) { SetVisible(false, "T单击关闭观察窗口"); return; }
                    if (intent == ToggleIntent.ExitControl) { HeadCameraApi.ExitControlFor("T键退出接管"); return; }
                    if (intent == ToggleIntent.RejectControl) { ShowControlNotice(gesture.RejectionReason); return; }
                    if (intent == ToggleIntent.RequestControl && gesture.RequestTarget.HasValue) {
                        feedback.Request(gesture.RequestTarget.Value, HeadCameraApi.RequestControl,
                            Plugin.OutputMode.Value == FeedOutput.TestPattern, ShowControlNotice,
                            error => Plugin.Instance.Log.LogWarning("[头戴摄像头] 接管回调异常：" + error));
                        return;
                    }
                }
                if (!Opened) return;
                if (HeadCameraApi.Busy) return;
                int step = Input.GetKeyDown(Plugin.NextKey.Value) ? 1 : Input.GetKeyDown(Plugin.PreviousKey.Value) ? -1 : 0;
                if (step != 0 || Time.unscaledTime >= nextTargets) RefreshTargets(step);
            }
            catch (Exception error) { ReportFailure("更新／输入", error); }
            finally { HeadCameraApi.NotifyHudViewIdentityChanged(); }
        }
        void RefreshTargets(int step)
        {
            nextTargets = Time.unscaledTime + .5f;
            var local = PlayerManager.GetLocalPlayerAgent();
            targets.Clear();
            if (local == null) { Selected = null; return; }
            for (int slot = 0; slot < 4; slot++)
            {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (agent == null || agent.Pointer == local.Pointer || agent.Owner == null || !agent.Alive
                    || agent.IsBeingDestroyed || agent.IsBeingDespawned) continue;
                targets.Add(new CameraTarget(slot, agent.Pointer.ToInt64(), agent.Owner.Pointer.ToInt64(), agent.PlayerName, agent.Owner.IsBot));
            }
            int index = ViewPolicy.Select(targets, Selected, step);
            var next = index < 0 ? (CameraTarget?)null : targets[index];
            if (Selected.HasValue != next.HasValue || next.HasValue && !next.Value.Same(Selected.Value))
            { Selected = next; clock.Reset(); frameRate.Reset(); message = null; }
        }
        internal bool SetVisible(bool value, string reason = "外部接口关闭摄像头")
        {
            if (!OnMainThread || stopped || value && (!Plugin.Enabled.Value || !InputAllowed)) return false;
            if (!value && HeadCameraApi.Busy) HeadCameraApi.ExitControlFor(reason);
            session.SetVisible(value);
            feedback.Clear();
            frameRate.Reset();
            if (value)
            {
                failures.Reset(); clock.Reset(); RefreshTargets(0);
                Plugin.Instance.Log.LogInfo("[头戴摄像头] 打开；可选队友=" + targets.Count);
            }
            else
            {
                hud.Hide(); DisposeCapture(); clock.Reset(); failures.Reset(); message = null;
                Plugin.Instance.Log.LogInfo("[头戴摄像头] 已关闭并释放额外渲染资源");
            }
            HeadCameraApi.NotifyHudViewIdentityChanged(); return true;
        }
        internal void ShowControlFeedback(string text)
        {
            if (!OnMainThread || stopped) return;
            feedback.Store(text, Time.unscaledTime);
        }
        internal void ShowControlNotice(string text)
        {
            if (!OnMainThread || stopped || !feedback.Store(text, Time.unscaledTime)) return;
            Plugin.Instance.Log.LogInfo("[接管提示] " + text);
            try { GuiManager.PlayerLayer?.m_gameEventLog?.AddLogItem("[接管] " + text, eGameEventChatLogType.GameEvent); }
            catch (Exception error) { Plugin.Instance.Log.LogWarning("[接管提示] 事件栏不可用，窗口与日志已记录：" + error.Message); }
        }
        internal void ObserveFocus()
        {
            if (stopped || !OnMainThread) return;
            if (!session.ObserveTerminal(FocusStateManager.CurrentState == eFocusState.ComputerTerminal)) return;
            allowed = false;
            SetVisible(false, "进入终端关闭摄像头");
            Plugin.Instance.Log.LogInfo("[头戴摄像头] 进入终端，摄像头已关闭；离开后按 " + Plugin.KeyLabel(Plugin.ToggleKey.Value) + " 重新打开。");
        }
        internal bool SelectSlot(int slot)
        {
            if (!OnMainThread || !Opened || !InputAllowed || HeadCameraApi.Busy) return false;
            RefreshTargets(0);
            foreach (var target in targets) if (target.Slot == slot) { Selected = target; clock.Reset(); frameRate.Reset(); message = null; return true; }
            return false;
        }
        public void LateUpdate()
        {
            if (stopped) return;
            try
            {
                // Focus may change after Update, e.g. the native terminal interaction this frame.
                ObserveFocus();
                if (!Opened || !allowed || !DisplayAllowed || !Plugin.Enabled.Value) { hud.Hide(); return; }
                if (!hud.Ensure()) return;
                if (Plugin.OutputMode.Value == FeedOutput.TestPattern)
                {
                    if (HeadCameraApi.Busy) HeadCameraApi.ExitControlFor("切换诊断彩条模式");
                    HeadCameraApi.RecordControlAim(null); frameRate.Reset(); DisposeCapture(); hud.ShowTestPattern(feedback.Read(Time.unscaledTime)); return;
                }
                if (!Selected.HasValue) { hud.Show(null, "头戴摄像头", feedback.Read(Time.unscaledTime) ?? ("没有存活队友 · " + Plugin.KeyLabel(Plugin.ToggleKey.Value) + " 关闭")); return; }
                var selected = Selected.Value;
                var target = PlayerManager.Current?.GetPlayerAgentInSlot(selected.Slot);
                var local = PlayerManager.GetLocalPlayerAgent();
                if (target == null || local == null || target.Pointer.ToInt64() != selected.AgentIdentity
                    || target.Owner?.Pointer.ToInt64() != selected.OwnerIdentity || !target.Alive)
                { RefreshTargets(0); hud.Show(null, "头戴摄像头", "观看对象已离队／失效，重新选择中"); return; }
                string title = (selected.IsBot ? "人机 · " : "队友 · ") + selected.Name;
                ControlView? controlView = HeadCameraApi.TryView(selected, out var view) ? view : null;
                // Suspended means the control menu owns input, not that the video is hidden.
                bool publishAim = controlView.HasValue && !controlView.Value.Suspended;
                if (!publishAim) HeadCameraApi.RecordControlAim(null);
                if (HeadCameraApi.Busy && HeadCameraApi.HasViewProvider && !controlView.HasValue) {
                    hud.Show(null, title, HeadCameraApi.ControlStatus?.Invoke() ?? "等待接管视角"); return;
                }
                if (target.DimensionIndex != local.DimensionIndex)
                { frameRate.Reset(); hud.Show(null, title, "其他维度，暂不显示 · " + Plugin.SwitchHint); return; }
                if (target.m_movingCuller?.CurrentNode == null)
                { frameRate.Reset(); hud.Show(null, title, "队友房间尚未加载 · " + Plugin.SwitchHint); return; }
                if (failures.Tripped) { hud.Show(null, title, "渲染停止；" + Plugin.KeyLabel(Plugin.ToggleKey.Value) + " 关闭后重试"); return; }
                if (failures.Ready(Time.unscaledTime) && clock.Due(Time.unscaledTime, Plugin.FramesPerSecond.Value))
                {
                    Camera main = local.FPSCamera?.m_camera;
                    if (main == null || !RenderPipe.Initialized) { hud.Show(null, title, "等待原版相机准备完成"); return; }
                    var watch = Stopwatch.StartNew();
                    capture ??= new NativeCapture();
                    capture.Initialize(main, Plugin.Width.Value);
                    capture.Capture(target, main, controlView);
                    HeadCameraApi.RecordControlAim(publishAim ? capture.CapturedView(controlView.Value) : null);
                    watch.Stop(); elapsedTotal += watch.Elapsed.TotalMilliseconds; elapsedMax = Math.Max(elapsedMax, watch.Elapsed.TotalMilliseconds); captureCount++;
                    failures.Success(); frameRate.Sample(Time.unscaledTime);
                    message = Plugin.KeyLabel(Plugin.ToggleKey.Value) + " 关闭 · " + Plugin.SwitchHint + " · "
                        + (frameRate.Actual > 0 ? frameRate.Actual.ToString("F0") + "/" : "目标 ") + Plugin.FramesPerSecond.Value + " FPS";
                    if (lastLoggedAgent != selected.AgentIdentity || lastLoggedControlled != controlView.HasValue)
                    {
                        lastLoggedAgent = selected.AgentIdentity; lastLoggedControlled = controlView.HasValue;
                        Plugin.Instance.Log.LogInfo("[头戴摄像头] 画面提交：slot=" + selected.Slot + " name=" + selected.Name + " bot=" + selected.IsBot
                            + " controlled=" + controlView.HasValue + " suspended=" + (controlView?.Suspended == true) + " dimension=" + target.DimensionIndex + " texture=" + capture.Texture.width + "×" + capture.Texture.height
                            + " targetPos=" + target.Position + "；画面内容仍需游戏内确认");
                    }
                }
                hud.Show(failures.Ready(Time.unscaledTime) ? capture?.Texture : null, title,
                    feedback.Read(Time.unscaledTime) ?? HeadCameraApi.ControlStatus?.Invoke() ?? message ?? "等待队友画面");
                if (Plugin.Diagnostics.Value && Time.unscaledTime >= nextDiagnostics && captureCount > 0)
                {
                    nextDiagnostics = Time.unscaledTime + 10;
                    Plugin.Instance.Log.LogInfo("[头戴摄像头] CPU 提交耗时（非 GPU）：次数=" + captureCount + " 平均=" + (elapsedTotal / captureCount).ToString("F2") + "ms 最大=" + elapsedMax.ToString("F2") + "ms；实际拍摄=" + frameRate.Actual.ToString("F1") + "FPS 目标=" + Plugin.FramesPerSecond.Value);
                    elapsedTotal = elapsedMax = 0; captureCount = 0;
                }
            }
            catch (Exception error) { ReportFailure("拍摄", error); }
            finally { HeadCameraApi.NotifyHudViewIdentityChanged(); }
        }
        void ReportFailure(string phase, Exception error)
        {
            HeadCameraApi.RecordControlAim(null);
            if (failures.Tripped) return;
            failures.Fail(Time.unscaledTime); frameRate.Reset(); DisposeCapture();
            if (error is CaptureRestoreException) failures.Trip();
            message = failures.Tripped ? "连续失败，渲染已停止；查看日志" : "画面暂不可用，5 秒后重试";
            Plugin.Instance?.Log.LogWarning("[头戴摄像头] opened=" + Opened + " controlling=" + HeadCameraApi.Busy + " tripped=" + failures.Tripped + "；" + phase + "失败；" + message + "\n" + error);
        }
        void DisposeCapture() { var old = capture; capture = null; old?.Dispose(); }
        internal void ResetLevel(string reason = "关卡清理")
        {
            gesture.Reset();
            feedback.Clear();
            if (HeadCameraApi.Busy) HeadCameraApi.ExitControlFor(reason);
            session.SetVisible(false); frameRate.Reset(); allowed = false; Selected = null; targets.Clear(); hud.Hide(); DisposeCapture(); hud.Dispose();
            failures.Reset(); clock.Reset(); message = null; nextTargets = 0; elapsedMax = elapsedTotal = 0; captureCount = 0;
            lastLoggedAgent = 0; lastLoggedControlled = null;
            HeadCameraApi.NotifyHudViewIdentityChanged();
        }
        internal void Shutdown() { if (stopped) return; ResetLevel("摄像头卸载／销毁"); stopped = true; if (Current == this) Current = null; }
        public void OnDestroy() => Shutdown();
    }
}
