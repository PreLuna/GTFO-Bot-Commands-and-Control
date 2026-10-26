using System;
using System.Collections.Generic;
using PreLuna.HeadCamera;
using UnityEngine;
static class LightBufferBindingsTests
{
    static int checks;
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static void Main()
    {
        string[] names = { "_CL_PLL", "_CL_ELL", "_CL_SLL", "_CL_FSL", "_CB_LightClusters", "_LightCluster", "_FogCluster" };
        var main = new ClusteredRendering(); var feed = new ClusteredRendering();
        NativeSetup(main); var before = new LightBufferBindings(main);
        var initial = new Dictionary<string, ComputeBuffer>(Shader.Buffers);
        for (int i = 0; i < 10; i++) {
            // Replay the native SetupLightLists global-buffer overwrite and a render failure.
            try { NativeSetup(feed); new LightBufferBindings(feed).Restore(); throw new Exception("render failed"); }
            catch { }
            finally { before.Restore(); }
            foreach (string name in names) Check(ReferenceEquals(Shader.Buffers[name], initial[name]), "拍摄后必须恢复本体绑定 " + name);
        }
        feed.LightCollector.m_CB_SpotLightList.Disposed = true;
        foreach (string name in names) Check(!Shader.Buffers[name].Disposed, "关闭副相机后全局不得引用已释放缓冲 " + name);
        // A renderer's collector is assigned by native Setup, not by an earlier AddComponent.
        var unused = feed.LightCollector; feed.LightCollector = new();
        new LightBufferBindings(feed).Restore();
        Check(ReferenceEquals(Shader.Buffers["_CL_SLL"], feed.LightCollector.m_CB_SpotLightList), "绑定原生Setup实际创建的收集器");
        Check(!ReferenceEquals(Shader.Buffers["_CL_SLL"], unused.m_CB_SpotLightList), "不绑定旧空收集器");
        new LightBufferBindings(null).Restore();
        Check(Shader.Buffers["_CL_SLL"] == null, "主场景不存在时解除旧绑定");
        Console.WriteLine("PASS: " + checks + " actual light-buffer scope/lifetime checks; native Setup binding effects replayed, no Unity/GPU execution.");
    }
    static void NativeSetup(ClusteredRendering renderer)
    {
        // Independent replay of the native setters confirmed in GameAssembly.
        Shader.SetGlobalBuffer("_CL_PLL", renderer.LightCollector.m_CB_PointLightList);
        Shader.SetGlobalBuffer("_CL_ELL", renderer.LightCollector.m_CB_EffectLightList);
        Shader.SetGlobalBuffer("_CL_SLL", renderer.LightCollector.m_CB_SpotLightList);
        Shader.SetGlobalBuffer("_CL_FSL", renderer.LightCollector.m_CB_FogSpheres);
        Shader.SetGlobalBuffer("_CB_LightClusters", renderer.m_CB_LightClusters);
        Shader.SetGlobalBuffer("_LightCluster", renderer.m_lightCluster);
        Shader.SetGlobalBuffer("_FogCluster", renderer.m_fogCluster);
    }
}
public class ClusteredRendering
{
    public ClusteredLightCollector LightCollector = new();
    public ComputeBuffer m_CB_LightClusters = new(), m_lightCluster = new(), m_fogCluster = new();
}
public class ClusteredLightCollector
{
    public ComputeBuffer m_CB_PointLightList = new(), m_CB_EffectLightList = new(), m_CB_SpotLightList = new(), m_CB_FogSpheres = new();
}
namespace UnityEngine
{
    public class ComputeBuffer { public bool Disposed; }
    public static class Shader
    {
        public static Dictionary<string, ComputeBuffer> Buffers = new();
        public static void SetGlobalBuffer(string name, ComputeBuffer value) => Buffers[name] = value;
    }
}
