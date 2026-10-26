using System;
using System.Collections.Generic;
using System.Linq;
using PreLuna.HeadCamera;
using UnityEngine;
using TMPro;
static class ControlMenuOverlayTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var font=new TMP_FontAsset();var overlay=new ControlMenuOverlay();var native=new PUI_CommunicationMenu();ObjectState.Menu=native;
        native.m_buttons.Add(new(){m_numberText=new(){text="1"},m_lineText=new(){text="打开箱子",color=new(.5f,.5f,.5f,.5f)}});
        native.m_buttons.Add(new(){m_numberText=new(){text="2"},m_lineText=new(){text="退出并驻守"}});
        var renderer=new Renderer();var alreadyHidden=new Renderer{forceRenderingOff=true};native.m_root.Renderers.AddRange(new[]{renderer,alreadyHidden});
        native.m_active=true;overlay.Tick(false,font);Check(!renderer.forceRenderingOff,"普通观察不改变原版菜单");
        overlay.Tick(true,font);var root=GameObject.All.Single(g=>g.name=="PreLuna.HeadCamera.CommandMenu");
        Check(root.activeInHierarchy&&root.GetComponent<Canvas>().sortingOrder>45,"菜单显示层在视频Canvas上方");
        var rows=GameObject.All.Select(g=>g.GetComponent<TextMeshProUGUI>()).Where(t=>t!=null).ToList();
        Check(rows.Any(r=>r.text=="1  打开箱子")&&rows.Any(r=>r.text=="2  退出并驻守"),"直接同步原生选项与原始编号");
        Check(rows.Any(r=>r.text=="1  打开箱子"&&r.color.a==.5f),"保留禁用／淡化提示颜色");
        Check(renderer.forceRenderingOff&&alreadyHidden.forceRenderingOff,"镜像准备好后隐藏原始3D文字防止重复");
        native.m_buttons[0].m_lineText.text="执行中";native.m_highlightedButtonIndex=1;overlay.Tick(true,font);
        Check(rows.Any(r=>r.text=="1  执行中")&&rows.Any(r=>r.text=="2  退出并驻守"&&r.fontStyle==FontStyles.Bold),"动态刷新选项和原生高亮");
        native.m_buttons[1].gameObject.SetActive(false);overlay.Tick(true,font);
        Check(rows.Single(r=>r.text=="2  退出并驻守").gameObject.activeInHierarchy==false,"原版隐藏选项不显示");
        native.m_active=false;overlay.Tick(true,font);
        Check(!root.activeInHierarchy&&!renderer.forceRenderingOff&&alreadyHidden.forceRenderingOff,"关菜单恢复每个原始渲染标志");
        native.m_active=true;overlay.Tick(true,font);overlay.Tick(false,font);
        Check(!renderer.forceRenderingOff&&!root.activeInHierarchy,"退出接管立即恢复原菜单");
        overlay.Tick(true,font);overlay.Dispose();Check(root.Destroyed&&!renderer.forceRenderingOff,"销毁显示层同时归还原版文字");
        overlay=new();overlay.Tick(true,null);Check(!renderer.forceRenderingOff,"字体未就绪不隐藏原菜单");
        Check(GameObject.All.Where(g=>g.GetComponent<TextMeshProUGUI>()!=null).All(g=>!g.GetComponent<TextMeshProUGUI>().raycastTarget),"镜像不获取鼠标或替代原生指令输入");
        var events=new ControlEventOverlay();var log=GuiManager.PlayerLayer.m_gameEventLog;
        var original=new Renderer();var originallyHidden=new Renderer{forceRenderingOff=true};log.m_chatInActiveRoot.Renderers.AddRange(new[]{original,originallyHidden});
        log.m_logItems.Add(new(){m_text=new(){text="已标记地面"}});Time.unscaledTime=0;
        events.Tick(true,font);var eventRoot=GameObject.All.Single(g=>g.name=="PreLuna.HeadCamera.Events");
        Check(eventRoot.activeInHierarchy&&eventRoot.GetComponent<Canvas>().sortingOrder==60,"事件层高于视频，低于Q菜单");
        Check(original.forceRenderingOff&&originallyHidden.forceRenderingOff,"准备好镜像后隐藏原生事件文字");
        var eventText=GameObject.All.Last(g=>g.name=="Text").GetComponent<TextMeshProUGUI>();
        Check(eventText.text.Contains("已标记地面"),"原生标记反馈可见");
        log.m_logItems.Add(new(){m_text=new(){text="队友：此处集合"}});events.Tick(true,font);
        Check(eventText.text.Contains("队友：此处集合")&&eventText.text.Contains("已标记地面"),"聊天与最近反馈同时可见");
        log.m_logItems.Insert(0,new(){m_text=new(){text="前插入的游戏事件"}});events.Tick(true,font);
        Check(eventText.text.Contains("前插入的游戏事件"),"兼容原生池头部插入，不假定新消息位于末尾");
        Time.unscaledTime=8;events.Tick(true,font);Check(!eventRoot.activeInHierarchy&&!original.forceRenderingOff,"旧消息有界过期，不永久覆盖画面");
        PlayerChatManager.InChatMode=true;log.m_txtInput.text="输入：hello";events.Tick(true,font);
        Check(eventText.text.Contains("输入：hello"),"聊天输入保持原生来源，仅镜像显示");
        events.Tick(false,font);Check(!original.forceRenderingOff&&originallyHidden.forceRenderingOff&&!eventRoot.activeInHierarchy,"退出接管恢复原来的事件渲染标志");
        events.Tick(true,font);events.Dispose();Check(eventRoot.Destroyed&&!original.forceRenderingOff,"销毁后归还原生事件栏");
        Console.WriteLine("PASS: "+checks+" actual ControlMenuOverlay presentation/order/native-label/restore checks; Unity mocked, no commands executed.");
    }
}
public static class ObjectState {public static PUI_CommunicationMenu Menu;}
namespace UnityEngine {
    public class Object {public static T FindObjectOfType<T>() where T:class=>ObjectState.Menu as T;public static void Destroy(GameObject go){go.Destroyed=true;}}
    public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;}
    public class Transform:Component {public void SetParent(Transform parent,bool world){}}
    public class RectTransform:Transform {public Vector2 anchorMin,anchorMax,pivot,sizeDelta,anchoredPosition;}
    public class GameObject:Object {
        public static List<GameObject> All=new();public string name;public bool activeInHierarchy=true,Destroyed;public Transform transform;readonly Dictionary<Type,Component> components=new();public List<Renderer> Renderers=new();
        public GameObject(string name="fixture"){this.name=name;transform=new Transform{gameObject=this};All.Add(this);}
        public void SetActive(bool active){activeInHierarchy=active;}
        public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components[typeof(T)]=c;if(c is RectTransform)transform=c as Transform;return c;}
        public T GetComponent<T>() where T:class=>components.TryGetValue(typeof(T),out var c)?c as T:null;
        public IEnumerable<T> GetComponentsInChildren<T>(bool inactive) where T:class=>Renderers.Select(r=>r as T);
    }
    public class Renderer {static int serial;public IntPtr Pointer=new(++serial);public bool forceRenderingOff;}
    public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 zero=>default;}
    public struct Vector3 {}
    public struct Color {public float a;public Color(float r,float g,float b,float alpha){a=alpha;}public static Color white=>new(1,1,1,1);}
    public enum RenderMode {ScreenSpaceOverlay}
    public class Canvas:Component {public RenderMode renderMode;public int sortingOrder;}
    public static class Screen {public static int width=1920,height=1080;}
}
namespace UnityEngine.UI {
    public class Image:Component {public Color color;public bool raycastTarget;}
    public class CanvasScaler:Component {public enum ScaleMode{ScaleWithScreenSize}public ScaleMode uiScaleMode;public Vector2 referenceResolution;public float matchWidthOrHeight;}
}
namespace TMPro {
    public class TMP_FontAsset {}
    public enum FontStyles{Normal,Bold}public enum TextOverflowModes{Ellipsis}public enum TextAlignmentOptions{MidlineLeft,BottomLeft}
    public class TextMeshPro {public string text;public Color color=Color.white;}
    public class TextMeshProUGUI:Component {public TMP_FontAsset font;public float fontSize;public bool richText,raycastTarget;public TextAlignmentOptions alignment;public TextOverflowModes overflowMode;public string text;public Color color;public FontStyles fontStyle;public RectTransform rectTransform=>gameObject.GetComponent<RectTransform>();}
}
public class PUI_CommunicationButton {public GameObject gameObject=new();public TextMeshPro m_numberText,m_lineText;}
public class PUI_CommunicationMenu {public bool m_active;public List<PUI_CommunicationButton> m_buttons=new();public TextMeshPro m_title=new(){text="指令"};public GameObject m_root=new(),gameObject=new();public int m_highlightedButtonIndex;}
namespace PreLuna.HeadCamera {
    public struct CameraTarget {}
    public class Plugin {public static Plugin Instance=new();public Logger Log=new();public class Logger{public void LogWarning(string message){}}}
}

public static class PlayerChatManager {public static bool InChatMode;}
public static class GuiManager {public static Layer PlayerLayer=new();public class Layer {public EventLog m_gameEventLog=new();}public class EventLog {public List<EventItem> m_logItems=new();public TextMeshPro m_txtInput=new();public GameObject m_chatActiveRoot=new(),m_chatInActiveRoot=new();}public class EventItem {static long serial;public IntPtr Pointer=new(++serial);public TextMeshPro m_text;}}
namespace UnityEngine {public static class Time {public static float unscaledTime;}}
