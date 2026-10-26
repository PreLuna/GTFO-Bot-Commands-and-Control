using UnityEngine;
namespace PreLuna.HeadCamera
{
    // SetupLightLists binds these globally, not on the Camera or its command buffer.
    // Rebind the actual renderer's buffers for each view and before owned disposal.
    internal sealed class LightBufferBindings
    {
        readonly ComputeBuffer points, effects, spots, spheres, clusters, lights, fog;
        internal LightBufferBindings(ClusteredRendering renderer)
        {
            var collector = renderer?.LightCollector;
            points = collector?.m_CB_PointLightList; effects = collector?.m_CB_EffectLightList;
            spots = collector?.m_CB_SpotLightList; spheres = collector?.m_CB_FogSpheres;
            clusters = renderer?.m_CB_LightClusters; lights = renderer?.m_lightCluster; fog = renderer?.m_fogCluster;
        }
        internal void Restore()
        {
            Shader.SetGlobalBuffer("_CL_PLL", points); Shader.SetGlobalBuffer("_CL_ELL", effects);
            Shader.SetGlobalBuffer("_CL_SLL", spots); Shader.SetGlobalBuffer("_CL_FSL", spheres);
            Shader.SetGlobalBuffer("_CB_LightClusters", clusters); Shader.SetGlobalBuffer("_LightCluster", lights);
            Shader.SetGlobalBuffer("_FogCluster", fog);
        }
    }
}
