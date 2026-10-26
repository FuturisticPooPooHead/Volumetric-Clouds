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
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:X2}{1:X2}{2:X2}",
            (int)Math.Clamp(color.R * 255f, 0, 255),
            (int)Math.Clamp(color.G * 255f, 0, 255),
            (int)Math.Clamp(color.B * 255f, 0, 255));
    }

    public static string ToHexStringRgba(this Color color)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:X2}{1:X2}{2:X2}{3:X2}",
            (int)Math.Clamp(color.R * 255f, 0, 255),
            (int)Math.Clamp(color.G * 255f, 0, 255),
            (int)Math.Clamp(color.B * 255f, 0, 255),
            (int)Math.Clamp(color.A * 255f, 0, 255));
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

        color = new Color(r / 255f, g / 255f, b / 255f, 1f);
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

        color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }
}