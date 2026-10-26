using System;
using PreLuna.HeadCamera;
using UnityEngine;
static class ConfigUpgradeTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static Plugin Existing(int version,bool samples)
    {
        var plugin=new Plugin();
        plugin.Config.Set("内部","配置版本",version);plugin.Config.Set("诊断","保存画面样本",samples);
        plugin.Config.Set("画面","渲染宽度",800);plugin.Config.Set("画面","刷新帧率",12);plugin.Config.Set("小窗","显示宽度",360);
        plugin.Config.Set("画面","视野角度",95f);plugin.Config.Set("按键","下一位队友",KeyCode.Slash);
        return plugin;
    }
    static void Main()
    {
        var old=Existing(1,true);old.Load();
        Check(!Plugin.CaptureSamples.Value&&old.Config.Value<int>("内部","配置版本")==2,"实际Load关闭旧配置中的采样并保存v2标记");
        Check(Plugin.Width.Value==800&&Plugin.FramesPerSecond.Value==12&&Plugin.WindowWidth.Value==360&&Plugin.FieldOfView.Value==95&&Plugin.NextKey.Value==KeyCode.Slash,"实际诊断迁移保留分辨率、主动设定的旧帧率／窗口、FOV及按键");
        Check(Plugin.BufferFix.Value&&Plugin.ProjectionFix.Value&&old.Config.Saves==1,"保留正常渲染修复并沿原入口保存一次");
        Check(Plugin.KeyLabel(KeyCode.LeftArrow)=="LeftArrow"&&Plugin.KeyLabel(KeyCode.RightArrow)=="RightArrow","实际状态栏使用原生字体可显示的ASCII按键名");
        old.Unload();
        var second=Existing(2,true);second.Load();
        Check(Plugin.CaptureSamples.Value,"迁移完成后主动重新开启诊断得到保留");second.Unload();
        var disabled=Existing(1,false);disabled.Load();Check(!Plugin.CaptureSamples.Value,"已关闭的采样保持关闭");disabled.Unload();
        var fresh=new Plugin();fresh.Load();Check(!Plugin.CaptureSamples.Value&&Plugin.Width.Value==1280&&Plugin.FramesPerSecond.Value==30,"新安装正常默认不采样");fresh.Unload();
        var legacy=Existing(0,true);legacy.Config.Set("画面","渲染宽度",480);legacy.Config.Set("按键","下一位队友",KeyCode.RightBracket);legacy.Load();
        Check(!Plugin.CaptureSamples.Value&&Plugin.Width.Value==1280&&Plugin.FramesPerSecond.Value==30&&Plugin.WindowWidth.Value==480&&Plugin.NextKey.Value==KeyCode.RightArrow,"v0仍执行原有默认升级及本次诊断关闭");legacy.Unload();
        var future=Existing(3,true);future.Load();Check(Plugin.CaptureSamples.Value&&future.Config.Value<int>("内部","配置版本")==3,"未来版本标记和主动采样不被降级");future.Unload();
        Console.WriteLine("PASS: "+checks+" actual Plugin.Load config migration/save/key-label assertions; framework and Unity calls mocked.");
    }
}
