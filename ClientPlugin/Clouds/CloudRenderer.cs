using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRage.FileSystem;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;
using ClientPlugin.Shaders;

namespace ClientPlugin.Clouds
{
    /// <summary>
    /// Direct3D 11 rendering orchestrator for volumetric clouds.
    /// Manages shader compilation, constant buffer b8 binding, texture slots t20-t22, and fullscreen quad execution.
    /// </summary>
    public static class CloudRenderer
    {
        private static MyPixelShaders.Id _cloudPixelShader = MyPixelShaders.Id.NULL;
        private static IConstantBuffer _constantBuffer;
        private static volatile CloudSnapshot _currentSnapshot = CloudSnapshot.Empty;
        private static bool _initialized = false;
        private static bool _failedInitialization = false;

        public static void UpdateSnapshot(CloudSnapshot snapshot)
        {
            _currentSnapshot = snapshot ?? CloudSnapshot.Empty;
        }

        public static void Init()
        {
            if (_initialized || _failedInitialization)
                return;

            try
            {
                // Direct3D resources must be created on the render path, not the simulation thread.
                CloudTextures.EnsureInitialized();

                // 1. Create Constant Buffer via MyManagers.Buffers for register(b8)
                int cbSize = Marshal.SizeOf<CloudConstantBufferData>();
                _constantBuffer = MyManagers.Buffers.CreateConstantBuffer(
                    "CloudConstantsBuffer",
                    cbSize,
                    null,
                    ResourceUsage.Dynamic,
                    isGlobal: false
                );

                // 2. Locate and load shader source
                string shaderPath = ResolveShaderPath();
                if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                {
                    MyLog.Default.Error($"[VolumetricClouds] HLSL shader file not found at: {shaderPath}");
                    _failedInitialization = true;
                    return;
                }

                // Flatten includes for cross-platform stability
                string flattenedSource = ShaderFlattener.Flatten(shaderPath);
                string runtimeShaderFile = Path.Combine(MyFileSystem.UserDataPath, "Storage", "VolumetricClouds_Flattened.hlsl");
                string dir = Path.GetDirectoryName(runtimeShaderFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(runtimeShaderFile, flattenedSource);

                // Compile pixel shader via MyPixelShaders
                _cloudPixelShader = MyPixelShaders.Create(runtimeShaderFile, null);

                _initialized = true;
                MyLog.Default.Info("[VolumetricClouds] CloudRenderer successfully initialized.");
            }
            catch (Exception ex)
            {
                _failedInitialization = true;
                MyLog.Default.Error($"[VolumetricClouds] Failed to initialize CloudRenderer: {ex}");
            }
        }

        private static string ResolveShaderPath()
        {
            // Search in active plugin directory or standard paths
            string baseDir = Path.GetDirectoryName(typeof(CloudRenderer).Assembly.Location);
            string localShader = Path.Combine(baseDir, "Shaders", "VolumetricClouds.hlsl");
            if (File.Exists(localShader))
                return localShader;

            string devShader = Path.Combine(baseDir, "..", "..", "ClientPlugin", "Shaders", "VolumetricClouds.hlsl");
            if (File.Exists(devShader))
                return Path.GetFullPath(devShader);

            return localShader;
        }

        public static void Draw(MyRenderContext rc)
        {
            if (!_initialized)
            {
                Init();
                if (!_initialized)
                    return;
            }

            CloudSnapshot snapshot = _currentSnapshot;
            if (snapshot == null || !snapshot.IsValid)
                return;

            if (_cloudPixelShader == MyPixelShaders.Id.NULL || _constantBuffer == null)
                return;

            try
            {
                // 1. Upload snapshot data to Constant Buffer b8
                DataBox box = rc.MapSubresource(_constantBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
                Marshal.StructureToPtr(snapshot.BufferData, box.DataPointer, false);
                rc.UnmapSubresource(_constantBuffer, 0);

                // Bind Constant Buffer to Pixel Shader stage at register b8
                rc.PixelShader.SetConstantBuffer(8, _constantBuffer);

                // 2. Bind Textures
                // t20: 3D Cloud Noise Texture (Perlin-Worley)
                ISrvBindable noiseSrv = CloudTextures.NoiseTextureSrv;
                if (noiseSrv != null)
                {
                    rc.PixelShader.SetSrv(20, noiseSrv);
                }

                // t21: 2D Weather Coverage Map
                ISrvBindable weatherSrv = CloudTextures.WeatherMapSrv;
                if (weatherSrv != null)
                {
                    rc.PixelShader.SetSrv(21, weatherSrv);
                }

                // t22: GBuffer Resolved Depth
                var resolvedDepth = MyGBuffer.Main?.ResolvedDepthStencil;
                if (resolvedDepth != null)
                {
                    rc.PixelShader.SetSrv(22, resolvedDepth.SrvDepth);
                }

                // 3. Set Render Pipeline States
                rc.SetBlendState(MyBlendStateManager.BlendAlphaPremult);
                rc.SetDepthStencilState(MyDepthStencilStateManager.DepthTestReadOnly);

                // Set Pixel Shader
                PixelShader d3dPixelShader = MyPixelShaders.GetShader(_cloudPixelShader);
                rc.PixelShader.Set(d3dPixelShader);

                // 4. Set Render Target to MyGBuffer.Main.LBuffer and Draw Fullscreen Quad
                rc.SetRtv(MyGBuffer.Main.LBuffer);
                MyScreenPass.DrawFullscreenQuad(rc);

                // 5. Cleanup SRVs & CB to avoid state leaks
                rc.PixelShader.SetSrv(20, null);
                rc.PixelShader.SetSrv(21, null);
                rc.PixelShader.SetSrv(22, null);
                rc.PixelShader.SetConstantBuffer(8, null);
            }
            catch (Exception ex)
            {
                MyLog.Default.Error($"[VolumetricClouds] Error during CloudRenderer.Draw: {ex}");
            }
        }

        public static void Dispose()
        {
            try
            {
                if (_constantBuffer != null)
                {
                    MyManagers.Buffers.Dispose(_constantBuffer);
                    _constantBuffer = null;
                }
                _initialized = false;
                _failedInitialization = false;
            }
            catch (Exception ex)
            {
                MyLog.Default.Error($"[VolumetricClouds] Error disposing CloudRenderer: {ex}");
            }
        }
    }
}
