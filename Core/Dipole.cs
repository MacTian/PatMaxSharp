namespace PatMaxSharp.Core;

/// <summary>
/// Geometric feature dipole — the atomic unit of PatMax's feature representation.
/// Reconstructed from ccPMFieldDipole (36 bytes, RVA evidence in reports/01-edge-field.md).
/// </summary>
public struct Dipole
{
    /// <summary>Sub-pixel X (pixel-center convention: integer pixel + 0.5).</summary>
    public float X;
    /// <summary>Sub-pixel Y.</summary>
    public float Y;

    /// <summary>Edge weight (magnitude contribution).</summary>
    public float Weight;

    /// <summary>Edge strength (from Gabor magnitude).</summary>
    public float Strength;

    /// <summary>Edge direction in radians (0..2π).</summary>
    public float Angle;

    /// <summary>12-bit quantized angle (as in the original uint16 angle12 field).</summary>
    public ushort Angle12 => (ushort)((int)(Angle * (4096.0 / (2.0 * Math.PI))) & 0xFFF);

    /// <summary>8-bit quantized angle (256 levels, constant 256/(2π) = 40.74366543152521).</summary>
    public byte Angle8 => (byte)((int)(Angle * 40.74366543152521) & 0xFF);

    public bool IsSet => Strength > 0f;

    public override string ToString() =>
        $"Dipole(X={X:F2}, Y={Y:F2}, W={Weight:F3}, S={Strength:F1}, θ={Angle * 180 / Math.PI:F1}°)";
}

/// <summary>
/// Sampled feature point derived from a dipole chain (Featurelet).
/// Reconstructed from ccFeatureletDefs (source record 0x30 B, target 0x38 B).
/// </summary>
public struct Featurelet
{
    /// <summary>Sub-pixel X in training image coordinates.</summary>
    public float X;
    /// <summary>Sub-pixel Y.</summary>
    public float Y;

    /// <summary>8-bit quantized angle (0..255), step 2π/256.</summary>
    public byte Angle8;

    /// <summary>Double-precision angle (radians).</summary>
    public double Angle;

    /// <summary>Edge magnitude (contrast).</summary>
    public float Magnitude;

    /// <summary>Chain index this featurelet belongs to.</summary>
    public int ChainIndex;

    /// <summary>Position along the chain (arc length in pixels).</summary>
    public float ChainPosition;

    /// <summary>Weight used by the scorer (W accumulation).</summary>
    public double Weight;

    public override string ToString() =>
        $"Featurelet(X={X:F2}, Y={Y:F2}, θ8={Angle8}, mag={Magnitude:F1})";
}
