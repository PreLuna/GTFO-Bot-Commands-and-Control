using System;
using System.Collections.Generic;
using PreLuna.HeadCamera;
static class OwnedFogLifetimeTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Main()
    {
        var lifetime = new OwnedFogLifetime();
        var spheres = new List<object> { new object() };
        void NativeDestroy(long handle, int instance) { if (!lifetime.Contains(handle, instance)) spheres.Clear(); }
        for (int i = 0; i < 10; i++) {
            long handle = 100 + i; int instance = 200 + i;
            lifetime.Register(handle, instance); bool destroyed = false;
            lifetime.Destroy(handle, () => {
                Check(lifetime.Contains(handle, instance), "销毁回调执行期间仍有自有身份登记");
                NativeDestroy(handle, instance); destroyed = true;
            }, () => destroyed);
            Check(!lifetime.Contains(handle, instance), "销毁完成后才解除身份登记");
            Check(spheres.Count == 1, "反复关闭副相机保留主画面雾球");
        }
        lifetime.Register(500, 600);
        try { lifetime.Destroy(500, () => throw new Exception("destroy failed"), () => false); }
        catch (Exception) { }
        Check(lifetime.Contains(500, 600), "销毁失败且对象仍存在时保留清理保护");
        lifetime.Destroy(500, () => NativeDestroy(500, 600), () => true);
        Check(spheres.Count == 1 && !lifetime.Contains(500, 600), "重试销毁仍受保护并正确注销");
        lifetime.Register(700, 800);
        Check(!lifetime.Contains(700, 801), "同地址不同实例不能取得自有保护");
        NativeDestroy(900, 1000);
        Check(spheres.Count == 0, "真实主相机原生清理仍能清空关卡登记");
        Console.WriteLine("PASS: " + checks + " owned fog destroy lifetime/global collection checks; native cleanup replayed, no Unity/GPU execution.");
    }
}
