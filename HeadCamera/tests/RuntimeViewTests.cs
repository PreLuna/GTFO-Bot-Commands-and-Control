using System;
using Player;
using UnityEngine;
using PreLuna.HeadCamera;
static class RuntimeViewTests
{
    static int checks;static CameraRuntime runtime;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Reset(bool control=true)
    {
        runtime?.Shutdown();HeadCameraApi.Busy=HeadCameraApi.Suspended=false;HeadCameraApi.Exits=0;HeadCameraApi.Aim=null;
        Time.unscaledTime=0;Application.isFocused=true;InputMapper.PlayerHasFocus=true;FocusStateManager.CurrentState=eFocusState.FPS;
        GameStateManager.CurrentStateName=eGameStateName.InLevel;PlayerManager.Current=new();NativeCapture.Captures=0;NativeCapture.ThrowCapture=false;HeadCameraApi.LastExit=null;
        PlayerManager.Current.Slots[0]=new(){PlayerSlotIndex=0};PlayerManager.Current.Slots[1]=new(){PlayerSlotIndex=1,Owner=new(){IsBot=true}};
        runtime=new((IntPtr)100);runtime.Awake();Check(runtime.SetVisible(true),"测试窗口可打开");HeadCameraApi.Busy=control;
        runtime.Update();runtime.LateUpdate();
    }
    static void Main()
    {
        Reset();Check(CameraHud.Last.IsVisible&&NativeCapture.Captures==1,"实际LateUpdate正常显示并拍摄");
        runtime.ShowControlFeedback("已标记墙面");runtime.LateUpdate();Check(CameraHud.Last.Status=="已标记墙面","指令成功反馈优先于常规接管状态栏");
        HeadCameraApi.Suspended=true;Time.unscaledTime=.1f;runtime.Update();runtime.LateUpdate();
        Check(CameraHud.Last.IsVisible&&NativeCapture.Captures==2&&HeadCameraApi.Aim==null,"暂停微调的Q首帧仍拍摄显示，但不发布可执行标记射线");
        FocusStateManager.CurrentState=eFocusState.Menu;InputMapper.PlayerHasFocus=false;Time.unscaledTime=.2f;runtime.Update();runtime.LateUpdate();
        Check(runtime.Opened&&CameraHud.Last.IsVisible&&NativeCapture.Captures==3&&HeadCameraApi.Exits==0,"Q完整打开不隐藏也不退出接管");
        FocusStateManager.CurrentState=eFocusState.FPS;InputMapper.PlayerHasFocus=true;HeadCameraApi.Suspended=false;Time.unscaledTime=.3f;runtime.Update();runtime.LateUpdate();
        Check(CameraHud.Last.IsVisible&&HeadCameraApi.Aim.HasValue,"关闭Q后视频连续且恢复标记射线");
        Time.unscaledTime=.4f;runtime.Update();HeadCameraApi.Suspended=true;FocusStateManager.CurrentState=eFocusState.Menu;InputMapper.PlayerHasFocus=false;runtime.LateUpdate();
        Check(CameraHud.Last.IsVisible&&HeadCameraApi.Aim==null,"Q在Update与LateUpdate之间打开也不闪断");
        FocusStateManager.CurrentState=eFocusState.ComputerTerminal;Time.unscaledTime=.5f;runtime.Update();runtime.LateUpdate();
        Check(!runtime.Opened&&!CameraHud.Last.IsVisible&&HeadCameraApi.Exits==1,"真正进入终端仍关闭窗口并归还控制");
        Check(HeadCameraApi.LastExit=="进入终端关闭摄像头","终端退出保留具体来源");
        Reset();runtime.SetVisible(false);Check(HeadCameraApi.LastExit=="外部接口关闭摄像头","外部接口关闭来源与T区分");
        Reset();Plugin.OutputMode.Value=FeedOutput.TestPattern;runtime.LateUpdate();Check(HeadCameraApi.LastExit=="切换诊断彩条模式","彩条退出有单独来源");Plugin.OutputMode.Value=FeedOutput.ToneMapped;
        Reset();NativeCapture.ThrowCapture=true;Time.unscaledTime=.1f;runtime.Update();runtime.LateUpdate();
        Check(runtime.Opened&&HeadCameraApi.Busy&&HeadCameraApi.Exits==0,"拍摄异常仅停止画面重试，不自动结束接管");
        Reset(false);FocusStateManager.CurrentState=eFocusState.Menu;InputMapper.PlayerHasFocus=false;Time.unscaledTime=.1f;runtime.Update();runtime.LateUpdate();
        Check(!CameraHud.Last.IsVisible&&runtime.Opened,"普通观看仍可随菜单暂时隐藏，不误关闭会话");
        Reset();Application.isFocused=false;Time.unscaledTime=.1f;runtime.Update();runtime.LateUpdate();
        Check(!CameraHud.Last.IsVisible,"失焦仍隐藏画面");
        runtime.Shutdown();Console.WriteLine("PASS: "+checks+" actual CameraRuntime Update/LateUpdate view and Q transition checks; native rendering mocked.");
    }
}
