using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ClientPlugin.Settings.Elements;

namespace ClientPlugin.Settings
{
    public class Config : INotifyPropertyChanged
    {
        public static readonly Config Default = new Config();
        public static Config Current => Plugin.Instance?.Config ?? Default;

        [XmlIgnore]
        public string Title => "Volumetric Dynamic Clouds";

        private bool _enabled = true;
        private int _stepQuality = 48; // Default Medium quality
        private bool _enableLiveWeather = true;
        private float _cloudDensityMultiplier = 1.0f;
        private float _windSpeedMultiplier = 1.0f;

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [Separator(caption: "General Settings")]

        [Checkbox(label: "Enable Volumetric Clouds", description: "Toggle real-time volumetric planetary clouds on or off.")]
        public bool Enabled
        {
            get => _enabled;
            set => SetField(ref _enabled, value);
        }

        [Slider(min: 16f, max: 96f, step: 8f, type: SliderAttribute.SliderType.Integer, label: "Raymarch Steps", description: "Number of raymarching samples along the view ray (higher = better quality, lower performance).")]
        public int StepQuality
        {
            get => _stepQuality;
            set => SetField(ref _stepQuality, value);
        }

        [Separator(caption: "Weather Integration")]

        [Checkbox(label: "Open-Meteo Live Weather", description: "Dynamically modulates cloud layers using live telemetry based on planetary latitude and longitude.")]
        public bool EnableLiveWeather
        {
            get => _enableLiveWeather;
            set => SetField(ref _enableLiveWeather, value);
        }

        [Slider(min: 0.1f, max: 3.0f, step: 0.1f, type: SliderAttribute.SliderType.Float, label: "Cloud Density", description: "Global cloud opacity and density multiplier.")]
        public float CloudDensityMultiplier
        {
            get => _cloudDensityMultiplier;
            set => SetField(ref _cloudDensityMultiplier, value);
        }

        [Slider(min: 0.0f, max: 5.0f, step: 0.2f, type: SliderAttribute.SliderType.Float, label: "Wind Speed", description: "Multiplier for cloud layer translation and shear velocity.")]
        public float WindSpeedMultiplier
        {
            get => _windSpeedMultiplier;
            set => SetField(ref _windSpeedMultiplier, value);
        }
    }
}