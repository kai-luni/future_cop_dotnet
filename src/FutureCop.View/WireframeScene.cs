using FutureCop.Assets;

namespace FutureCop.View;

/// <summary>Which subject the viewer shows and how it is posed.</summary>
/// <param name="ModelId">Model id to start with in single-model mode.</param>
/// <param name="Assembly">Show the assembled player actor instead of one model.</param>
/// <param name="BoneFrame">Animation frame fed to the 3DMI sampling of the bone path.</param>
/// <param name="ForceStatic">Ignore the bone path and draw the rigid single list.</param>
public sealed record ViewerOptions(int ModelId = 30, bool Assembly = false, int BoneFrame = 0, bool ForceStatic = false);

/// <summary>
/// Platform-independent wireframe renderer: turns one .MIS model (or the assembled player
/// actor) into an RGB framebuffer. Frontends only present the buffer and translate input,
/// so desktop Linux and the Tizen TV build share the exact same projection and walk.
/// </summary>
public sealed class WireframeScene
{
    private const double FillFactor = 0.70;
    private const int MinDistance = 0x480;

    private static readonly byte[][] Palette =
    {
        new byte[] { 255, 255, 255 }, new byte[] { 120, 200, 255 }, new byte[] { 255, 180, 80 },
        new byte[] { 160, 255, 160 }, new byte[] { 255, 120, 200 }, new byte[] { 200, 200, 120 },
        new byte[] { 160, 160, 255 }, new byte[] { 255, 255, 160 },
    };

    private readonly MisFile _file;
    private readonly List<Model> _models;
    private readonly List<PlayerActor> _actors;
    private bool _assemblyMode;
    private readonly bool _forceStatic;
    private readonly int _boneFrame;
    private readonly int _focal;
    private readonly int _offsetX;
    private readonly int _offsetY;

    private int _modelIndex;
    private Model _model;
    private IReadOnlyList<Vertex> _verts;
    private short[] _widths;
    private bool _scripted;
    private int _cx, _cy, _cz, _dist;
    private int _actorIndex;
    private IReadOnlyList<PlayerPart>? _parts;

    public WireframeScene(string misPath, ViewerOptions options, int width = 320, int height = 240)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        // The retail projection takes a focal length and a 16.16 screen offset; keep the
        // 320x240 geometry of the original viewer and scale both with the framebuffer.
        _focal = (int)Math.Round(0x200 * (width / 320.0));
        _offsetX = (width / 2) << 16;
        _offsetY = (height / 2) << 16;

        _assemblyMode = options.Assembly;
        _forceStatic = options.ForceStatic;
        _boneFrame = Math.Max(0, options.BoneFrame);
        Rgb = new byte[width * height * 3];

        _file = new MisFile(misPath);
        _models = _file.Models
            .Where(m => m.Vertices.TryGetValue(1, out var v) && v.Count > 0 && m.Polygons.Count > 0)
            .ToList();
        _actors = _file.PlayerActors
            .GroupBy(a => string.Join(",", a.Resources.Select(r => $"{r.Type}:{r.Id}")))
            .Select(g => g.First())
            .ToList();

        if (_assemblyMode)
        {
            if (_actors.Count == 0) throw new InvalidDataException("No player actors found in mission");
            _model = _models.FirstOrDefault() ?? throw new InvalidDataException("No drawable models in mission");
            _verts = Array.Empty<Vertex>();
            _widths = Array.Empty<short>();
            _parts = PlayerAssembly.InitialWalker(_actors[0], _file.Models);
            FitAssembly();
            Description = $"actor {_actors[0].Id}: models {string.Join(",", _parts.Select(p => p.Model.Id))}";
        }
        else
        {
            int index = _models.FindIndex(m => m.Id == options.ModelId);
            if (index < 0) throw new InvalidDataException($"model {options.ModelId} not drawable in {misPath}");
            _modelIndex = index;
            _model = _models[index];
            (_verts, _widths, _scripted) = ModelState(_model);
            FitModel();
            Description = Describe(_model, _verts, _scripted);
        }
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Top-down RGB buffer, <see cref="Width"/> * <see cref="Height"/> * 3 bytes.</summary>
    public byte[] Rgb { get; }

    /// <summary>Accepted faces of the last <see cref="Render"/> call.</summary>
    public int FaceCount { get; private set; }

    /// <summary>Short status line for HUDs and logs.</summary>
    public string Description { get; private set; }

    public bool Assembly => _assemblyMode;

    /// <summary>Window/HUD title for the current subject.</summary>
    public string Title => _assemblyMode
        ? $"Future Cop wireframe - player actor {_actors[_actorIndex].Id} (experimental)"
        : $"Future Cop wireframe - model {_model.Id}";

    /// <summary>Switches to the previous (-1) or next (+1) model or player actor.</summary>
    public string Select(int step)
    {
        if (step == 0) return Description;
        if (_assemblyMode)
        {
            _actorIndex = (_actorIndex + step + _actors.Count) % _actors.Count;
            _parts = PlayerAssembly.InitialWalker(_actors[_actorIndex], _file.Models);
            FitAssembly();
            Description = $"actor {_actors[_actorIndex].Id}: models {string.Join(",", _parts!.Select(p => p.Model.Id))}";
            return Description;
        }

        return SelectModel(step);
    }

    /// <summary>
    /// Switches to single-model mode (leaving the player assembly if it was active) and
    /// steps through the drawable models of the mission.
    /// </summary>
    public string SelectModel(int step)
    {
        if (step == 0) return Description;
        if (_assemblyMode)
        {
            _assemblyMode = false;
            _parts = null;
            _verts = _model.Vertices[1];
            _widths = Array.Empty<short>();
            _scripted = false;
        }
        _modelIndex = (_modelIndex + step + _models.Count) % _models.Count;
        _model = _models[_modelIndex];
        (_verts, _widths, _scripted) = ModelState(_model);
        FitModel();
        Description = Describe(_model, _verts, _scripted);
        return Description;
    }

    /// <summary>Renders the current subject turned <paramref name="degrees"/> about its Y axis.</summary>
    public void Render(double degrees)
    {
        Array.Clear(Rgb);
        var view = ViewTransform.Turn(degrees, _cx, _cy, _cz, _dist);

        if (_parts is not null)
        {
            int total = 0;
            foreach (var part in _parts)
            {
                var partVerts = part.Model.Vertices[part.Model.IsScripted ? 1 : part.Frame + 1];
                var composed = ScriptRender.Compose(part.Matrix, view);
                var projected = part.Model.IsScripted
                    ? ScriptRender.Project(part.Model, partVerts, part.Frame, composed, _focal, _offsetX, _offsetY, 1)
                    : ModelRender.Project(Gte.Load(composed), partVerts, _focal, _offsetX, _offsetY, 1);
                var partWidths = part.Model.Widths.TryGetValue(1, out var pw) ? pw.ToArray() : Array.Empty<short>();
                var faces = ModelRender.Walk(part.Model.Polygons, projected, partWidths, part.Mask, 0);
                foreach (var face in faces)
                    foreach (var (x0, y0, x1, y1) in ModelRender.Edges(face))
                        Line(x0, y0, x1, y1, Palette[face.Group & 7]);
                total += faces.Count;
            }
            FaceCount = total;
            return;
        }

        var sv = _scripted
            ? ScriptRender.Project(_model, _verts, _boneFrame, view, _focal, _offsetX, _offsetY, 1)
            : ModelRender.Project(Gte.Load(view), _verts, _focal, _offsetX, _offsetY, 1);
        var wire = ModelRender.Walk(_model.Polygons, sv, _widths, 0xFF, 0);
        foreach (var face in wire)
            foreach (var (x0, y0, x1, y1) in ModelRender.Edges(face))
                Line(x0, y0, x1, y1, Palette[face.Group & 7]);
        FaceCount = wire.Count;
    }

    /// <summary>Copies the RGB frame into a top-down 32-bit BGRX buffer, upscaled by an integer factor.</summary>
    public void CopyToBgrx(byte[] destination, int scale)
    {
        int sw = Width * scale, sh = Height * scale;
        if (destination.Length < sw * sh * 4) throw new ArgumentException("destination too small", nameof(destination));
        for (int y = 0; y < sh; y++)
        {
            int sourceRow = (y / scale) * Width;
            for (int x = 0; x < sw; x++)
            {
                int s = (sourceRow + x / scale) * 3;
                int d = (y * sw + x) * 4;
                // X11 ZPixmap and Tizen NativeImageSource both expect little-endian BGRX.
                destination[d] = Rgb[s + 2];
                destination[d + 1] = Rgb[s + 1];
                destination[d + 2] = Rgb[s];
                destination[d + 3] = 0;
            }
        }
    }

    private (IReadOnlyList<Vertex> Verts, short[] Widths, bool Scripted) ModelState(Model model)
    {
        var verts = model.Vertices[1];
        var widths = model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();
        bool scripted = model.IsScripted && model.Hierarchy.Count > 0 && !_forceStatic;
        return (verts, widths, scripted);
    }

    private void FitModel()
    {
        (var cx, var cy, var cz, var radius) = _scripted
            ? BoundsOf(ScriptRender.PoseVertices(_model, _verts, _boneFrame, Gte.Mat.Identity(0, 0, 0)))
            : Bounds(_verts);
        SetFraming(cx, cy, cz, radius);
    }

    private void FitAssembly()
    {
        var points = new List<(double X, double Y, double Z)>();
        foreach (var part in _parts!)
        {
            if (!part.Model.Vertices.TryGetValue(part.Model.IsScripted ? 1 : part.Frame + 1, out var pv))
                throw new InvalidDataException($"Missing model {part.Model.Id} vertices");
            if (part.Model.IsScripted)
            {
                points.AddRange(ScriptRender.PoseVertices(part.Model, pv, part.Frame, part.Matrix));
                continue;
            }
            foreach (var v in pv)
                points.Add((
                    (part.Matrix.W[0] * (double)v.X + part.Matrix.W[3] * (double)v.Y + part.Matrix.W[6] * (double)v.Z) / 65536 + part.Matrix.Tx / 16.0,
                    (part.Matrix.W[1] * (double)v.X + part.Matrix.W[4] * (double)v.Y + part.Matrix.W[7] * (double)v.Z) / 65536 + part.Matrix.Ty / 16.0,
                    (part.Matrix.W[2] * (double)v.X + part.Matrix.W[5] * (double)v.Y + part.Matrix.W[8] * (double)v.Z) / 65536 + part.Matrix.Tz / 16.0));
        }
        (var cx, var cy, var cz, var radius) = BoundsOf(points.ToArray());
        SetFraming(cx, cy, cz, radius);
    }

    private void SetFraming(int cx, int cy, int cz, int radius)
    {
        _cx = cx;
        _cy = cy;
        _cz = cz;
        // The turn sweeps the XZ radius across the horizontal axis, so size the camera
        // distance on that. The floor matters: the retail clip byte rejects sz3 < 0x400.
        _dist = (int)Math.Max(MinDistance, Math.Round(radius * _focal / (FillFactor * (Width / 2.0))));
    }

    private static string Describe(Model model, IReadOnlyList<Vertex> verts, bool scripted)
    {
        string path = model.IsScripted
            ? $"scripted path (4DGI flags 0x{model.Flags:X8}, {model.Hierarchy.Count} bones, table {model.MeshTable.Length} s16)" +
              (model.Hierarchy.Count == 0 ? " - no 3DHY on disc, drawing the static list" : "")
            : $"static path (4DGI flags 0x{model.Flags:X8})";
        return $"model {model.Id}: {verts.Count} verts, {model.Polygons.Count} parts, " + path +
               (scripted ? "" : "");
    }

    private void Line(int x0, int y0, int x1, int y1, byte[] colour)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < Width && y0 >= 0 && y0 < Height)
            {
                int o = (y0 * Width + x0) * 3;
                Rgb[o] = colour[0];
                Rgb[o + 1] = colour[1];
                Rgb[o + 2] = colour[2];
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static (int Cx, int Cy, int Cz, int Radius) Bounds(IReadOnlyList<Vertex> verts)
    {
        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue,
            z0 = int.MaxValue, z1 = int.MinValue;
        foreach (var v in verts)
        {
            x0 = Math.Min(x0, v.X); x1 = Math.Max(x1, v.X);
            y0 = Math.Min(y0, v.Y); y1 = Math.Max(y1, v.Y);
            z0 = Math.Min(z0, v.Z); z1 = Math.Max(z1, v.Z);
        }
        return BoundsFrom(x0, x1, y0, y1, z0, z1);
    }

    private static (int Cx, int Cy, int Cz, int Radius) BoundsOf((double X, double Y, double Z)[] points)
    {
        return BoundsFrom(
            (int)Math.Floor(points.Min(p => p.X)), (int)Math.Ceiling(points.Max(p => p.X)),
            (int)Math.Floor(points.Min(p => p.Y)), (int)Math.Ceiling(points.Max(p => p.Y)),
            (int)Math.Floor(points.Min(p => p.Z)), (int)Math.Ceiling(points.Max(p => p.Z)));
    }

    private static (int Cx, int Cy, int Cz, int Radius) BoundsFrom(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, cz = (z0 + z1) / 2;
        double rx = (x1 - x0) / 2.0, ry = (y1 - y0) / 2.0, rz = (z1 - z0) / 2.0;
        int radius = (int)Math.Ceiling(Math.Max(Math.Sqrt(rx * rx + rz * rz), ry));
        return (cx, cy, cz, Math.Max(radius, 1));
    }
}
