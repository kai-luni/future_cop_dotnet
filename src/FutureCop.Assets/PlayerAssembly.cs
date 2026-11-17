namespace FutureCop.Assets;

/// <summary>A single resource placed into a stationary player pose.</summary>
public readonly record struct PlayerPart(Model Model, int Frame, Gte.Mat Matrix, int Mask);

/// <summary>Player-actor pose reconstructed from the original resource links.</summary>
public static class PlayerAssembly
{
    /// <summary>Joint outputs of func_80052960; termination precedes the copy.</summary>
    public static (Gte.Mat? Third, Gte.Mat? Fifth) FindJoints(Model model, int frame, Gte.Mat root)
    {
        var stack = new Gte.Mat[4];
        stack[0] = root;
        Gte.Mat? third = null, fifth = null;
        for (int i = 0; i < model.Hierarchy.Count && i < 5; i++)
        {
            var e = model.Hierarchy[i];
            if (e.MatrixIndex == 0 || e.MatrixIndex >= stack.Length || stack[e.MatrixIndex - 1].W == null)
                throw new InvalidDataException($"Invalid joint stack slot {e.MatrixIndex}");
            var result = ScriptRender.Compose(ScriptRender.LocalMatrix(e, model.MeshTable, frame, true), stack[e.MatrixIndex - 1]);
            stack[e.MatrixIndex] = result;
            if (e.IsLast) break;
            if (i == 2) third = result;
            if (i == 4) fifth = result;
        }
        return (third, fifth);
    }

    /// <summary>func_80017668/80010234 point placement in engine coordinates.</summary>
    public static (int X, int Y, int Z) Point(Model model, int frame, int part, Gte.Mat matrix)
    {
        Vertex point;
        if (model.IsScripted)
        {
            int index = checked(frame * model.AttachmentPointCount + part);
            if (model.AttachmentPointCount <= 0 || part < 0 || part >= model.AttachmentPointCount || index < 0 || index >= model.AttachmentPoints.Count)
                throw new InvalidDataException($"Missing model {model.Id} attachment point {part} at frame {frame}");
            point = model.AttachmentPoints[index];
        }
        else
        {
            if (model.Header.Length <= 9 || part < 0 || part >= 4 || !model.Vertices.TryGetValue(frame + 1, out var vertices))
                throw new InvalidDataException($"Missing model {model.Id} point data at frame {frame}");
            int index = (int)((model.Header[9] >> (part * 8)) & 0xff);
            if (index >= vertices.Count) throw new InvalidDataException($"Model {model.Id} point index {index} is invalid");
            point = vertices[index];
        }
        var loaded = Gte.Load(matrix);
        int[] translation = { loaded.Trx, loaded.Try, loaded.Trz };
        int[] values = { point.X, point.Y, point.Z };
        var result = new int[3];
        for (int i = 0; i < 3; i++)
        {
            long sum = (long)translation[i] * 4096;
            for (int j = 0; j < 3; j++) sum += (long)loaded.R[i * 3 + j] * values[j];
            int mac = (int)(sum >> 12);
            result[i] = (i == 1 ? -mac : mac) << 4;
        }
        return (result[0], result[1], result[2]);
    }

    /// <summary>Builds the initial walker pose at zero heading; model IDs come from LSRa.</summary>
    public static IReadOnlyList<PlayerPart> InitialWalker(PlayerActor actor, IReadOnlyList<Model> models)
    {
        if (actor.Resources.Count < 5) throw new InvalidDataException($"Actor {actor.Id} lacks player resources");
        Model Get(int slot)
        {
            var reference = actor.Resources[slot];
            if (reference.Type != "Cobj") throw new InvalidDataException($"Actor {actor.Id}, slot {slot}: expected Cobj");
            return models.FirstOrDefault(m => m.Id == reference.Id) ?? throw new InvalidDataException($"Model {reference.Id} not found");
        }
        Model main = Get(0), companion = Get(2), twin = Get(3), extra = Get(4);
        int mainFrame = main.AnimationStarts.Count > 0 ? main.AnimationStarts[0] : 0;
        int companionFrame = companion.AnimationStarts.Count > 0 ? companion.AnimationStarts[0] : 0;
        var mainMatrix = Gte.Mat.Identity(0, 0, 0);
        var anchor = Point(main, mainFrame, 0, mainMatrix);
        var companionMatrix = Gte.Mat.Identity(anchor.X, anchor.Y, anchor.Z);
        var joints = FindJoints(companion, companionFrame, companionMatrix);
        if (joints.Third is null || joints.Fifth is null)
            throw new InvalidDataException($"Model {companion.Id} does not provide both attachment matrices");
        var point2 = Point(companion, companionFrame, 2, companionMatrix);
        var extraMatrix = Gte.Mat.Identity(point2.X, point2.Y, point2.Z);
        return new[] { new PlayerPart(main, mainFrame, mainMatrix, 3),
            new PlayerPart(companion, companionFrame, companionMatrix, 5),
            new PlayerPart(twin, 0, joints.Fifth.Value, 1),
            new PlayerPart(twin, 0, joints.Third.Value, 1),
            new PlayerPart(extra, 0, extraMatrix, 1) };
    }
}
