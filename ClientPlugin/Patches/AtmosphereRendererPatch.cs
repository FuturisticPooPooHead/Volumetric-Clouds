using System;
using System.Reflection;
using HarmonyLib;
using VRage.Render11.RenderContext;
using VRage.Utils;
using ClientPlugin.Clouds;

namespace ClientPlugin.Patches
{
    /// <summary>
    /// Hooks MyAtmosphereRenderer.RenderGBuffer to inject volumetric clouds into the HDR LBuffer
    /// immediately after atmospheric scattering is drawn.
    /// </summary>
    [HarmonyPatch]
    public static class AtmosphereRendererPatch
    {
        private static bool _disabledOnError = false;

        public static MethodBase TargetMethod()
        {
            // VRage.Render11.GeometryStage.Rendering.MyAtmosphereRenderer or VRageRender.MyAtmosphereRenderer
            Type targetType = AccessTools.TypeByName("VRageRender.MyAtmosphereRenderer")
                              ?? AccessTools.TypeByName("VRage.Render11.GeometryStage.Rendering.MyAtmosphereRenderer");

            if (targetType == null)
            {
                MyLog.Default.Error("[VolumetricClouds] Could not resolve type for MyAtmosphereRenderer.");
                return null;
            }

            MethodInfo method = AccessTools.Method(targetType, "RenderGBuffer", new[] { typeof(MyRenderContext) });
            if (method == null)
            {
                MyLog.Default.Error("[VolumetricClouds] Could not find method RenderGBuffer(MyRenderContext) on MyAtmosphereRenderer.");
            }

            return method;
        }

        [HarmonyPostfix]
        public static void Postfix(MyRenderContext rc)
        {
            if (_disabledOnError)
                return;

            try
            {
                if (rc != null)
                {
                    CloudRenderer.Draw(rc);
                }
            }
            catch (Exception ex)
            {
                _disabledOnError = true;
                MyLog.Default.Error($"[VolumetricClouds] Exception encountered in RenderGBuffer postfix. Disabling cloud hook: {ex}");
            }
        }
    }
}
