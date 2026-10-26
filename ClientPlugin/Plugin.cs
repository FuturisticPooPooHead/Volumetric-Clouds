using System.Reflection;
using ClientPlugin.Clouds;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRageMath;

// Set the assembly version manually if compiled by Pulsar
#if !DEV_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif

namespace ClientPlugin
{
    public class Plugin : IPlugin
    {
        public const string Name = "VolumetricCloudsPlugin";
        public static Plugin Instance { get; private set; }
        private SettingsGenerator _settingsGenerator;

        public Config Config { get; private set; }
        private readonly WeatherManager _weatherManager = new WeatherManager();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Init(object gameInstance)
        {
            Instance = this;
            Config = ConfigStorage.Load();
            _settingsGenerator = new SettingsGenerator();

            // Patch VRage.Render11 via Harmony
            var harmony = new Harmony(Name);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public void Dispose()
        {
            if (Config != null)
            {
                ConfigStorage.Save(Config);
            }

            CloudTextures.Dispose();
            CloudRenderer.Dispose();
            Instance = null;
        }

        public void Update()
        {
            if (Config == null || !Config.Enabled)
            {
                CloudRenderer.UpdateSnapshot(CloudSnapshot.Empty);
                return;
            }

            // Check if player is near any atmospheric planet
            Vector3D cameraPos = MySector.MainCamera != null ? MySector.MainCamera.Position : Vector3D.Zero;
            MyPlanet closestPlanet = MyPlanets.Static?.GetClosestPlanet(cameraPos);

            if (closestPlanet == null || !closestPlanet.HasAtmosphere)
            {
                CloudRenderer.UpdateSnapshot(CloudSnapshot.Empty);
                return;
            }

            Vector3D planetCenterWorld = closestPlanet.PositionComp.GetPosition();
            float innerRadius = closestPlanet.AverageRadius;
            float outerRadius = closestPlanet.AtmosphereRadius;

            // Camera-relative planet center
            Vector3 centerRel = (Vector3)(planetCenterWorld - cameraPos);
            Vector3 poleAxis = (Vector3)closestPlanet.PositionComp.WorldMatrixRef.Up;

            float dt = 0.01666f;
            _weatherManager.Update(cameraPos, planetCenterWorld, poleAxis, dt * Config.WindSpeedMultiplier, Config.EnableLiveWeather);

            // Sunlight and ambient parameters
            Vector3 sunDir = (Vector3)MySector.DirectionToSunNormalized;

            Vector4 coverage = _weatherManager.CurrentCoverage;
            coverage.W *= Config.CloudDensityMultiplier;

            CloudConstantBufferData cbData = new CloudConstantBufferData
            {
                CenterInnerRadius = new Vector4(centerRel.X, centerRel.Y, centerRel.Z, innerRadius),
                PoleOuterRadius = new Vector4(poleAxis.X, poleAxis.Y, poleAxis.Z, outerRadius),
                WindOffsetLow = new Vector4(_weatherManager.CurrentWindOffset.X, _weatherManager.CurrentWindOffset.Y, _weatherManager.TargetWindVector.X, _weatherManager.TargetWindVector.Y),
                WindOffsetMidHigh = new Vector4(_weatherManager.CurrentWindOffset.X * 1.2f, _weatherManager.CurrentWindOffset.Y * 1.2f, _weatherManager.CurrentWindOffset.X * 1.5f, _weatherManager.CurrentWindOffset.Y * 1.5f),
                CoverageLayers = coverage,
                SunDirection = new Vector4(sunDir.X, sunDir.Y, sunDir.Z, 3.5f),
                AmbientColor = new Vector4(0.18f, 0.22f, 0.28f, 0.0f),
                StepQuality = new Vector4(Config.StepQuality, 4.0f, 1.0f, 0.0f)
            };

            CloudRenderer.UpdateSnapshot(new CloudSnapshot(true, cbData));
        }

        public void OpenConfigDialog()
        {
            _settingsGenerator.SetLayout<Simple>();
            MyGuiSandbox.AddScreen(_settingsGenerator.Dialog);
        }
    }
}
