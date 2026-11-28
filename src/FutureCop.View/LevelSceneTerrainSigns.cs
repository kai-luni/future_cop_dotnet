using System.Buffers.Binary;
using FutureCop.Assets;
using FutureCop.Game;

namespace FutureCop.View;

public sealed partial class LevelScene
{
    private readonly List<TerrainSign> _terrainSigns = new();

    private sealed record TerrainSign(Model Model, List<Vertex> Vertices, int X, int Y, int Z, int[] Basis);

    // gLoadActorChunk.c:145-164 creates these Cact actors immediately. Csac records of the same
    // type are separate, gated spawns, not replacements for the signs present at level load.
    private void LoadTerrainSigns(IReadOnlyList<EntityRecord> records)
    {
        foreach (var record in records)
        {
            if (record is not { Container: EntityContainer.Cact, Type: 11, Source.Length: >= 0x2C }) continue;

            if (!_modelsById.TryGetValue(record.Model(), out var model)
                || !model.Vertices.TryGetValue(1, out var vertices) || vertices.Count == 0) continue;
            // Only the stationary signs here: animated props and slope-aligned actors need
            // the type-11 entity update or terrain normal path, respectively.
            if (model.AnimationClips.Count != 0 || (record.U8(0x25) & 1) != 0) continue;

            // gInitType11Entity.c:148-180: surface rank, signed height offset and yaw.
            int mode = unchecked((sbyte)record.U8(0x24));
            int rank = mode switch { 0 => -1, 1 => -2, _ => mode - 2 };
            int y = (LevelGround.PlacementHeight(Map, record.X, record.Z, rank) ?? 0)
                + BinaryPrimitives.ReadInt32LittleEndian(record.Source.AsSpan(0x28));
            var (sin, cos) = RetailAngles.SinCos(record.U16(0x26));
            _terrainSigns.Add(new TerrainSign(model, vertices, record.X, y, record.Z,
                new[] { cos, 0, -sin, 0, 0x10000, 0, sin, 0, cos }));
        }
    }

    // Pixel regression probe: omit just the signs while keeping the same terrain and camera.
    internal bool TerrainSignsDisabled { get; set; }

    private void QueueTerrainSigns(Gte.Mat view, ref int drawOrder)
    {
        if (TerrainSignsDisabled) return;
        foreach (var sign in _terrainSigns)
        {
            var local = new Gte.Mat(sign.Basis,
                ((sign.X - MechWorld.X) >> 4) << 4,
                ((sign.Y - MechWorld.Y) >> 4) << 4,
                ((sign.Z - MechWorld.Z) >> 4) << 4);
            QueueModel(sign.Model, sign.Vertices, 0, ScriptRender.Compose(local, view), 0xFF, ref drawOrder);
        }
    }
}
