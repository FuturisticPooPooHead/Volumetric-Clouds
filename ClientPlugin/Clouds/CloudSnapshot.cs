using System.Runtime.InteropServices;
using VRageMath;

namespace ClientPlugin.Clouds
{
    /// <summary>
    /// Immutable constant buffer representation mapped directly to HLSL register(b8).
    /// Must strictly align to 16-byte boundaries (std140).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 128)]
    public struct CloudConstantBufferData
    {
        public Vector4 CenterInnerRadius;  // xyz = Camera-relative planet center, w = Inner shell radius
        public Vector4 PoleOuterRadius;    // xyz = Planet rotation axis, w = Outer shell radius
        public Vector4 WindOffsetLow;      // xy = Low cloud UV translation, zw = Wind vector (m/s)
        public Vector4 WindOffsetMidHigh;  // xy = Mid cloud UV offset, zw = High cloud UV offset
        public Vector4 CoverageLayers;     // x = Low %, y = Mid %, z = High %, w = Global density multiplier
        public Vector4 SunDirection;       // xyz = Unit vector towards sun, w = Sun intensity
        public Vector4 AmbientColor;       // rgb = Atmospheric ambient bounce, w = Precipitation factor
        public Vector4 StepQuality;        // x = Primary step count, y = Shadow step count, z = Dither strength, w = Reserved
    }

    /// <summary>
    /// Thread-safe immutable snapshot captured on the game thread and dispatched to the render thread.
    /// </summary>
    public sealed class CloudSnapshot
    {
        public static readonly CloudSnapshot Empty = new CloudSnapshot(false, default);

        public readonly bool IsValid;
        public readonly CloudConstantBufferData BufferData;

        public CloudSnapshot(bool isValid, CloudConstantBufferData bufferData)
        {
            IsValid = isValid;
            BufferData = bufferData;
        }
    }
}
