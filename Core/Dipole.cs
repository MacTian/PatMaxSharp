namespace PatMaxSharp.Core;

/// <summary>
/// Geometric feature dipole — the atomic unit of PatMax's feature representation.
///
/// Layout: two sub-pixel coordinates, weight, strength and normal angle - five floats.
/// Early analysis notes described the native counterpart as ccPMFieldDipole, 36 bytes, with
/// RVAs quoted from the native core. cogxpma.dll is WIBU-packed (no .text, no .rdata), so that
/// name, the 36-byte size and the RVAs are unverified claims only and are not asserted here.
/// </summary>
public struct Dipole
{
    /// <summary>Sub-pixel X (pixel-center convention: integer pixel + 0.5).</summary>
    public float X;
    /// <summary>Sub-pixel Y.</summary>
    public float Y;

    /// <summary>Edge weight (magnitude contribution).</summary>
    public float Weight;

    /// <summary>Edge strength (from gradient magnitude).</summary>
    public float Strength;

    /// <summary>Edge normal direction in radians (-pi..pi).</summary>
    public float Angle;

    /// <summary>12-bit quantized angle in [0, 4095] (the angle12 field of the early layout notes).</summary>
    public ushort Angle12 => (ushort)((int)((Angle + Math.PI) * (2048.0 / Math.PI)) & 0xFFF);

    /// <summary>8-bit quantized angle (256 levels).</summary>
    public byte Angle8 => (byte)((int)((Angle + Math.PI) * (128.0 / Math.PI)) & 0xFF);

    /// <summary>True when the dipole carries a gradient (strength above zero).</summary>
    public bool IsSet => Strength > 0f;

    public override string ToString() =>
        $"Dipole(X={X:F2}, Y={Y:F2}, W={Weight:F3}, S={Strength:F1}, theta={Angle * 180 / Math.PI:F1}deg)";
}

/// <summary>
/// Sampled feature point derived from a dipole (Featurelet).
///
/// Early analysis notes named the native record ccFeatureletDefs and quoted a source record
/// of 0x30 B and a target record of 0x38 B. Those sizes describe the packed native core,
/// which cannot be inspected statically, so they stay an unverified claim; this struct's own
/// field layout is what the code uses.
///
/// (X, Y) are in training-image coordinates; (Nx, Ny) is the unit edge normal used for
/// the directional-derivative test, and <see cref="Polarity"/> records whether the edge
/// goes from dark to bright (+1) or bright to dark (-1) along that normal.
/// </summary>
public struct Featurelet
{
    /// <summary>Sub-pixel X in training image coordinates.</summary>
    public float X;
    /// <summary>Sub-pixel Y.</summary>
    public float Y;

    /// <summary>Unit edge normal X (direction of increasing intensity).</summary>
    public float Nx;
    /// <summary>Unit edge normal Y.</summary>
    public float Ny;

    /// <summary>+1 dark-to-bright, -1 bright-to-dark along the normal.</summary>
    public sbyte Polarity;

    /// <summary>Gradient magnitude at train time (contrast of this feature).</summary>
    public float Magnitude;

    /// <summary>Continuous edge normal direction (radians) — equals atan2(Ny, Nx).</summary>
    public double Angle => Math.Atan2(Ny, Nx);

    /// <summary>8-bit quantized angle (0..255) — compatibility field.</summary>
    public byte Angle8 => (byte)((int)((Angle + Math.PI) * (128.0 / Math.PI)) & 0xFF);

    /// <summary>Chain/graph index this featurelet belongs to (diagnostics only).</summary>
    public int ChainIndex;

    /// <summary>Position along the chain (arc length in pixels, diagnostics only).</summary>
    public float ChainPosition;

    /// <summary>Weight used by the scorer (W accumulation).</summary>
    public double Weight;

    public override string ToString() =>
        $"Featurelet(X={X:F2}, Y={Y:F2}, n=({Nx:F2},{Ny:F2}), pol={Polarity}, mag={Magnitude:F1})";
}
