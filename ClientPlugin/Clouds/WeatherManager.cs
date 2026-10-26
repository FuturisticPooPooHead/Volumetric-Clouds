using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Clouds
{
    /// <summary>
    /// Coordinates asynchronous weather data querying from Open-Meteo, coordinates projection, and frame interpolation.
    /// </summary>
    public sealed class WeatherManager
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private DateTime _lastQueryTime = DateTime.MinValue;
        private readonly TimeSpan _queryInterval = TimeSpan.FromMinutes(15);
        private bool _isQuerying = false;

        // Current smoothly interpolated weather parameters
        public Vector4 CurrentCoverage = new Vector4(0.5f, 0.4f, 0.3f, 1.0f); // Low, Mid, High, Global
        public Vector2 CurrentWindOffset = Vector2.Zero;
        public Vector2 TargetWindVector = new Vector2(5.0f, 0.0f);

        // Target weather parameters for smooth lerping
        private Vector4 _targetCoverage = new Vector4(0.5f, 0.4f, 0.3f, 1.0f);

        /// <summary>
        /// Projects player 3D Cartesian coordinates relative to planet center into latitude and longitude in degrees.
        /// </summary>
        public static void CartesianToSphericalLatLon(Vector3D relPos, Vector3D poleAxis, out double latitude, out double longitude)
        {
            Vector3D normRel = Vector3D.Normalize(relPos);
            double sinPhi = MathHelper.Clamp(Vector3D.Dot(normRel, poleAxis), -1.0, 1.0);
            latitude = Math.Asin(sinPhi) * (180.0 / Math.PI);

            // Construct orthogonal surface tangent frame
            Vector3D arbitrary = Math.Abs(poleAxis.Y) < 0.999 ? Vector3D.Up : Vector3D.Right;
            Vector3D east = Vector3D.Normalize(Vector3D.Cross(poleAxis, arbitrary));
            Vector3D north = Vector3D.Cross(east, poleAxis);

            double x = Vector3D.Dot(normRel, east);
            double y = Vector3D.Dot(normRel, north);
            longitude = Math.Atan2(x, y) * (180.0 / Math.PI);
        }

        public void Update(Vector3D playerPos, Vector3D planetCenter, Vector3D poleAxis, float deltaTime, bool liveWeatherEnabled)
        {
            // Advance wind animation offsets
            CurrentWindOffset += TargetWindVector * (deltaTime * 0.00005f);
            if (CurrentWindOffset.X > 1.0f) CurrentWindOffset.X -= 1.0f;
            if (CurrentWindOffset.Y > 1.0f) CurrentWindOffset.Y -= 1.0f;

            // Smoothly lerp coverage layers to target
            CurrentCoverage = Vector4.Lerp(CurrentCoverage, _targetCoverage, MathHelper.Clamp(deltaTime * 0.5f, 0.0f, 1.0f));

            if (!liveWeatherEnabled)
                return;

            if (_isQuerying || (DateTime.UtcNow - _lastQueryTime) < _queryInterval)
                return;

            Vector3D relPos = playerPos - planetCenter;
            CartesianToSphericalLatLon(relPos, poleAxis, out double lat, out double lon);

            _lastQueryTime = DateTime.UtcNow;
            _isQuerying = true;

            Task.Run(async () =>
            {
                try
                {
                    await FetchWeatherAsync(lat, lon);
                }
                catch (Exception ex)
                {
                    MyLog.Default.Warning($"[VolumetricClouds] Weather query failed: {ex.Message}");
                }
                finally
                {
                    _isQuerying = false;
                }
            });
        }

        private async Task FetchWeatherAsync(double lat, double lon)
        {
            string url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}&current=cloud_cover,cloud_cover_low,cloud_cover_mid,cloud_cover_high,wind_speed_10m,wind_direction_10m",
                lat, lon
            );

            HttpResponseMessage response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Parse JSON fields using lightweight manual token extraction to avoid external dependencies
            float low = ExtractJsonFloat(json, "cloud_cover_low") / 100.0f;
            float mid = ExtractJsonFloat(json, "cloud_cover_mid") / 100.0f;
            float high = ExtractJsonFloat(json, "cloud_cover_high") / 100.0f;
            float windSpeed = ExtractJsonFloat(json, "wind_speed_10m"); // km/h
            float windDirDeg = ExtractJsonFloat(json, "wind_direction_10m");

            float windRad = MathHelper.ToRadians(windDirDeg);
            Vector2 windVec = new Vector2((float)Math.Sin(windRad), (float)Math.Cos(windRad)) * (windSpeed * 0.277f); // convert to m/s

            _targetCoverage = new Vector4(
                MathHelper.Clamp(low, 0.05f, 1.0f),
                MathHelper.Clamp(mid, 0.05f, 1.0f),
                MathHelper.Clamp(high, 0.05f, 1.0f),
                1.0f
            );
            TargetWindVector = windVec;
        }

        private static float ExtractJsonFloat(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return 0.0f;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return 0.0f;

            int start = colonIdx + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;

            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-')) end++;

            string valStr = json.Substring(start, end - start);
            float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float result);
            return result;
        }
    }
}
