namespace FutureCop.Assets;

/// <summary>
/// One 20-byte record of the 3DHY chunk, read as the <c>struct Input</c> of decomp
/// func_80013F08: a bone transform plus the vertex and normal ranges it applies to.
/// </summary>
/// <param name="MatrixIndex">Bone slot written by func_80013CB8; the parent is slot - 1.</param>
/// <param name="NormalOffset">Start index into the normal block (func_8001010C).</param>
/// <param name="NormalCount">Number of normals transformed by this bone.</param>
/// <param name="VertexOffset">Start index into the vertex block (func_8001047C).</param>
/// <param name="VertexCount">Number of vertices projected by this bone.</param>
/// <param name="Flags">
/// Rotation/translation source bits: 4/2/1 select rx/ry/rz directly, 0x08/0x10/0x20 select
/// x/y/z directly, 0x80 ends the entry list (func_80013CB8, func_80013F08).
/// </param>
public readonly record struct ScriptEntry(
    byte MatrixIndex, byte NormalOffset, byte NormalCount, byte VertexOffset, byte VertexCount,
    byte Pad5, byte Pad6, byte Flags,
    short X, short Y, short Z, short Rx, short Ry, short Rz)
{
    /// <summary>True when bit 0x80 marks this as the last entry (func_80013F08).</summary>
    public bool IsLast => (Flags & 0x80) != 0;
}
