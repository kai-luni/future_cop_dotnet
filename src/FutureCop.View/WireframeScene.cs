using FutureCop.Assets;

namespace FutureCop.View;

/// <summary>Which subject the viewer shows and how it is posed.</summary>
/// <param name="ModelId">Model id to start with in single-model mode.</param>
/// <param name="Assembly">Show the assembled player actor instead of one model.</param>
/// <param name="BoneFrame">Animation frame fed to the 3DMI sampling of the bone path.</param>
/// <param name="ForceStatic">Ignore the bone path and draw the rigid single list.</param>
/// <param name="Textured">Render affine-textured and flat-shaded polygons instead of wireframe.</param>
/// <param name="TransformSpeed">
/// Multiplier for the mech transformation cadence: 1 = retail (0xA0 progress per 1/60 s tick,
/// about 6.8 s for the whole transform), 4 = about 1.7 s.
/// </param>
public sealed record ViewerOptions(int ModelId = 30, bool Assembly = false, int BoneFrame = 0,
    bool ForceStatic = false, bool Textured = false, double TransformSpeed = 4.0);

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
    private readonly TextureBank? _textures;
    private readonly Dictionary<int, Dictionary<int, TextureRecord>> _texByModel = new();

    private int _modelIndex;
    private Model _model;
    private IReadOnlyList<Vertex> _verts;
    private short[] _widths;
    private bool _scripted;
    private int _cx, _cy, _cz, _dist;
    private int _actorIndex;
    private IReadOnlyList<PlayerPart>? _parts;
    private PlayerForm? _form;
    private readonly double _transformSpeed;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastAdvance;

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
        _transformSpeed = options.TransformSpeed > 0 ? options.TransformSpeed : 4.0;
        _forceStatic = options.ForceStatic;
        _boneFrame = Math.Max(0, options.BoneFrame);
        Rgb = new byte[width * height * 3];

        _file = new MisFile(misPath);
        if (options.Textured)
            _textures = TextureBank.FromMisFile(misPath);
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
            _form = new PlayerForm();
            RebuildAssembly();
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
        ? $"Future Cop wireframe - player mech {_form!.Name} (left = hover, right = walker)"
        : $"Future Cop wireframe - model {_model.Id}";

    /// <summary>Form of the assembled player mech, null outside the assembly mode.</summary>
    public PlayerForm? Form => _form;

    /// <summary>
    /// Rebuilds the five instances for the current form progress. The camera is only refitted
    /// when the subject changes, so an ongoing transition does not make the view jump.
    /// </summary>
    private void RebuildAssembly(bool refit = true)
    {
        _parts = _form!.Build(_actors[_actorIndex], _file.Models);
        if (refit) FitAssembly();
        Description = _form.IsWalker || _form.IsHover
            ? $"actor {_actors[_actorIndex].Id} [{_form.Name}]: models {string.Join(",", _parts.Select(p => p.Model.Id))}"
            : $"actor {_actors[_actorIndex].Id} [{_form.Name}]: masks " +
              $"{PlayerForm.Masks(_form.Progress).Main:X2}/{PlayerForm.Masks(_form.Progress).Companion:X2}";
    }

    /// <summary>
    /// Advances a running transformation by retail ticks (deterministic, one tick = step * 0xA0
    /// of progress). Used by captures and tests; the wall-clock path is <see cref="Advance"/>.
    /// </summary>
    public void AdvanceForm(int ticks)
    {
        if (_form is null || !_form.Advance(ticks)) return;
        RebuildAssembly();
    }

    /// <summary>
    /// Advances a running transformation by <paramref name="seconds"/> of wall clock. One retail
    /// tick is 1/60 s scaled by <see cref="ViewerOptions.TransformSpeed"/>.
    /// </summary>
    public void Advance(double seconds)
    {
        if (_form is null || !_form.AdvanceSeconds(seconds, _transformSpeed)) return;
        RebuildAssembly();
    }

    /// <summary>Switches to the previous (-1) or next (+1) model or player actor.</summary>
    public string Select(int step)
    {
        if (step == 0) return Description;
        if (_assemblyMode)
        {
            // Left steps to the floating (hover) mech, right back to the standing (walker) one;
            // func_80055988 turns the request into a transition on the following ticks.
            _form!.Request(step < 0 ? PlayerForm.Hover : PlayerForm.Walker);
            RebuildAssembly();
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
            _form = null;
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
        // A running transformation advances itself, so every frontend (desktop, TV) animates the
        // form change without extra input plumbing.
        var now = _clock.Elapsed;
        Advance((now - _lastAdvance).TotalSeconds);
        _lastAdvance = now;

        Array.Clear(Rgb);
        var view = ViewTransform.Turn(degrees, _cx, _cy, _cz, _dist);

        if (_textures is not null)
        {
            RenderTextured(view);
            return;
        }

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

    private void RenderTextured(Gte.Mat view)
    {
        var allFaces = new List<(TexturedFace Face, Model Model)>();
        if (_parts is not null)
        {
            foreach (var part in _parts)
            {
                var partVerts = part.Model.Vertices[part.Model.IsScripted ? 1 : part.Frame + 1];
                var composed = ScriptRender.Compose(part.Matrix, view);
                var projected = part.Model.IsScripted
                    ? ScriptRender.Project(part.Model, partVerts, part.Frame, composed, _focal, _offsetX, _offsetY, 1)
                    : ModelRender.Project(Gte.Load(composed), partVerts, _focal, _offsetX, _offsetY, 1);
                var partWidths = part.Model.Widths.TryGetValue(1, out var pw) ? pw.ToArray() : Array.Empty<short>();
                foreach (var face in ModelRender.WalkTextured(part.Model.Polygons, projected, partWidths, part.Mask, 0))
                    allFaces.Add((face, part.Model));
            }
        }
        else
        {
            var sv = _scripted
                ? ScriptRender.Project(_model, _verts, _boneFrame, view, _focal, _offsetX, _offsetY, 1)
                : ModelRender.Project(Gte.Load(view), _verts, _focal, _offsetX, _offsetY, 1);
            foreach (var face in ModelRender.WalkTextured(_model.Polygons, sv, _widths, 0xFF, 0))
                allFaces.Add((face, _model));
        }

        // Painter's order (largest Z first), matching the PS1 ordering table.
        allFaces.Sort((a, b) => b.Face.Work.Z.CompareTo(a.Face.Work.Z));
        foreach (var (face, model) in allFaces)
            DrawTexturedFace(face, model);
        FaceCount = allFaces.Count;
    }

    private Dictionary<int, TextureRecord> TextureLookup(Model model)
    {
        if (!_texByModel.TryGetValue(model.Id, out var map))
        {
            map = new Dictionary<int, TextureRecord>(model.Textures.Count);
            foreach (var t in model.Textures)
                map[t.Offset] = t;
            _texByModel[model.Id] = map;
        }
        return map;
    }

    private void DrawTexturedFace(TexturedFace face, Model model)
    {
        var map = TextureLookup(model);
        bool hasRec = map.TryGetValue(face.TextureOffset, out var rec);

        int x0 = (short)face.Work.Xy0, y0 = (short)(face.Work.Xy0 >> 16);
        int x1 = (short)face.Work.Xy1, y1 = (short)(face.Work.Xy1 >> 16);
        int x2 = (short)face.Work.Xy2, y2 = (short)(face.Work.Xy2 >> 16);
        int x3 = (short)face.Work.Xy3, y3 = (short)(face.Work.Xy3 >> 16);

        // UVs are packed in polygon vertex order (v0, v1, v2, v3); for type 4 quads Work stores (v0, v1, v3, v2).
        int u0 = (byte)(rec.Uv0 & 0xFF), v0 = (byte)((rec.Uv0 >> 8) & 0xFF);
        int u1 = (byte)((rec.Uv0 >> 16) & 0xFF), v1 = (byte)((rec.Uv0 >> 24) & 0xFF);
        int u2 = (byte)(rec.Uv1 & 0xFF), v2 = (byte)((rec.Uv1 >> 8) & 0xFF);
        int u3 = (byte)((rec.Uv1 >> 16) & 0xFF), v3 = (byte)((rec.Uv1 >> 24) & 0xFF);

        if (face.Type == 3)
        {
            RasterizeTriangle(x0, y0, u0, v0, x1, y1, u1, v1, x2, y2, u2, v2, hasRec, rec);
        }
        else if (face.Type == 4)
        {
            // Work holds (v0, v1, v3, v2) in (Xy0, Xy1, Xy2, Xy3).
            RasterizeTriangle(x0, y0, u0, v0, x1, y1, u1, v1, x2, y2, u3, v3, hasRec, rec);
            RasterizeTriangle(x1, y1, u1, v1, x3, y3, u2, v2, x2, y2, u3, v3, hasRec, rec);
        }
        else
        {
            // Billboards (type 5/6) store (top-left, top-right, bottom-left, bottom-right).
            RasterizeTriangle(x0, y0, u0, v0, x1, y1, u1, v1, x2, y2, u2, v2, hasRec, rec);
            RasterizeTriangle(x1, y1, u1, v1, x3, y3, u3, v3, x2, y2, u2, v2, hasRec, rec);
        }
    }

    private void RasterizeTriangle(
        int x0, int y0, int u0, int v0,
        int x1, int y1, int u1, int v1,
        int x2, int y2, int u2, int v2,
        bool hasRec, TextureRecord rec)
    {
        int area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        if (area == 0) return;

        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
        int maxX = Math.Min(Width - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
        int maxY = Math.Min(Height - 1, Math.Max(y0, Math.Max(y1, y2)));
        if (minX > maxX || minY > maxY) return;

        bool flat = hasRec && rec.TypeLoaded is 0 or 1;
        bool missing = !hasRec || (!flat && !_textures!.IsPageLoaded(rec.Tpage));
        byte flatR = missing ? (byte)255 : (byte)(rec.Rgb >> 16);
        byte flatG = missing ? (byte)0 : (byte)(rec.Rgb >> 8);
        byte flatB = missing ? (byte)255 : (byte)rec.Rgb;

        double invArea = 1.0 / area;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int w0 = (x1 - x) * (y2 - y) - (y1 - y) * (x2 - x);
                int w1 = (x2 - x) * (y0 - y) - (y2 - y) * (x0 - x);
                int w2 = (x0 - x) * (y1 - y) - (y0 - y) * (x1 - x);
                if (area > 0 ? (w0 < 0 || w1 < 0 || w2 < 0) : (w0 > 0 || w1 > 0 || w2 > 0))
                    continue;

                if (flat || missing)
                {
                    int o = (y * Width + x) * 3;
                    Rgb[o] = flatR;
                    Rgb[o + 1] = flatG;
                    Rgb[o + 2] = flatB;
                    continue;
                }

                double b0 = w0 * invArea;
                double b1 = w1 * invArea;
                double b2 = w2 * invArea;
                int u = (int)Math.Round(b0 * u0 + b1 * u1 + b2 * u2);
                int v = (int)Math.Round(b0 * v0 + b1 * v1 + b2 * v2);
                var (r, g, b, transparent) = _textures!.Vram.SampleRgb(rec.Tpage, rec.Clut, u, v);
                if (transparent) continue;

                int dst = (y * Width + x) * 3;
                Rgb[dst] = r;
                Rgb[dst + 1] = g;
                Rgb[dst + 2] = b;
            }
        }
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
        // Two constraints decide the camera distance:
        //  - the turn sweeps the XZ radius across the horizontal axis, so size it on that;
        //  - the retail clip byte sets bit 0 for sz3 < 0x400 and func_80015B68 then drops the
        //    whole face. radius bounds the depth extent too, so the nearest vertex needs that
        //    much room behind the near guard, otherwise faces of the near side vanish (holes
        //    that move with the turn).
        int sizeDriven = (int)Math.Round(radius * _focal / (FillFactor * (Width / 2.0)));
        int depthDriven = 0x400 + radius + 0x20;
        _dist = Math.Max(Math.Max(MinDistance, depthDriven), sizeDriven);
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
