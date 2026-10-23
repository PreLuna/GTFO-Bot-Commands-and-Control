using System;
using System.Collections.Generic;
using System.Reflection;
using PreLuna.Captain.Tasks;
class NativeObject { static int next; public IntPtr Pointer = new(++next); public T TryCast<T>() where T:class => this as T; }
enum eGameStateName { Lobby, InLevel }
enum eResourceContainerSpawnType { Health, AmmoWeapon, AmmoTool, Disinfection }
class AIDangerZone { public enum SeverityEnum { None,Harmful,Deadly,CertainDeath } }
static class GameStateManager { public static eGameStateName CurrentStateName = eGameStateName.InLevel; }
namespace LevelGeneration { static class Builder { public static NativeObject CurrentFloor=new(); } }
namespace UnityEngine { struct Vector3 { } static class Time { public static float time; } }
namespace SNetwork {
    static class SNet { public static bool IsMaster = true; }
    class SNet_Player : NativeObject { public bool IsBot; }
}
namespace BepInEx.Configuration {
    class ConfigEntry<T> { public T Value; }
    class ConfigFile { public ConfigEntry<T> Bind<T>(string a, string b, T value, string c) => new(){Value=value}; }
}
namespace HarmonyLib {
    class Harmony { public readonly List<MethodInfo> Patched = new(); public void Patch(MethodInfo method, HarmonyMethod prefix=null, HarmonyMethod postfix=null) { if(method == null) throw new Exception("missing native method"); Patched.Add(method); } }
    class HarmonyMethod { public int priority; public HarmonyMethod(Type t,string name) { if(t.GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)==null)throw new Exception("missing patch"); } }
    static class Priority { public const int First=800; }
    static class AccessTools {
        public static MethodInfo Method(Type type,string name,Type[] args=null) => args==null ? type.GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic) : type.GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,args,null);
        public static FieldInfo Field(Type type,string name) => type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    }
}
class Item : NativeObject { public string PublicName="医疗包"; public Item Get_pItemData()=>this; }
class ItemEquippable : Item { public Gear.ResourcePackPickup World=new(); }
namespace Gear {
    class ResourcePackPickup : NativeObject { public eResourceContainerSpawnType m_packType; }
    static class ResourcePackFirstPerson {
        public static int Uses;
        public static void ApplyPackBot(Player.PlayerAgent source,Player.PlayerAgent receiver,ItemEquippable item) {
            object[] args={source,receiver,item,-1f};
            bool allow=(bool)ResourceUseTests.Call("BeforeApply",args);
            if(allow){Uses++; source.Bot.Backpack.AmmoStorage.Pack.AmmoInPack=Math.Max(0,source.Bot.Backpack.AmmoStorage.Pack.AmmoInPack-20); receiver.Damage.Health=Math.Min(receiver.Damage.HealthMax,receiver.Damage.Health+20);}
            ResourceUseTests.Call("AfterApply",args);
        }
    }
}
class Damage { public float Health=100, HealthMax=100, Infection; public bool GrabbedAtAll; }
namespace Player {
    enum InventorySlot { None, GearStandard, GearSpecial, GearClass, ResourcePack, Consumable, ConsumableHeavy, InLevelCarry }
    class InventorySlotAmmo { public float AmmoInPack, AmmoMaxCap=100, CostOfBullet=1; }
    class PlayerAmmoStorage {
        public InventorySlotAmmo Pack=new(){AmmoInPack=60}, Main=new(){AmmoInPack=100}, Special=new(){AmmoInPack=100}, Tool=new(){AmmoInPack=100};
        public float MainClip, SpecialClip;
        public InventorySlotAmmo GetInventorySlotAmmo(InventorySlot slot)=>slot switch {InventorySlot.ResourcePack=>Pack, InventorySlot.GearStandard=>Main,InventorySlot.GearSpecial=>Special,InventorySlot.GearClass=>Tool,_=>new()};
        public float GetClipAmmoFromSlot(InventorySlot slot)=>slot==InventorySlot.GearStandard?MainClip:slot==InventorySlot.GearSpecial?SpecialClip:0;
    }
    class BackpackItem { public Item Instance; }
    class PlayerBackpack {
        public ItemEquippable Held=new(); public PlayerAmmoStorage AmmoStorage=new();
        public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item) { item=slot==InventorySlot.ResourcePack&&Held!=null?new(){Instance=Held}:null;return item!=null; }
    }
    static class PlayerBackpackManager {
        public static readonly Dictionary<IntPtr,PlayerBackpack> All=new();
        public static bool TryGetBackpack(SNetwork.SNet_Player owner,out PlayerBackpack pack)=>All.TryGetValue(owner.Pointer,out pack);
        public static bool TryGetItemInLevelFromItemData(Item item,out NativeObject world){world=(item as ItemEquippable)?.World;return world!=null;}
    }
    class PlayerAgent : NativeObject {
        public SNetwork.SNet_Player Owner=new(); public bool Alive=true; public int PlayerSlotIndex, DimensionIndex; public UnityEngine.Vector3 Position; public string PlayerName="Bishop"; public Damage Damage=new(); public PlayerAIBot Bot;
        public T GetComponent<T>() where T:class => Bot as T;
        public bool NeedHealth()=>Damage.Health<Damage.HealthMax;
        public bool NeedWeaponAmmo()=>Bot.Backpack.AmmoStorage.Main.AmmoInPack<100||Bot.Backpack.AmmoStorage.Special.AmmoInPack<100;
        public bool NeedToolAmmo()=>Bot.Backpack.AmmoStorage.Tool.AmmoInPack<100;
        public bool NeedDisinfection()=>Damage.Infection>0;
    }
    class PlayerBotActionBase : NativeObject {
        public PlayerAIBot m_bot; public Descriptor DescBase;
        public class Descriptor : NativeObject {
            public PlayerAIBot Bot; public PlayerBotActionBase ActionBase; public bool Terminated; public float Prio;
            public Descriptor(PlayerAIBot bot){Bot=bot;} public bool IsTerminated()=>Terminated;
        }
    }
    class PlayerBotActionShareResourcePack : PlayerBotActionBase {
        public enum State {Idle,Move,Apply} public State m_state; public Descriptor m_desc;
        public new class Descriptor : PlayerBotActionBase.Descriptor {
            public PlayerAgent Receiver; public ItemEquippable Item; public float Haste;
            public Descriptor(PlayerAIBot bot):base(bot){}
        }
        public bool Update(){object[] args={this,true};bool allow=(bool)ResourceUseTests.Call("UpdateShare",args);return allow;}
    }
    class PlayerBotActionRevive : PlayerBotActionBase { }
    class PlayerBotActionDeploySentryGun : PlayerBotActionBase { }
    class PlayerBotActionDeployTripMine : PlayerBotActionBase { }
    class PlayerBotActionGatherDeployables : PlayerBotActionBase { }
    class PlayerBotActionUnlock : PlayerBotActionBase { }
    class RootPlayerBotAction : PlayerBotActionBase {
        public PlayerBotActionShareResourcePack.Descriptor m_shareResourceAction;
        public void UpdateActionShareResoursePack(ref PlayerBotActionBase.Descriptor desc) { }
    }
    class PlayerAIBot : NativeObject {
        public PlayerAgent Agent; public PlayerBackpack Backpack=new(); public readonly List<PlayerBotActionBase> Actions=new(); public int Requests,Stops; public bool Accept=true,SafeDestination=true;
        public bool TestDangerZones(UnityEngine.Vector3 position,AIDangerZone.SeverityEnum threshold,out AIDangerZone.SeverityEnum found){found=SafeDestination?AIDangerZone.SeverityEnum.None:AIDangerZone.SeverityEnum.CertainDeath;return SafeDestination;}
        public bool RequestAction(PlayerBotActionBase.Descriptor desc) {
            Requests++; object[] args={this,desc,false}; bool allow=(bool)ResourceUseTests.Call("Request",args); bool result=allow?Accept:(bool)args[2];
            if(allow&&result&&desc is PlayerBotActionShareResourcePack.Descriptor share){var action=new PlayerBotActionShareResourcePack{m_bot=this,m_desc=share,DescBase=share};desc.ActionBase=action;Actions.Add(action);}
            ResourceUseTests.Call("RequestResult",this,desc,result); return result;
        }
        public void StopAction(PlayerBotActionBase.Descriptor desc){Stops++;desc.Terminated=true;}
    }
    class PlayerManager {
        public static PlayerManager Current=new(); public readonly PlayerAgent[] Agents=new PlayerAgent[4]; public PlayerAgent GetPlayerAgentInSlot(int slot)=>Agents[slot];
    }
}
class Recorder { public Player.PlayerAIBot m_bot; public Player.RootPlayerBotAction m_rootAction; public void UpdateShareResourceAction(){} }
namespace PreLuna.Captain {
    static class KnownTypes { public static Type Get(string assembly,string name)=>typeof(Recorder); }
    class Logger { public void LogWarning(object message){} }
    class Catalog { public string TranslateItems(string name)=>name; }
    static class Plugin { public static readonly Logger Logger=new(); public static readonly Catalog Catalog=new(); }
}
namespace PreLuna.Captain.Tasks {
    enum ItemTaskStage {Waiting,Moving,Interacting,Complete,Failed,Cancelled}
    static class CaptainCommandCatalog { public static readonly string[] ResourceModes={"只按命令使用","仅允许紧急使用","允许战斗自动补给","恢复原版自动分发"}; }
    static class SingleKillAdapter { public static bool Owns(Player.PlayerAIBot bot)=>false; }
    static class ItemTaskAdapter { public static readonly List<string> Messages=new(); public static bool IsBusy; public static int Returns; public static bool Busy(Player.PlayerAIBot bot)=>IsBusy; public static void Feedback(string text)=>Messages.Add(text); public static void ReturnAfterSupply(Player.PlayerAIBot bot)=>Returns++; }
    static class BotTaskBridge {
        public static bool Available=true,Danger,Execute=true,AcquireOK=true,ReportOK=true; public static int Acquires,Releases;
        public static bool AutoAvailable(Player.PlayerAIBot bot)=>Available;
        public static bool Unsafe(Player.PlayerAIBot bot)=>Danger;
        public static bool Acquire(Player.PlayerAIBot bot,out long token){Acquires++; token=Acquires;return AcquireOK;}
        public static bool CanExecute(Player.PlayerAIBot bot,long token)=>Execute;
        public static bool Report(Player.PlayerAIBot bot,long token,ItemTaskStage stage)=>ReportOK;
        public static void Release(Player.PlayerAIBot bot,long token,ItemTaskStage state)=>Releases++;
    }
}

namespace PreLuna.Captain.Tasks { static class ToolSupplyAdapter { public static bool Authorizes(Player.PlayerAgent source, Player.PlayerAgent receiver, ItemEquippable item)=>false; } }
