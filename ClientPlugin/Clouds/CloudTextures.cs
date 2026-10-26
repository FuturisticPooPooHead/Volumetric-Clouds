using System;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using Device = SharpDX.Direct3D11.Device;

namespace ClientPlugin.Clouds
{
    /// <summary>
    /// ISrvBindable wrapper adapter for Direct3D11 ShaderResourceViews to interface with VRage.Render11.
    /// </summary>
    public sealed class SrvBindableAdapter : ISrvBindable
    {
        public ShaderResourceView Srv { get; }
        public SharpDX.Direct3D11.Resource Resource { get; }
        public string Name { get; }
        public Vector3I Size3 { get; }
        public Vector2I Size => new Vector2I(Size3.X, Size3.Y);

        public SrvBindableAdapter(ShaderResourceView srv, SharpDX.Direct3D11.Resource resource, string name, Vector3I size)
        {
            Srv = srv;
            Resource = resource;
            Name = name;
            Size3 = size;
        }
    }

    /// <summary>
    /// Generates and manages procedural 3D Perlin-Worley noise textures and 2D weather coverage maps.
    /// </summary>
    public static class CloudTextures
    {
        private const int NoiseResolution = 128; // 128x128x128 3D texture
        private const int WeatherResolution = 256;

        private static Texture3D _noiseTexture3D;
        private static ShaderResourceView _noiseSrv;
        private static SrvBindableAdapter _noiseBindable;

        private static Texture2D _weatherTexture2D;
        private static ShaderResourceView _weatherSrv;
        private static SrvBindableAdapter _weatherBindable;

        private static bool _initialized = false;

        public static ISrvBindable NoiseTextureSrv => _noiseBindable;
        public static ISrvBindable WeatherMapSrv => _weatherBindable;

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            try
            {
                Device device = MyRender11.DeviceInstance;
                if (device == null)
                    return;

                Generate3DNoiseTexture(device);
                GenerateDefaultWeatherMap(device);

                _initialized = true;
                MyLog.Default.Info("[VolumetricClouds] CloudTextures initialized with 3D Perlin-Worley and Weather textures.");
            }
            catch (Exception ex)
            {
                MyLog.Default.Error($"[VolumetricClouds] Failed to generate cloud textures: {ex}");
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static unsafe void Generate3DNoiseTexture(Device device)
        {
            int size = NoiseResolution;
            byte[] data = new byte[size * size * size * 4];

            // Worley cellular feature points for cell-distance calculation
            Vector3[] cellPoints = new Vector3[32];
            Random rnd = new Random(1337);
            for (int i = 0; i < cellPoints.Length; i++)
            {
                cellPoints[i] = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
            }

            int index = 0;
            for (int z = 0; z < size; z++)
            {
                float nz = (float)z / size;
                for (int y = 0; y < size; y++)
                {
                    float ny = (float)y / size;
                    for (int x = 0; x < size; x++)
                    {
                        float nx = (float)x / size;
                        Vector3 pos = new Vector3(nx, ny, nz);

                        // Perlin base approximation (Sine harmonic mix)
                        float p1 = (float)Math.Sin(pos.X * Math.PI * 4.0) * (float)Math.Cos(pos.Y * Math.PI * 4.0) * (float)Math.Sin(pos.Z * Math.PI * 4.0);
                        float p2 = (float)Math.Sin(pos.X * Math.PI * 8.0) * (float)Math.Sin(pos.Y * Math.PI * 8.0) * (float)Math.Cos(pos.Z * Math.PI * 8.0);
                        float perlin = Clamp01((p1 * 0.7f + p2 * 0.3f + 1.0f) * 0.5f);

                        // Worley cellular distance
                        float minDist1 = 1.0f;
                        float minDist2 = 1.0f;
                        for (int i = 0; i < cellPoints.Length; i++)
                        {
                            Vector3 diff = pos - cellPoints[i];
                            // Wrap periodic boundary
                            diff.X -= (float)Math.Round(diff.X);
                            diff.Y -= (float)Math.Round(diff.Y);
                            diff.Z -= (float)Math.Round(diff.Z);
                            float d = diff.Length();
                            if (d < minDist1)
                            {
                                minDist2 = minDist1;
                                minDist1 = d;
                            }
                            else if (d < minDist2)
                            {
                                minDist2 = d;
                            }
                        }

                        float worley1 = Clamp01(1.0f - minDist1 * 2.5f);
                        float worley2 = Clamp01(1.0f - minDist2 * 1.8f);
                        float worleyDetail = Clamp01(worley1 * 0.7f + worley2 * 0.3f);

                        // Perlin-Worley composite
                        float perlinWorley = Clamp01(perlin * 0.6f + worley1 * 0.4f);

                        data[index++] = (byte)(perlinWorley * 255.0f);  // R: Perlin-Worley Base
                        data[index++] = (byte)(worley1 * 255.0f);       // G: Detail Worley Low
                        data[index++] = (byte)(worley2 * 255.0f);       // B: Detail Worley Mid
                        data[index++] = (byte)(worleyDetail * 255.0f);  // A: Detail Worley High
                    }
                }
            }

            Texture3DDescription desc = new Texture3DDescription
            {
                Width = size,
                Height = size,
                Depth = size,
                MipLevels = 1,
                Format = Format.R8G8B8A8_UNorm,
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };

            fixed (byte* pData = data)
            {
                SharpDX.DataBox box = new SharpDX.DataBox(new IntPtr(pData), size * 4, size * size * 4);
                _noiseTexture3D = new Texture3D(device, desc, new[] { box });
            }

            _noiseSrv = new ShaderResourceView(device, _noiseTexture3D);
            _noiseBindable = new SrvBindableAdapter(_noiseSrv, _noiseTexture3D, "CloudNoiseTexture3D", new Vector3I(size, size, size));
        }

        private static unsafe void GenerateDefaultWeatherMap(Device device)
        {
            int size = WeatherResolution;
            byte[] data = new byte[size * size * 4];

            int index = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    data[index++] = 200; // R: Low cover default
                    data[index++] = 180; // G: Mid cover default
                    data[index++] = 150; // B: High cover default
                    data[index++] = 0;   // A: Precipitation
                }
            }

            Texture2DDescription desc = new Texture2DDescription
            {
                Width = size,
                Height = size,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };

            fixed (byte* pData = data)
            {
                SharpDX.DataRectangle rect = new SharpDX.DataRectangle(new IntPtr(pData), size * 4);
                _weatherTexture2D = new Texture2D(device, desc, new[] { rect });
            }

            _weatherSrv = new ShaderResourceView(device, _weatherTexture2D);
            _weatherBindable = new SrvBindableAdapter(_weatherSrv, _weatherTexture2D, "CloudWeatherMap2D", new Vector3I(size, size, 1));
        }

        public static void Dispose()
        {
            _noiseSrv?.Dispose();
            _noiseTexture3D?.Dispose();
            _weatherSrv?.Dispose();
            _weatherTexture2D?.Dispose();

            _noiseSrv = null;
            _noiseTexture3D = null;
            _weatherSrv = null;
            _weatherTexture2D = null;
            _noiseBindable = null;
            _weatherBindable = null;
            _initialized = false;
        }
    }
}
