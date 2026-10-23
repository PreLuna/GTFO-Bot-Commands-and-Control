using System;
using System.Collections.Generic;
using System.Reflection;
using BoosterImplants;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SNetwork;

// Controlled substitutes for testing the actual adapter's routing/lifecycle. These are never packaged.
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        static long next = 1000;
        public IntPtr Pointer { get; } = new IntPtr(next++);
        public T TryCast<T>() where T : class => this as T;
    }
}
public class Proxy : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T> : List<T>
    {
        public Il2CppReferenceArray(int count) { for (int i = 0; i < count; i++) Add(default); }
        public Il2CppReferenceArray(T[] values) : base(values) { }
        public int Length => Count;
    }
    public class Il2CppStructArray<T> : List<T>
    {
        public Il2CppStructArray(int count) { for (int i = 0; i < count; i++) Add(default); }
        public Il2CppStructArray(T[] values) : base(values) { }
        public int Length => Count;
    }
}
namespace Il2CppSystem
{
    public class Action<T, U> { }
    public class Action<T> { public Proxy Target; }
}
namespace Il2CppInterop.Runtime
{
    public static class IL2CPP {
        static long next=100000; static readonly Dictionary<IntPtr,object> objects=new();
        public static int NativeCalls; public static bool ThrowNative;
        public static IntPtr Il2CppObjectBaseToPtrNotNull(object value) {
            if(value==null)throw new ArgumentNullException();
            var pointer=value is Proxy p?p.Pointer:new IntPtr(next++);objects[pointer]=value;return pointer;
        }
        public static IntPtr Il2CppObjectBaseToPtr(object value)=>value==null?IntPtr.Zero:Il2CppObjectBaseToPtrNotNull(value);
        public static unsafe IntPtr il2cpp_runtime_invoke(IntPtr method,IntPtr self,void** args,ref IntPtr exception) {
            NativeCalls++;if(ThrowNative){exception=new IntPtr(1);return IntPtr.Zero;}
            if(method!=new IntPtr(123))throw new InvalidOperationException("unexpected native method");
            var state=(PlayerBoosterImplantState)objects[self];
            var values=(Il2CppReferenceArray<pBoosterImplantData>)objects[new IntPtr(args[0])];
            state.LoadDatasNative(values);return IntPtr.Zero;
        }
    }
    public static class Il2CppException {public static void RaiseExceptionIfNecessary(IntPtr ex){if(ex!=IntPtr.Zero)throw new InvalidOperationException("mock-native-cleanup-exception");}}
    public static class DelegateSupport { public static T ConvertDelegate<T>(Delegate value) where T : new() => new T(); }
}
namespace UnityEngine
{
    public class Object : Proxy
    {
        public static GameObject Instantiate(GameObject original, Transform parent, bool world) => new GameObject();
        public static void Destroy(Object value) { }
    }
    public class Transform : Object
    {
        public GameObject gameObject; public Transform parent;
        public Transform() { gameObject = new GameObject(); }
        public Transform(GameObject owner) { gameObject = owner; }
    }
    public class GameObject : Object
    {
        public string name = "Mock";
        public readonly Transform transform;
        public GameObject() { transform = new Transform(this); }
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public void SetActive(bool value) { activeSelf = value; }
        public T GetComponent<T>() where T : new() => new T();
    }
    public class MonoBehaviour : Object { public GameObject gameObject = new(); public Transform transform => gameObject.transform; }
    public class BoxCollider2D : Object { public bool enabled; }
    public struct Color { public float a; }
    public class SpriteRenderer : MonoBehaviour { public bool enabled = true; public Color color = new Color { a = 1 }; }
    public struct Vector2 { public float x, y; }
    public static class Time { public static float realtimeSinceStartup; public static int frameCount; }
}
namespace TMPro { public class TextMeshPro : UnityEngine.MonoBehaviour { public string text; } }
public class MainMenuGuiLayer
{
    public UnityEngine.GameObject m_menu; public UnityEngine.GameObject Root = new();
    public bool Visible = true; public bool ThrowOnRead;
    public bool IsVisible() => ThrowOnRead ? throw new InvalidOperationException("native page not ready") : Visible;
}
public class UI_SpriteResizer : UnityEngine.MonoBehaviour
{
    public UnityEngine.SpriteRenderer Sprite = new();
    public UI_SpriteResizer() { Sprite.transform.parent = transform; }
}
namespace BepInEx
{
    public static class Paths { public static string ConfigPath; }
}
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; }
    public class ConfigFile
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<T> Bind<T>(string section, string name, T value, string description)
        { var result = new ConfigEntry<T> { Value = value }; if (section == "机器人强化剂" && name == "启用" && result is ConfigEntry<bool> flag) Enabled = flag; return result; }
    }
}
namespace HarmonyLib
{
    public class HarmonyMethod { public int priority; public HarmonyMethod(Type type, string method) { } }
    public static class Priority { public const int Last = 0, Normal = 400; }
    public class Harmony
    {
        public static int PatchCount;
        public static readonly List<MethodInfo> PatchedTargets = new();
        public static int ThrowOnPatch;
        public Harmony(string id) { }
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null)
        { PatchCount++; PatchedTargets.Add(target); if (PatchCount == ThrowOnPatch) throw new InvalidOperationException("simulated patch failure"); }
        public void UnpatchSelf() { PatchCount = 0; PatchedTargets.Clear(); }
    }
    public static class AccessTools
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, Flags, null, args, null);
        public static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Flags);
    }
}
namespace PreLuna.Captain
{
    public sealed class TestLog
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Infos = new();
        public void LogInfo(string text) { Infos.Add(text); }
        public void LogError(string text) { Warnings.Add(text); }
        public void LogWarning(string text) { Warnings.Add(text); }
    }
    public static class Plugin { public const string Guid = "PreLuna.Captain"; public static TestLog Logger = new(); }
    internal static class KnownTypes {public static Type Get(string assembly,string name)=>Type.GetType(name);}
}
public enum eGameStateName { Lobby, Generating, InLevel, AfterLevel, Startup, Offline }
public class GameStateManager
{
    public static bool IsReady;
    public static eGameStateName CurrentStateName;
    public void ChangeState(eGameStateName state) { CurrentStateName = state; }
}
public enum BoosterImplantCategory { Muted, Bold, Aggressive }
public sealed class BoosterImplant : Proxy
{
    public BoosterImplant() { }
    public BoosterImplant(pBoosterImplantData data) { TemplateId = data.BoosterImplantID; }
    public GameData.BoosterImplantTemplateDataBlock Template => new();
    public uint InstanceId, TemplateId;
    public int Uses;
    public BoosterImplantCategory Category;
    public Il2CppStructArray<pBoosterEffectData> Effects = new(0);
    public Il2CppStructArray<uint> Conditions = new(0);
}
public sealed class BoosterImplantInventoryItem { public BoosterImplant Implant; public bool Prepared; }
public sealed class PersistentInventoryManager
{
    public static PersistentInventoryManager Current = new();
    public static readonly List<BoosterImplantInventoryItem>[] Stock = { new(), new(), new() };
    public static int PrepareCalls, UnprepareCalls, DropCalls;
    public static List<BoosterImplantInventoryItem> GetBoosterImplantInventory(BoosterImplantCategory category) => Stock[(int)category];
    public void PrepareBoosterImplantForInjection(uint id) { PrepareCalls++; }
    public void UnPrepareBoosterImplant(BoosterImplantCategory category) { UnprepareCalls++; }
    public void UnPrepareAllBoosterImplants() { UnprepareCalls++; }
    public void UnprepareAllBoosters() { UnprepareCalls++; }
    public void DropBoosterImplant(uint id) { DropCalls++; }
}
namespace GameData
{
    public enum BoosterEffectCategory { Damage }
    public class BoosterImplantTemplateDataBlock { public BoosterEffectCategory MainEffectType => BoosterEffectCategory.Damage; }
    public class BoosterImplantEffectDataBlock { public static object GetBlock(uint id) => id == 0 ? null : new object(); }
    public class BoosterImplantConditionDataBlock { public static object GetBlock(uint id) => id == 0 ? null : new object(); }
}
namespace Player { public class PlayerAgent : Proxy { public SNet_Player Owner; } }
namespace SNetwork
{
    public class SNet_Slot { public int index; }
    public class SNet_Player : Proxy
    {
        public bool IsBot;
        public string NickName;
        public SNet_Slot PlayerSlot;
        public Player.PlayerAgent PlayerAgent;
    }
    public static class SNet
    {
        public static bool IsMaster;
        public static SNet_PlayerSlotManager Slots = new();
    }
    public class SNet_PlayerSlotManager
    {
        public SNet_Player[] Players = new SNet_Player[4];
        public SNet_Player GetPlayerInSlot(int slot) => Players[slot];
        public void OnResetSession() { }
    }
}
namespace BoosterImplants
{
    public enum BoosterCondition {None,Health,Infection,CarryItem}
    public struct pBoosterEffectData { public uint BoosterEffectID; public float EffectValue; }
    public class pBoosterImplantData
    {
        public const int IMPLANT_EFFECT_SYNC_COUNT = 8, IMPLANT_CONDITION_SYNC_COUNT = 8;
        public uint BoosterImplantID;
        public Il2CppStructArray<pBoosterEffectData> BoosterEffectDatas = new(8);
        public Il2CppStructArray<uint> Conditions = new(8);
        public int ConditionCount, BoosterEffectCount, UseCount;
    }
    public class BoosterImplantInstance { public uint BoosterImplantID; public bool ConditionsMet; }
    public class PlayerBoosterImplantState : Proxy
    {
        public SNet_Player Player;
        public Player.PlayerAgent m_playerAgent;
        public bool _IsEnabled_k__BackingField;
        public bool IsEnabled => _IsEnabled_k__BackingField;
        public Player.PlayerAgent PlayerAgent => m_playerAgent;
        public Il2CppReferenceArray<pBoosterImplantData> BoosterImplantDatas = new(3);
        public Il2CppReferenceArray<BoosterImplantInstance> BoosterImplants = new(0);
        public int Updates, OtherModifier = 77;
        public void Update() { if (IsEnabled && PlayerAgent != null) Updates++; }
        static readonly IntPtr NativeMethodInfoPtr_SetBoosterImplantDatas_Public_Void_Il2CppReferenceArray_1_pBoosterImplantData_0=new(123);
        public static int BadWrapperCalls;
        public bool CheckCondition(BoosterCondition condition)=>false;
        public void SetBoosterImplantDatas(Il2CppReferenceArray<pBoosterImplantData> values) {BadWrapperCalls++;throw new TypeLoadException("broken StructArray<class> wrapper");}
        public void LoadDatasNative(Il2CppReferenceArray<pBoosterImplantData> values) {BoosterImplantDatas=values;BoosterImplants=new(0);}
        public void SetBoosterImplants(Il2CppReferenceArray<BoosterImplantInstance> values) { }
    }
    public class BoosterImplantManager : Proxy
    {
        public static BoosterImplantManager Current = new();
        public readonly Dictionary<IntPtr, PlayerBoosterImplantState> States = new();
        public static bool RejectSync, RejectMapping, EnableOnSync;
        public static int SyncCalls;
        public static Exception SyncException;
        public void Update() { }
        public void SetupBoosterManager() { }
        public void OnLevelCleanup() { }
        public bool UpdateBoosterPlayerSlotMapping(int slot)
        {
            var owner = SNet.Slots.GetPlayerInSlot(slot);
            if (owner == null || RejectMapping) return false;
            if (!States.ContainsKey(owner.Pointer)) States.Add(owner.Pointer, new PlayerBoosterImplantState { Player = owner });
            return true;
        }
        public static bool TryGetPlayerBoosterImplantState(SNet_Player owner, out PlayerBoosterImplantState state) => Current.States.TryGetValue(owner.Pointer, out state);
        public static bool TryGetImplantDatas(SNet_Player owner, out Il2CppReferenceArray<pBoosterImplantData> values)
        { throw new TypeLoadException("模拟本机损坏的旧读取包装：不得调用"); }
        public static bool SyncSetBoosterImplants(SNet_Player owner, Il2CppReferenceArray<pBoosterImplantData> values)
        {
            SyncCalls++; if (RejectSync || !SNet.IsMaster) return false;
            if (SyncException != null) throw SyncException;
            if (!TryGetPlayerBoosterImplantState(owner, out var state)) return false;
            state.LoadDatasNative(values); if (EnableOnSync) state._IsEnabled_k__BackingField = true; return true;
        }
        public static pBoosterImplantData ActiveBoosterImplantToData(BoosterImplant item)
        {
            var result = new pBoosterImplantData { BoosterImplantID = item.TemplateId, UseCount = item.Uses,
                BoosterEffectCount = item.Effects.Length, ConditionCount = item.Conditions.Length };
            for (int i = 0; i < item.Effects.Length && i < 8; i++) result.BoosterEffectDatas[i] = item.Effects[i];
            for (int i = 0; i < item.Conditions.Length && i < 8; i++) result.Conditions[i] = item.Conditions[i];
            return result;
        }
        public static bool TryGetBoosterImplant(SNet_Player owner, BoosterImplantCategory category, out pBoosterImplantData data)
        { data = null; return false; }
    }
}
namespace CellMenu
{
    public interface iCellMenuInputHandler { }
    public class CM_BoosterImplantSlotHolder : UnityEngine.MonoBehaviour
    {
        public Dictionary<BoosterImplantCategory, CM_BoosterImplantSlot> m_categorySlots = new();
        public bool m_isSetup = true;
        public UnityEngine.Vector2 m_size;
        public int m_layout;
        public void Setup(float width, int layout) { }
        public void add_OnBoosterImplantSlotClicked(Il2CppSystem.Action<BoosterImplantCategory, SNet_Player> callback) { }
        public void UpdateSlotItems(SNet_Player player) { }
        public void UpdateBoosterImplantInventory(SNet_Player player) { }
    }
    public class CM_BoosterImplantSlot : UnityEngine.MonoBehaviour
    {
        public CM_BoosterImplantSlot()
        {
            m_boxRoot.transform.parent = transform;
            m_boosterIcon.transform.parent = m_boxRoot.transform;
            m_emptyIcon.transform.parent = m_boxRoot.transform;
        }
        public UnityEngine.GameObject m_boxRoot = new();
        public void OnBtnPress(iCellMenuInputHandler handler) { }
        public void SetBackgroundActive(bool active) { }
        public SNet_Player m_player;
        public BoosterImplant m_boosterInSlot;
        public CM_BoosterIcon m_boosterIcon = new();
        public UnityEngine.SpriteRenderer m_emptyIcon = new();
        public UnityEngine.BoxCollider2D m_slotCollider = new();
        public BoosterImplantCategory m_category;
        public void UpdateSlot(SNet_Player player) { }
        public void SetCategoryText(string text) { }
        public void SetStatusText(string text) { }
        public void UpdateInjectIcon() { }
    }
    public class CM_BoosterIcon : UnityEngine.MonoBehaviour
    {
        public CM_BoosterIcon()
        { m_iconBackgroundRenderer.transform.parent = transform; m_iconForegroundRenderer.transform.parent = transform; }
        public UI_SpriteResizer m_iconBackgroundRenderer = new(), m_iconForegroundRenderer = new();
        public bool IsBoosterIconEnabled => Enabled;
        public bool IsBoosterIconActive => Active;
        public bool Active, Enabled, Empty;
        public bool SetIcon(BoosterImplantCategory category, GameData.BoosterEffectCategory effect) { Empty = false; return true; }
        public bool SetIconEmpty(BoosterImplantCategory category) { Empty = true; return true; }
        public void SetIconEnabled(bool enabled) { Enabled = enabled; m_iconBackgroundRenderer.Sprite.enabled = enabled; m_iconForegroundRenderer.Sprite.enabled = enabled; }
        public void SetIconActive(bool active) { Active = active; m_iconBackgroundRenderer.Sprite.color = new UnityEngine.Color { a = 1 }; m_iconForegroundRenderer.Sprite.color = new UnityEngine.Color { a = 1 }; }
    }
    public class CM_BoosterImplantSlotItem : Proxy
    {
        public uint BoosterInstanceID;
        public bool _IsPreparedSlot_k__BackingField, _IsInjectedSlot_k__BackingField;
        public void LoadData(BoosterImplantInventoryItem item) { }
        public void LoadData(BoosterImplant item, bool touched) { }
        public void PrepareBoosterImplant() { }
        public void UnprepareBoosterImplant() { }
        public void DropBoosterImplant() { }
    }
    public class CM_Item : Proxy
    {
        public Il2CppSystem.Action<int> OnBtnPressCallback;
        public Il2CppReferenceArray<TMPro.TextMeshPro> GetTexts() => new(0);
        public void SetText(string text) { }
    }
    public class CM_ScrollWindow : UnityEngine.MonoBehaviour
    {
        public List<CM_Item> m_nonContentItems = new();
        public void SetVisible(bool visible) { gameObject.SetActive(visible); }
    }
    public class CM_PageBase : UnityEngine.MonoBehaviour
    {
        public bool m_isActive = true; public MainMenuGuiLayer m_guiLayer = new();
        public CM_PageBase() { transform.parent = m_guiLayer.Root.transform; }
    }
    public class CM_PageLoadout : CM_PageBase
    {
        public Il2CppReferenceArray<CM_PlayerLobbyBar> m_playerLobbyBars = new(0);
        public void UpdateReadyState() { }
        public void SetPageActive(bool active) { m_isActive = active; gameObject.SetActive(active); }
        public void OnSetGuiLayerVisible(bool visible) { }
    }
    public class CM_PlayerLobbyBar : UnityEngine.MonoBehaviour
    {
        public CM_PlayerLobbyBar()
        {
            transform.parent = m_parentPage.transform;
            m_boosterImplantAlign.parent = transform;
            m_boosterImplantSlotHolder.transform.parent = m_boosterImplantAlign;
            m_boosterImplantsHeaderText.transform.parent = transform;
        }
        public CM_PageBase m_parentPage = new CM_PageLoadout();
        public void OnEnable() { }
        public void SetHasPlayer(bool a, bool b) { }
        public void HideLoadoutUI(bool a, bool b) { m_boosterImplantAlign.gameObject.SetActive(!a); }
        public void UpdatePlayer(SNet_Player player) { m_player = player; }
        public static List<CM_PlayerLobbyBar> instances = new();
        public SNet_Player m_player;
        public CM_BoosterImplantSlotHolder m_boosterImplantSlotHolder = new();
        public UnityEngine.GameObject m_boosterImplantSlotHolderPrefab;
        public UnityEngine.Transform m_boosterImplantAlign = new UnityEngine.GameObject().transform;
        public TMPro.TextMeshPro m_boosterImplantsHeaderText = new();
        public CM_ScrollWindow m_popupScrollWindow = new();
        public void UpdateBoosterImplantInventory() { }
        public void ShowBoosterImplantSelect(BoosterImplantCategory category, SNet_Player player) { }
        public void ShowBoosterImplantTeamView(BoosterImplantCategory category, SNet_Player player) { }
        public void ShowBoosterImplantSelect(BoosterImplantCategory category, int selected) { }
        public void UpdateBoosterSelectionPoup(BoosterImplantCategory category, int selected) { }
        public void UpdateBoosterImplantContentItem(CM_BoosterImplantSlotItem item) { }
        public class __c__DisplayClass141_0 : Proxy
        {
            public CM_PlayerLobbyBar __4__this { get; set; }
            public void _ShowBoosterImplantSelect_b__0(int index) { }
            public void _ShowBoosterImplantSelect_b__1(int index) { }
            public void _ShowBoosterImplantSelect_b__2(int index) { }
            public void _ShowBoosterImplantSelect_b__3(int index) { }
            public void _ShowBoosterImplantSelect_b__4(int index) { }
        }
    }
}
namespace PreLuna.Captain.Boosters
{
    // Native Unity UI is checked by the production build/JIT; routing tests record display requests.
    static class BotBoosterLayout { public static void Apply(CellMenu.CM_PlayerLobbyBar bar) { } public static void Restore(CellMenu.CM_PlayerLobbyBar bar) { } public static void Reset() { } }
    static class BotBoosterHud {
        public static BoosterSlotView[] Last;
        public static void Show(int slot, string name, BoosterSlotView[] states) { Last = states; }
        public static void Hide(int slot) { }
        public static void Reset() { Last = null; }
    }
}
