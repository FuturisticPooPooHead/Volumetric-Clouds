using System;
using System.Globalization;
using VRageMath;

namespace ClientPlugin.Settings.Tools;

public static class Tools
{
    public static string GetLabelOrDefault(string propertyName, string configuredLabel)
    {
        return string.IsNullOrWhiteSpace(configuredLabel) ? propertyName : configuredLabel;
    }

    public static string ToHexStringRgb(this Color color)
    {
        var r = ClampInt((int)Math.Round(color.R * 255f), 0, 255);
        var g = ClampInt((int)Math.Round(color.G * 255f), 0, 255);
        var b = ClampInt((int)Math.Round(color.B * 255f), 0, 255);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:X2}{1:X2}{2:X2}",
            r, g, b);
    }

    public static string ToHexStringRgba(this Color color)
    {
        var r = ClampInt((int)Math.Round(color.R * 255f), 0, 255);
        var g = ClampInt((int)Math.Round(color.G * 255f), 0, 255);
        var b = ClampInt((int)Math.Round(color.B * 255f), 0, 255);
        var a = ClampInt((int)Math.Round(color.A * 255f), 0, 255);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:X2}{1:X2}{2:X2}{3:X2}",
            r, g, b, a);
    }

    public static bool TryParseColorFromHexRgb(this string text, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().TrimStart('#');

        if (text.Length != 6)
            return false;

        if (!byte.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new Color((byte)r, (byte)g, (byte)b, 255);
        return true;
    }

    public static bool TryParseColorFromHexRgba(this string text, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().TrimStart('#');

        if (text.Length != 8)
            return false;

        if (!byte.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ||
            !byte.TryParse(text.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a))
        {
            return false;
        }

        color = new Color((byte)r, (byte)g, (byte)b, (byte)a);
        return true;
    }

    private static int ClampInt(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
