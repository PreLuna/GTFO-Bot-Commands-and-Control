using System;
using Player;
using SNetwork;
using UnityEngine;
using PreLuna.HeadCamera;
using PreLuna.Captain.Control;
using PreLuna.Captain.Tasks;
using Il2CppInterop.Runtime.Attributes;
using LevelGeneration;
namespace PreLuna.MicroMaster
{
    public sealed class ControlRuntime : MonoBehaviour
    {
        public ControlRuntime(IntPtr pointer) : base(pointer) { }
        internal static ControlRuntime Current;
        long ticket, body, bodyOwner;
        int bodySlot, mainThread, bodyInstance, targetInstance;
        IntPtr floor, master;
        CameraTarget target;
        BotControlState state = BotControlState.Released;
        bool registered, stopped, suspended, deferredStop, quitting;
        readonly BotFlashlights lights = new();
        internal bool NativeLightControls(PlayerAgent agent)
        { try { return lights.Controls(agent); } catch { return false; } }
        internal bool AllowNativeLightSwitch(PlayerAgent agent, bool enabled, int boundary) => lights.AllowSwitch(agent, enabled, boundary);
        internal IDisposable BeginNativeLightHandoff(PlayerAgent agent) => lights.BeginLightHandoff(agent);
        internal bool OwnsNativeLight(Light light)
        { try { return lights.Owns(light); } catch { return false; } }
        internal bool OwnsNativeLightItem(PlayerAgent agent) { try { return lights.OwnsItem(agent); } catch { return false; } }
        internal void BeforeNativeLightSetup(PlayerAgent owner, uint id, ItemEquippable item) { try { lights.BeforeSetup(owner, id, item); } catch (Exception e) { Plugin.Instance?.Log.LogWarning("[原生接管手电] 初始化保护失败：" + e.GetBaseException().Message); } }
        internal void AfterNativeLightSetup(PlayerAgent owner) { try { lights.AfterSetup(owner); } catch (Exception e) { Plugin.Instance?.Log.LogWarning("[原生接管手电] 初始化收尾失败：" + e.GetBaseException().Message); } }
        internal bool MaySyncNativeLight(PlayerAgent agent) { try { return lights.MaySync(agent); } catch { return false; } }
        internal bool AllowNativeLightData(Light lamp) { try { return lights.AllowData(lamp); } catch (Exception e) { Plugin.Instance?.Log.LogWarning("[原生接管手电] 拒绝异常灯数据：" + e.GetBaseException().Message); return false; } }
        internal void FilterNativeLightWrite(Light lamp, ref float value, int kind) => lights.Filter(lamp, ref value, kind);
        internal void NativeLightRenderDisabled(CL_Light light) => lights.RenderDisabled(light);
        float previousHealth, nextRegister, nextRegistrationNotice;
        string status, lastWait;
        readonly ManualLook look = new();
        NativeManualWalk walk;
        ManualMotionRunner motion;

        PUI_CommunicationMenu menu;
        bool enlarged;
        int qPauseFrame = -10;
        internal bool CommandBusy => ticket != 0 && BotControlApi.CommandBusy(ticket, out _);
        internal bool UiBlocked => FocusStateManager.CurrentState != eFocusState.FPS || !InputMapper.PlayerHasFocus
            || PlayerChatManager.InChatMode || menu?.m_active == true || Input.GetKeyDown(KeyCode.Q) || Time.frameCount <= qPauseFrame + 1;
        internal bool MotionAllowed => ManualMotionPolicy.InputAllowed(BlocksBody && state == BotControlState.Active,
            FocusStateManager.CurrentState == eFocusState.FPS, Application.isFocused, PlayerChatManager.InChatMode, UiBlocked, CommandBusy);
        internal static MoveIntent MoveKeys => ManualMotionPolicy.Keys(Input.GetKey(KeyCode.W), Input.GetKey(KeyCode.A), Input.GetKey(KeyCode.S), Input.GetKey(KeyCode.D));
        internal bool IsOwnWalk(PlayerBotActionBase.Descriptor desc) => walk?.IsOwn(desc) == true;
        internal bool CanWalk(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc) => MotionAllowed && MoveKeys.Moving
            && motion?.InputActive == true && walk?.Bot?.Pointer == bot?.Pointer && IsOwnWalk(desc);
        internal bool CanUpdateWalk(PlayerBotActionWalk action) => !deferredStop && MotionAllowed && MoveKeys.Moving && motion?.InputActive == true && IsOwnWalk(action?.DescBase);
        internal void StopMotionNow()
        {
            try { motion?.Stop(); }
            catch (Exception error) { Plugin.Instance?.Log.LogWarning("[微操大师] 步行停止异常：" + error); Exit("微调停止失败", true); }
        }
        internal void PrepareNativeWalk(PlayerBotActionWalk action)
        {
            if (!IsOwnWalk(action?.DescBase)) return;
            try {
                if (!CanUpdateWalk(action) || !walk.RefreshBeforeNativeStep(ManualMotionPolicy.World(MoveKeys, look.Yaw))) {
                    walk.ClearPath(); deferredStop = true;
                }
            } catch (Exception error) {
                walk.ClearPath(); deferredStop = true;
                Plugin.Instance?.Log.LogWarning("[微操大师] 步行前边界核验失败，延后停止：" + error.GetBaseException().Message);
            }
        }
        internal bool Busy => ticket != 0 && state is BotControlState.Waiting or BotControlState.Active;
        internal bool BlocksBody { get {
            try { return Busy && Application.isFocused && WorldIdentityValid()
                && BotControlApi.Controls(target.AgentIdentity, target.OwnerIdentity); }
            catch (Exception error) { Exit("输入控制校验异常：" + error.GetBaseException().Message, true); return false; }
        } }
        internal long Body => body;
        bool WorldIdentityValid()
        {
            if (!SNet.IsMaster || SNet.Master?.Pointer != master || master == IntPtr.Zero
                || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor?.Pointer != floor) return false;
            var local = PlayerManager.GetLocalPlayerAgent();
            var agent = PlayerManager.Current?.GetPlayerAgentInSlot(target.Slot);
            return local?.Pointer.ToInt64() == body && local.Owner?.Pointer.ToInt64() == bodyOwner
                && local.PlayerSlotIndex == bodySlot && local.GetInstanceID() == bodyInstance && !local.IsBeingDestroyed && !local.IsBeingDespawned
                && agent?.Pointer.ToInt64() == target.AgentIdentity && agent.Owner?.Pointer.ToInt64() == target.OwnerIdentity
                && agent.Owner.IsBot && agent.GetInstanceID() == targetInstance && !agent.IsBeingDestroyed && !agent.IsBeingDespawned;
        }
        internal bool Controls(PlayerAgent agent) {
            try { return agent?.Owner != null && BotControlApi.Controls(agent.Pointer.ToInt64(), agent.Owner.Pointer.ToInt64()); }
            catch (Exception error) { Exit("机器人控制校验异常：" + error.GetBaseException().Message, true); return false; }
        }
        internal bool OwnsBot(PlayerAgent agent) => Controls(agent) || agent?.Owner != null
            && BotControlApi.CommandOwns(agent.Pointer.ToInt64(), agent.Owner.Pointer.ToInt64());
        public void Awake() { mainThread = Environment.CurrentManagedThreadId; NativeLightLifetime.Reset(); Current = this; }
        public void Update()
        {
            if (stopped || suspended || quitting) return;
            try {
                if (!registered && Time.unscaledTime >= nextRegister) {
                    nextRegister = Time.unscaledTime + 1;
                    registered = HeadCameraApi.RegisterControlInput(this, Request, () => Busy, () => Exit("旧摄像头接口退出请求（未提供来源）", true),
                        () => Busy ? status : null, Plugin.DoubleClickWindow.Value);
                    if (registered && (!HeadCameraApi.RegisterControlView(this, View) || !HeadCameraApi.RegisterControlMarker(this, Marker) || !HeadCameraApi.RegisterControlExitReason(this, reason => Exit(reason, true)))) {
                        HeadCameraApi.UnregisterControlInput(this); registered = false;
                    }
                    if (registered) Plugin.Instance.Log.LogInfo("[微操大师] 接管输入与视角入口已注册；双击窗口=" + Plugin.DoubleClickWindow.Value + "秒");
                    else if (Time.unscaledTime >= nextRegistrationNotice) {
                        nextRegistrationNotice = Time.unscaledTime + 10;
                        Plugin.Instance.Log.LogWarning("[微操大师] 接管入口注册尚未成功，正在重试；检查摄像头初始化或是否被其他控制器占用");
                    }
                }
                lights.Tick(Time.unscaledTime);
                if (!Busy) return;
                var local = PlayerManager.GetLocalPlayerAgent();
                string emergency = ControlSafety.Check(Plugin.Enabled.Value, SNet.IsMaster,
                    GameStateManager.CurrentStateName == eGameStateName.InLevel, Application.isFocused,
                    local?.Pointer.ToInt64() == body && local?.Owner?.Pointer.ToInt64() == bodyOwner && local?.PlayerSlotIndex == bodySlot,
                    local?.Alive == true, local?.Damage?.GrabbedAtAll == true,
                    local?.Locomotion != null && ControlSafety.BodyLocomotion((int)local.Locomotion.m_currentStateEnum),
                    Input.GetKeyDown(Plugin.EmergencyKey.Value) || Input.GetKeyDown(KeyCode.Escape));
                if (emergency != null) { Exit(emergency, true); return; }
                if (!WorldIdentityValid()) { Exit("关卡／房主／队员会话身份改变", false); return; }
                if (local.Damage == null) { Exit("本体伤害状态不可用", true); return; }
                float health = local.Damage.Health;
                if (!float.IsFinite(health)) { Exit("本体生命状态无效", true); return; }
                if (ControlSafety.HealthDropped(previousHealth, health)) { Exit("本体受到伤害", true); return; }
                previousHealth = health;
                if (!HeadCameraApi.IsVisible || !HeadCameraApi.TryGetSelected(out var selected)
                    || selected.Slot != target.Slot || selected.AgentIdentity != target.AgentIdentity || selected.OwnerIdentity != target.OwnerIdentity) { Exit("摄像头／观看对象已改变", true); return; }
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(target.Slot);
                if (agent?.Pointer.ToInt64() != target.AgentIdentity || agent.Owner?.Pointer.ToInt64() != target.OwnerIdentity
                    || !agent.Alive || agent.IsBeingDestroyed || agent.IsBeingDespawned || agent.DimensionIndex != local.DimensionIndex) { Exit("机器人失效／离队／维度改变", true); return; }
                var beforeRenew = BotControlApi.Poll(ticket, out var completed);
                if (beforeRenew == BotControlState.Released) { FinishReleased(completed); return; }
                if (!BotControlApi.Renew(ticket)) { Exit("接管心跳未被接受", true); return; }
                var next = BotControlApi.Poll(ticket, out var reason);
                if (next == BotControlState.Active && state != BotControlState.Active) {
                    state = next; previousHealth = health;
                    StartMotion(agent);
                    Plugin.Instance.Log.LogInfo("[微操大师] 接管成功 bot=" + target.Name + "；稳定视角与WASD微调已就绪，本体输入隔离仍生效");
                } else if (next is BotControlState.Failed or BotControlState.Released) Exit(reason ?? "接管服务结束", true);
                else if (next == BotControlState.Waiting && !string.IsNullOrWhiteSpace(reason)) {
                    status = "等待接管：" + reason + " · T／F8取消";
                    if (lastWait != reason) { lastWait = reason; Notice("等待接管：" + reason); }
                }
                if (Busy && state == BotControlState.Active) UpdateMotion(agent);
            } catch (Exception error) {
                Plugin.Instance?.Log.LogWarning("[微操大师] 更新异常，立即归还输入：" + error);
                Exit("运行异常", true);
            }
        }
        void StartMotion(PlayerAgent agent)
        {
            var bot = agent.GetComponent<PlayerAIBot>();
            if (bot == null) throw new InvalidOperationException("机器人运动尚未准备好");
            NativeControlView.Eye(agent); NativeControlView.FieldOfView();
            walk = new NativeManualWalk(bot); motion = new ManualMotionRunner(walk);

            menu = UnityEngine.Object.FindObjectOfType<PUI_CommunicationMenu>();
            enlarged = true; qPauseFrame = -10;
            deferredStop = false;
            lights.Select(bot);
            var direction = agent.TargetLookDir.sqrMagnitude > .001f ? agent.TargetLookDir : agent.Forward;
            look.Reset(new(direction.x, direction.y, direction.z));
        }
        void UpdateMotion(PlayerAgent agent)
        {
            // Global lamps are maintained before the input-session early return in Update.
            if (Input.GetKeyDown(KeyCode.Q)) qPauseFrame = Time.frameCount;
            bool allowed = MotionAllowed && !deferredStop;
            if (allowed) {
                if (ControlHooks.FlashlightPressed()) {
                    Notice(!Plugin.NativeLight.Value || Plugin.LightMode.Value == BotFlashlightMode.AI ? "原生手电：由机器人AI自动控制" : lights.Status(agent) + (Plugin.LightMode.Value == BotFlashlightMode.AllBots ? "；关卡内所有人机常亮，退出接管仍保持；F不关闭" : "；接管期间保持开启，退出后交还AI"));
                }
                bool markClick = Input.GetMouseButtonDown(2);
                if (markClick) MarkFromView();
                if (Input.GetKeyDown(Plugin.ToggleViewKey.Value)) enlarged = !enlarged;
                if (Input.GetKeyDown(Plugin.ResetViewKey.Value)) {
                    var forward = agent.Forward; look.Reset(new(forward.x, 0, forward.z));
                    enlarged = true;
                } else if (!markClick) {
                    ControlHooks.ReadMouse(out float x, out float y);
                    look.Apply(x, y, Plugin.MouseSensitivity.Value, Plugin.InvertMouse.Value);
                }
                var direction = look.Direction;
                walk.Bot.SyncValues.LookDirection = new Vector3(direction.X, direction.Y, direction.Z);
            }
            bool moving = motion.Tick(allowed, MoveKeys, look.Yaw);
            status = UiBlocked ? "界面打开，微调暂停 · F8紧急退出" : "WASD微调 · 中键标记 · Q指令 · " + lights.Status(agent) + " · T退出";
            if (BotControlApi.CommandBusy(ticket, out var command)) status = "指令执行：" + command + " · 微调／标记暂停 · F8紧急退出";
            if (allowed && MoveKeys.Moving && !moving) status = "前方空间／导航不可用，已停止微调 · T退出";
        }
        void FinishReleased(string reason) { Exit(reason ?? "接管已交接", false); HeadCameraApi.TrySetVisible(false); }
        internal void CommandFeedback(string message) { if (Busy) HeadCameraApi.ReportControlFeedback(this, message); }
        ControlMarker? Marker()
        {
            if (!Busy || state != BotControlState.Active || !BlocksBody || !ControlPresentation.TryGetMark(ticket, out var mark)) return null;
            return new ControlMarker(target, new Vector3(mark.X, mark.Y, mark.Z), mark.Label, mark.CanStation);
        }
        void MarkFromView()
        {
            if (!HeadCameraApi.TryGetControlAim(out var rendered) || rendered.Target.Slot != target.Slot
                || rendered.Target.AgentIdentity != target.AgentIdentity || rendered.Target.OwnerIdentity != target.OwnerIdentity) {
                Notice("机器人镜头尚未刷新／不可见，请等画面出现后再中键标记"); return;
            }
            var p = rendered.Position; var d = rendered.Direction.normalized;
            if (!BotControlApi.Mark(ticket, new BotControlRay(p.x, p.y, p.z, d.x, d.y, d.z), out var reason)) Notice(reason);
        }
        [HideFromIl2Cpp]
        ControlView? View(CameraTarget selected)
        {
            if (state != BotControlState.Active || !BlocksBody || selected.AgentIdentity != target.AgentIdentity || selected.OwnerIdentity != target.OwnerIdentity) return null;
            var agent = PlayerManager.Current?.GetPlayerAgentInSlot(target.Slot);
            if (agent?.Pointer.ToInt64() != target.AgentIdentity) return null;
            Vector3 position = NativeControlView.Eye(agent);
            var direction = look.Direction;
            return new ControlView(target, position, new Vector3(direction.X, direction.Y, direction.Z), NativeControlView.FieldOfView(), enlarged, UiBlocked);
        }
        [HideFromIl2Cpp]
        bool Request(CameraTarget selected)
        {
            if (Environment.CurrentManagedThreadId != mainThread) { Plugin.Instance?.Log.LogWarning("[微操大师] 拒绝非游戏主线程接管请求"); return false; }
            if (stopped || quitting) { Notice("接管运行器已停止或游戏正在退出"); return false; }
            if (suspended) { Notice("接管组件已停用，暂不能接管"); return false; }
            if (Busy) { Notice("已有接管会话，请先退出当前接管"); return false; }
            if (!Plugin.Enabled.Value) { Notice("微操大师已关闭"); return false; }
            if (!selected.IsBot) { Notice("只能接管机器人"); return false; }
            if (selected.Slot is < 0 or >= 4) { Notice("机器人槽位已失效"); return false; }
            if (!BotControlApi.CommandsAvailable) { Notice("接管指令接口不可用，请安装本批配套公共核心与指令模块"); return false; }
            var local = PlayerManager.GetLocalPlayerAgent();
            if (local?.Owner == null) { Notice("本体角色尚未就绪，不能接管"); return false; }
            if (local.Damage == null) { Notice("本体伤害状态不可用，不能接管"); return false; }
            if (!Application.isFocused) { Notice("游戏窗口没有焦点，不能接管"); return false; }
            if (PlayerChatManager.InChatMode) { Notice("聊天输入中，请关闭聊天后接管"); return false; }
            if (FocusStateManager.CurrentState != eFocusState.FPS) { Notice("当前处于菜单或终端，请回到人物操作画面后接管"); return false; }
            if (!float.IsFinite(local.Damage.Health)) { Notice("本体生命状态无效，不能接管"); return false; }
            var agent = PlayerManager.Current?.GetPlayerAgentInSlot(selected.Slot);
            if (!SNet.IsMaster) { Notice("仅房主可接管机器人，当前只能观看"); return false; }
            if (GameStateManager.CurrentStateName != eGameStateName.InLevel) { Notice("尚未进入关卡，不能接管"); return false; }
            if (Builder.CurrentFloor == null || SNet.Master == null) { Notice("关卡或房主会话尚未就绪，不能接管"); return false; }
            if (agent?.Pointer.ToInt64() != selected.AgentIdentity || agent.Owner?.Pointer.ToInt64() != selected.OwnerIdentity) {
                Notice("观看对象身份已改变，请重新选择机器人后接管"); return false;
            }
            if (!BotControlApi.Begin(new BotControlTarget(selected.Slot, selected.AgentIdentity, selected.OwnerIdentity), Plugin.Guid, out var nextTicket, out var reason)) {
                Notice("接管未开始：" + (reason ?? "队长接管服务未返回具体原因")); return false;
            }
            ticket = nextTicket; target = selected; body = local.Pointer.ToInt64(); bodyOwner = local.Owner.Pointer.ToInt64(); bodySlot = local.PlayerSlotIndex;
            bodyInstance = local.GetInstanceID(); targetInstance = agent.GetInstanceID(); floor = Builder.CurrentFloor.Pointer; master = SNet.Master.Pointer;
            previousHealth = local.Damage.Health; state = BotControlState.Waiting; lastWait = null;
            status = "等待任务暂停与控制权限 · T／F8取消";
            Plugin.Instance.Log.LogInfo("[微操大师] 等待接管 bot=" + selected.Name + "；本体仍可操作"); return true;
        }
        internal void Damage(Dam_PlayerDamageLocal damage, float before)
        {
            if (!Busy || damage?.Owner?.Pointer.ToInt64() != body) return;
            if (ControlSafety.HealthDropped(before, damage.Health) || !damage.Owner.Alive) Exit("本体受到伤害／倒地", true);
        }
        internal void Exit(string reason, bool recall)
        {
            long old = ticket;
            // Clear the local gate BEFORE callbacks/native cleanup. Cleanup failure cannot retain input.
            ticket = 0; state = BotControlState.Released; status = lastWait = null;
            try {
                try { lights.Release(); }
                finally {
                    try { motion?.Stop(); }
                    finally {
                        try { HeadCameraApi.ClearControlView(this); }
                        finally { if (old != 0) BotControlApi.Release(old, recall); }
                    }
                }
            }
            catch (Exception error) { Plugin.Instance?.Log.LogWarning("[微操大师] 退出清理异常；本体输入已归还：" + error); }
            if (old != 0 && !quitting) Notice("已退出：" + reason + "；本体操作已恢复，旧任务按原规则保留暂停");
        }
        static void Notice(string message)
        {
            if (HeadCameraApi.ReportControlNotice(Current, message)) return;
            Plugin.Instance?.Log.LogInfo("[微操大师] " + message);
            try { GuiManager.PlayerLayer?.m_gameEventLog?.AddLogItem("[微操大师] " + message, eGameEventChatLogType.GameEvent); }
            catch { /* Logging still records the result if the native HUD is unavailable. */ }
        }
        public void LateUpdate()
        {
            // Never remove a native action from inside that action's Update callback.
            if (deferredStop) { deferredStop = false; StopMotionNow(); }
            // Catch a lease/service loss occurring after this frame's Update.
            if (Busy && state == BotControlState.Active && !BlocksBody) {
                if (BotControlApi.Poll(ticket, out var reason) == BotControlState.Released) FinishReleased(reason);
                else Exit("控制权限已失效", true);
            }

        }
        public void OnApplicationFocus(bool focused) { if (!focused && Busy) Exit("窗口失去焦点", true); }
        public void OnApplicationPause(bool paused) { if (paused && Busy) Exit("游戏暂停／切入后台", true); }
        public void OnDisable() { suspended = true; if (Busy) Exit("接管组件停用", true); lights.Stop(); }
        public void OnEnable() => suspended = false;
        public void OnApplicationQuit()
        {
            if (quitting) return;
            quitting = true;
            lights.BeginTeardown(applicationQuit: true);
            Exit("应用退出", false);
            Plugin.Instance?.Log.LogInfo("[灯光生命周期] 应用退出：撤销托管，不恢复原生开关；迟到的受管人机灯初始化已阻止");
        }
        internal void StateChanging(eGameStateName next)
        {
            if (next == eGameStateName.InLevel) { lights.EnterLevel(); return; }
            lights.BeginTeardown(applicationQuit: false);
            if (Busy) Exit("关卡状态切换", false);
        }
        internal void LevelCleanup() { lights.BeginTeardown(applicationQuit: false); Exit("关卡清理", false); }
        internal void Departing(PlayerAgent agent)
        {
            if (agent?.Pointer == PlayerManager.GetLocalPlayerAgent()?.Pointer) lights.BeginTeardown(applicationQuit: false);
            else lights.Remove(agent);
            if (Busy && agent != null && (agent.Pointer.ToInt64() == body || agent.Pointer.ToInt64() == target.AgentIdentity))
                Exit("本体／机器人离队或销毁", false);
        }
        internal void Shutdown(bool discardLights = false)
        {
            if (stopped) return;
            if (discardLights) lights.BeginTeardown(applicationQuit: false);
            Exit("卸载／生命周期结束", !discardLights && !quitting); lights.Stop(); HeadCameraApi.UnregisterControlInput(this);
            registered = false; stopped = true; if (Current == this) Current = null;
        }
        public void OnDestroy() => Shutdown(discardLights: true);
    }
}
