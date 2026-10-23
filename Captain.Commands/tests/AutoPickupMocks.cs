using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { static long serial; public IntPtr Pointer = new(++serial); }
    public class Component : Object { public Transform transform = new(); public T TryCast<T>() where T:class=>this as T; }
    public class Transform { public Vector3 position; }
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x) { this.x = x; y = z = 0; }
        public float magnitude => MathF.Sqrt(x*x + y*y + z*z);
        public Vector3 normalized => magnitude > 0 ? new() { x=x/magnitude, y=y/magnitude, z=z/magnitude } : default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new() { x=a.x-b.x, y=a.y-b.y, z=a.z-b.z };
        public static float Distance(Vector3 a, Vector3 b) => (a-b).magnitude;
    }
    public static class Time { public static float time, unscaledTime; public static int frameCount; }
    public enum KeyCode { Y, N }
    public static class Input { public static readonly HashSet<KeyCode> Down = new(); public static bool GetKeyDown(KeyCode key) => Down.Contains(key); }
    public static class Application { public static bool isFocused = true; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics { public static bool Blocked; public static bool Raycast(Vector3 a, Vector3 b, float distance, int mask, QueryTriggerInteraction query) => Blocked; }
}
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; }
    public class ConfigFile
    {
        public readonly Dictionary<string, object> Entries = new();
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string help)
        { var entry = new ConfigEntry<T> { Value=value }; Entries[section+"/"+key] = entry; return entry; }
    }
}
namespace HarmonyLib
{
    public class Harmony { public void Patch(System.Reflection.MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
    public class HarmonyMethod { public int priority; public HarmonyMethod(Type type,string method){} }
    public static class Priority { public const int First=800; }
    public static class AccessTools { public static System.Reflection.MethodInfo Method(Type type,string name,Type[] args)=>type.GetMethod(name,args); }
}
public enum eGameStateName { Lobby, InLevel }
public enum eFocusState { FPS, FPS_CommunicationDialog, ComputerTerminal, Map, MainMenu }
public static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
public static class FocusStateManager { public static eFocusState CurrentState=eFocusState.FPS; }
public static class InputMapper { public static bool PlayerHasFocus=true; }
public static class PlayerChatManager { public static bool InChatMode; }
public static class LayerManager { public static int MASK_PLAYER_INTERACT_BLOCKERS=1; }
namespace SNetwork
{
    public static class SNet { public static bool IsMaster=true; }
    public class SNet_Player : UnityEngine.Object { public bool IsBot; }
}
namespace AIGraph
{
    public class Zone : UnityEngine.Object { public int DimensionIndex; public UnityEngine.Object Layer=new(); public static Zone Shared=new(); }
    public struct pCourseNode { public AIG_CourseNode Node; public bool TryGet(out AIG_CourseNode node){node=Node;return node!=null;} }
    public class AIG_CourseNode : UnityEngine.Object { public Zone m_zone=Zone.Shared; }
}
namespace Player
{
    public enum InventorySlot { ResourcePack=4, Consumable=5 }
    public struct pItemData { public InventorySlot slot; }
    public class PlayerLocomotion { public int m_currentStateEnum; }
    public class PlayerAgent : UnityEngine.Object
    {
        public SNetwork.SNet_Player Owner=new(); public bool Alive=true;
        public PlayerLocomotion Locomotion=new(); public AIGraph.AIG_CourseNode CourseNode=new();
        public int DimensionIndex; public UnityEngine.Vector3 Position; public UnityEngine.Vector3 EyePosition => Position;
        public string PlayerName="Bishop"; public PlayerAIBot Bot;
        public T GetComponent<T>() where T:class => Bot as T;
    }
    public class PlayerAIBot : UnityEngine.Object
    {
        public PlayerAgent Agent; public bool AutoAvailable=true, Unsafe; public string InvalidReason;
        public readonly List<PlayerBotActionBase> Actions=new(); public int Stops;
        public bool RequestAction(PlayerBotActionBase.Descriptor desc)=>true;
        public void StopAction(PlayerBotActionBase.Descriptor desc){desc.Terminated=true;Stops++;}
    }
    public class PlayerBotActionBase
    {
        public Descriptor DescBase;
        public class Descriptor : UnityEngine.Object
        { public bool Terminated; public bool IsTerminated()=>Terminated; public T TryCast<T>() where T:class=>this as T; }
    }
    public class PlayerBotActionCollectItem { public class Descriptor:PlayerBotActionBase.Descriptor { public LevelGeneration.ItemInLevel TargetItem; } }
    public class PlayerBotActionUnlock { public class Descriptor:PlayerBotActionBase.Descriptor { public enum TargetTypeEnum { Container,Door } public TargetTypeEnum TargetType; } }
    public class PlayerBotActionHighlight { public class Descriptor:PlayerBotActionBase.Descriptor { public enum TargetTypeEnum { Container,Door } public TargetTypeEnum TargetType; } }
    public class PlayerManager
    {
        public static PlayerManager Current=new(); public readonly PlayerAgent[] Slots=new PlayerAgent[4];
        public PlayerAgent GetPlayerAgentInSlot(int slot) => Slots[slot];
        public static PlayerAgent GetLocalPlayerAgent() => Current.Slots[0];
    }
}
namespace LevelGeneration
{
    public enum eResourceContainerStatus { Closed, Open }
    public enum ePickupItemStatus { PlacedInLevel, PickedUp }
    public class LG_ResourceContainer_Storage { public Core m_core=new(); public class Core { public eResourceContainerStatus Status=eResourceContainerStatus.Open; public AIGraph.AIG_CourseNode SpawnNode=new(); } }
    public struct pPickupPlacement { public AIGraph.pCourseNode node; }
    public class LG_PickupItem_Sync
    {
        public ePickupItemStatus Status=ePickupItemStatus.PlacedInLevel;
        public pPickupPlacement placement;
        public LG_PickupItem_Sync GetCurrentState() => this; public ePickupItemStatus status => Status;
        public T TryCast<T>() where T:class => this as T;
    }
    public class ItemInLevel : UnityEngine.Component
    {
        public UnityEngine.GameObject gameObject=new(); public object ItemDataBlock=new(); public string PublicName="Glow Sticks";
        public LG_ResourceContainer_Storage container; public AIGraph.AIG_CourseNode CourseNode=new();
        public LG_PickupItem_Sync Sync=new(); public Player.pItemData Data=new(){slot=Player.InventorySlot.Consumable};
        public Player.pItemData Get_pItemData() => Data; public LG_PickupItem_Sync GetSyncComponent()=>Sync;
    }
}
namespace PreLuna.Captain
{
    public class MockCatalog { public string TranslateItems(string value)=>value=="Glow Sticks"?"荧光棒":value; }
    public class MockLogger { public readonly List<string> Messages=new(); public void LogInfo(string value)=>Messages.Add(value); }
    public static class Plugin { public static MockCatalog Catalog=new(); public static MockLogger Logger=new(); }
}
namespace PreLuna.Captain.Tasks
{
    internal static class NativePickupAdapter { public static bool Authorizes(Player.PlayerAIBot bot,Player.PlayerBotActionBase.Descriptor desc)=>false; }
    internal static class SpecialItemAccess { public static bool Placed(LevelGeneration.ItemInLevel item)
        => item?.Sync?.GetCurrentState().status==LevelGeneration.ePickupItemStatus.PlacedInLevel; }
    internal static class OpenTaskAdapter
    {
        public static Player.PlayerAIBot Owner;
        public static Player.PlayerBotActionBase.Descriptor Action;
        public static bool Authorizes(Player.PlayerAIBot bot, Player.PlayerBotActionBase.Descriptor desc)
            => Owner != null && Owner == bot && Action != null && Action == desc;
    }
    internal static class ItemWorldIndex { public static readonly List<LevelGeneration.ItemInLevel> Items=new(); }
    internal static class KnownSupplyIndex { public static void Observe(LevelGeneration.ItemInLevel item, SupplySource source) { } }
    internal static class ItemInteractionGeometry {
        public static UnityEngine.Vector3 Point(LevelGeneration.ItemInLevel item)=>item.transform.position;
        public static bool Visible(UnityEngine.Vector3 eye,LevelGeneration.ItemInLevel item)=>!UnityEngine.Physics.Blocked;
    }
    internal static class CaptainMenuInput { public static bool OwnsInput; }
    internal static class BotTaskBridge
    {
        public static bool AutoAvailable(Player.PlayerAIBot bot)=>bot.AutoAvailable;
        public static bool AutoAvailable(Player.PlayerAIBot bot,out string reason) { reason=bot.AutoAvailable?null:"自动仲裁未放行";return bot.AutoAvailable; }
        public static bool Unsafe(Player.PlayerAIBot bot)=>bot.Unsafe;
    }
    internal class ItemTaskSession
    {
        readonly Player.PlayerAIBot bot;
        readonly LevelGeneration.ItemInLevel item;
        public static readonly Dictionary<IntPtr,string> InvalidItems=new();
        public ItemTaskSession(Player.PlayerAIBot bot, ItemTaskKind kind, Player.InventorySlot slot, LevelGeneration.ItemInLevel item, object container, string label, Action cleanup, bool explicitCommand) { this.bot=bot;this.item=item; }
        public string Validate()=>InvalidItems.TryGetValue(item.Pointer,out var reason)?reason:bot.InvalidReason;
    }
    internal static class ItemTaskAdapter
    {
        public static bool CommunicationOpen, Accept=true;
        public sealed class Request { public Player.PlayerAIBot Bot; public LevelGeneration.ItemInLevel Item; public Action<bool> Result; }
        public static readonly List<Request> Requests=new(); public static readonly HashSet<IntPtr> BusyBots=new();
        public static bool Busy(Player.PlayerAIBot bot)=>BusyBots.Contains(bot.Pointer);
        public static string BusyReason(Player.PlayerAIBot bot)=>Busy(bot)?"已有物品任务":null;
        public static void Feedback(string value){}
        public static bool Start(Player.PlayerAIBot bot, ItemTaskKind kind, Player.InventorySlot slot, LevelGeneration.ItemInLevel item, object container, string label, bool explicitCommand, Action<bool> result)
        {
            if (!Accept || Busy(bot)) return false;
            Requests.Add(new(){Bot=bot,Item=item,Result=result}); BusyBots.Add(bot.Pointer); return true;
        }
        public static void Complete(int index, bool success)
        {
            var req=Requests[index]; BusyBots.Remove(req.Bot.Pointer);
            if(success) req.Item.Sync.Status=LevelGeneration.ePickupItemStatus.PickedUp;
            req.Result(success);
        }
    }
    internal static class PickupPromptHud
    {
        public static bool Ready=true, Visible, InputAllowed; public static int Waiting, Shows; public static float Remaining; public static string Item;
        public static bool Show(string bot,string item,float remaining,float duration,int waiting,bool inputAllowed)
        { Visible=Ready; Shows++; Item=item; Remaining=remaining; Waiting=waiting; InputAllowed=inputAllowed; return Ready; }
        public static void Hide(){Visible=false;} public static void Reset(){Visible=false;Shows=Waiting=0;Remaining=0;}
    }
}
