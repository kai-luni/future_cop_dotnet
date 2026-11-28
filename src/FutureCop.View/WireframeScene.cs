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
/// <param name="ShotSlowMotion">
/// Slow motion of the projectile flight. The mover is retail-exact in <see cref="Shot.Tick"/>, and at
/// retail speed a 4Da shot (speed word 0x2000 &lt;&lt; 4) covers its whole leash within a single frame,
/// so the viewer divides the step: 300 stretches that flight to about a second, 1 is retail. The
/// ratio between fast and slow weapons stays the retail one.
/// </param>
/// <param name="ShotAimDegrees">
/// Divergence of a fired shot from straight ahead, in degrees: 0 fires along the mech's facing, 90
/// would fire sideways. The right arm fires at <c>180 - aim</c>, the left arm at <c>180 + aim</c> in
/// model space, so both muzzles shoot away from the camera and slightly outward. Retail aims at a
/// target entity (one vector shared by both muzzles); the viewer has none.
/// </param>
/// <param name="ShotScale">
/// Draw scale of the projectile model. The retail models of this mission are 32 units across, i.e.
/// a few pixels at the viewer's framing; retail world space is far larger than this one mech, so
/// the viewer enlarges the shot to stay readable.
/// </param>
public sealed record ViewerOptions(int ModelId = 30, bool Assembly = false, int BoneFrame = 0,
    bool ForceStatic = false, bool Textured = false, double TransformSpeed = 4.0,
    double ShotSlowMotion = 300.0, double ShotAimDegrees = 15.0, double ShotScale = 1.5,
    double CameraElevation = 45.0, double CameraDistanceMetres = 45.0,
    bool AutoFire = true, double FireRateScale = 0.1);

/// <summary>
/// Platform-independent wireframe renderer: turns one .MIS model (or the assembled player
/// actor) into an RGB framebuffer. Frontends only present the buffer and translate input,
/// so desktop Linux and the Tizen TV build share the exact same projection and walk.
/// </summary>
public sealed class WireframeScene
{
    private const double FillFactor = 0.70;
    private const int MinDistance = 0x480;

    /// <summary>
    /// Viewer scale between metres and model units. Nothing in the retail data states it: the
    /// assembled mech spans about 800 model units, so at 90 units per metre it is a nine metre
    /// walker and the automatic framing sits roughly twenty metres away. Only used to translate
    /// <see cref="ViewerOptions.CameraDistanceMetres"/> into the projection's distance parameter.
    /// </summary>
    public const double ModelUnitsPerMetre = 90.0;

    /// <summary>
    /// Furthest camera distance the projection survives. The GTE emulation keeps the rotated vertex
    /// in a 16-bit register (Gte.Rtps clamps to 0x7FFF and rejects faces that overflow), so a camera
    /// further out than this drops every face of the model.
    /// </summary>
    public const int MaxDistance = 0x7800;

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
    private readonly bool _autoFire;
    private readonly double _fireRateScale;
    private readonly CadenceState[] _cadence = new CadenceState[3];
    private double _pendingFireFrames;
    private TimeSpan _lastRender;
    private readonly double _cameraElevation;
    private readonly double _cameraDistanceMetres;
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

    private readonly List<Shot> _shots = new();
    private readonly List<ShotDefinition> _weapons = new();
    private int _weaponIndex;
    private int _muzzleSide;
    private double _pendingShotTicks;
    private readonly double _shotSpeed;
    private readonly double _shotAim;
    private readonly double _shotScale;
    private TimeSpan _lastShotAdvance;

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
        // Retail crosses the leash of a 4Da shot within one frame; the viewer stretches that
        // flight by ShotSlowMotion so the rocket stays readable (300 -> about one second).
        _shotSpeed = 1.0 / Math.Max(1.0, options.ShotSlowMotion);
        _shotAim = options.ShotAimDegrees;
        _shotScale = options.ShotScale > 0 ? options.ShotScale : 1.0;
        _autoFire = options.AutoFire;
        _fireRateScale = options.FireRateScale > 0 ? options.FireRateScale : 1.0;
        _cameraElevation = options.CameraElevation;

        _cameraDistanceMetres = options.CameraDistanceMetres > 0 ? options.CameraDistanceMetres : 0;
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

        // The mission's own fire definitions: the tACT records the retail registry D_800A3340
        // caches, restricted to halves that name a spawner action and a model the mission carries.
        _weapons.AddRange(WeaponCatalog.Load(_file));

        if (_assemblyMode)
        {
            if (_actors.Count == 0) throw new InvalidDataException("No player actors found in mission");
            _model = _models.FirstOrDefault() ?? throw new InvalidDataException("No drawable models in mission");
            _verts = Array.Empty<Vertex>();
            _widths = Array.Empty<short>();
            _form = new PlayerForm();
            RebuildAssembly(refit: true);
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
        ? $"Future Cop wireframe - player mech {_form!.Name}, {_shots.Count} shots in flight " +
          $"(left = hover, right = walker, up/down = {WeaponLine()})"
        : $"Future Cop wireframe - model {_model.Id}";

    /// <summary>Form of the assembled player mech, null outside the assembly mode.</summary>
    public PlayerForm? Form => _form;

    /// <summary>Camera framing (pivot and distance) - lets a test prove it stays put.</summary>
    public (int Cx, int Cy, int Cz, int Dist) Framing => (_cx, _cy, _cz, _dist);

    /// <summary>Fire definitions the mission carries; cycled with up/down (mouse wheel).</summary>
    public IReadOnlyList<ShotDefinition> Weapons => _weapons;

    /// <summary>Currently selected fire definition, null when the mission has none.</summary>
    public ShotDefinition? Weapon
    {
        get
        {
            if (_weapons.Count == 0) return null;
            int index = _weaponIndex % _weapons.Count;
            if (index < 0) index += _weapons.Count;
            return _weapons[index];
        }
    }

    /// <summary>Live projectiles; they fly on their own clock and vanish at the retail range.</summary>
    public IReadOnlyList<Shot> Shots => _shots;

    public int ShotCount => _shots.Count;

    /// <summary>Faces the projectiles contributed to the last <see cref="Render"/> call.</summary>
    public int ShotFaceCount { get; private set; }

    /// <summary>Status line of the selected weapon.</summary>
    public string WeaponLine() => _weapons.Count == 0
        ? "no fire definitions in this mission"
        : $"weapon {_weaponIndex + 1}/{_weapons.Count}: {Weapon!.Name}";

    /// <summary>Cycles the fire definition (up/down, mouse wheel); returns the status line.</summary>
    public string CycleWeapon(int step)
    {
        if (_weapons.Count == 0) return WeaponLine();
        if (step != 0)
        {
            int count = _weapons.Count;
            _weaponIndex = ((_weaponIndex + step) % count + count) % count;
            Description = WeaponLine();
        }
        return WeaponLine();
    }

    /// <summary>
    /// Fires the selected definition from one of the two alternating muzzles and returns a status
    /// line. Mirrors func_8006ACB0: the definition's model id becomes the projectile model and the
    /// entity's spawner turns the speed word into the per-tick step.
    /// </summary>
    public string Fire()
    {
        if (!_assemblyMode || _parts is null) return "firing needs the player assembly";
        var weapon = Weapon;
        if (weapon is null) return WeaponLine();
        var model = _file.Models.FirstOrDefault(m => m.Id == weapon.ModelId);
        if (model is null || !model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0)
        {
            Description = $"projectile model {weapon.ModelId} is not drawable";
            return Description;
        }

        int muzzleSide = _muzzleSide;
        var (px, py, pz) = Muzzle(muzzleSide);
        _muzzleSide ^= 1;
        // PlayerAssembly.Point returns engine translation units, i.e. 16x model units like Gte.Mat.Tx
        // (func_80017668 shifts the GTE result left by four). The flight state, the step and the leash
        // radius all live in plain model units, so the muzzle is scaled down here and ShotMatrix turns
        // the position back into translation units - mixing the two made the shot crawl out of the arm
        // and expire after a fraction of its leash.
        var (mx, my, mz) = (px >> 4, py >> 4, pz >> 4);
        // Viewer convention, not retail: the two arms splay by the aim knob in opposite directions.
        // func_8006ACB0:166-171 builds ONE aim vector (posB - posA) from the target and shares it
        // between both muzzles. 180 degrees is straight away from the camera: the mech faces the
        // camera, so 0 degrees (model -Z) would fly at the viewer and leave the frame within a few
        // ticks. Measured through ViewTransform: the projected depth grows with 180 and shrinks with 0.
        double aim = 180.0 + (muzzleSide == 0 ? -_shotAim : _shotAim);
        // func_8005E47C turns the speed word into a 16.16 scale (speed << 4, or << 1 with the alt
        // selector); positions are model units, so one tick covers scale / 4096 units.
        var shot = new Shot(weapon, model, mx, my, mz, _shotSpeed, aim);
        _shots.Add(shot);

        Description = $"{WeaponLine()}, fired model {model.Id} ({model.Polygons.Count} parts) " +
                      $"from {mx},{my},{mz} (step {shot.StepUnits}, range {shot.Range})";
        return Description;
    }

    /// <summary>
    /// Muzzle point of one of the two alternating sides. Retail keeps two 76-byte model instances at
    /// state + 0x178 and reads vertex 0 of each through func_80017668; the viewer reads the same
    /// point 0 of the main body and of the twin arm instance of the assembly pose.
    /// </summary>
    private (int X, int Y, int Z) Muzzle(int side)
    {
        // Slots 2 and 3 of the assembly are the two arm instances (joints.fifth and joints.third);
        // retail keeps the same pair of model instances at state + 0x178 and alternates them.
        var part = _parts!.Count >= 4 ? _parts![side == 0 ? 2 : 3] : _parts![0];
        return PlayerAssembly.Point(part.Model, part.Frame, 0, part.Matrix);
    }

    /// <summary>Deterministic shot clock: one tick = 1/60 s of the retail cadence.</summary>
    public void AdvanceShotTicks(int ticks)
    {
        for (int t = 0; t < ticks; t++)
            foreach (var shot in _shots) shot.Tick();
        _shots.RemoveAll(s => s.Expired);
    }

    /// <summary>
    /// Drives the retail trigger of the selected weapon: one 60 Hz frame subtracts
    /// <see cref="RetailClock.TicksPerFrame"/> from the reload timer (func_8006C3E0) and then asks the
    /// slot update whether the weapon fires (func_8006BA50). <see cref="ViewerOptions.FireRateScale"/>
    /// stretches the wall clock, so 1.0 is the retail cadence and smaller values slow the whole
    /// trigger down without changing the relative rhythm of the weapons.
    /// </summary>
    public string? AdvanceAutoFire(double seconds)
    {
        if (!_autoFire || !_assemblyMode || _weapons.Count == 0)
        {
            _pendingFireFrames = 0;
            return null;
        }
        if (seconds > 0) _pendingFireFrames += seconds * 60.0 * _fireRateScale;
        int frames = (int)_pendingFireFrames;
        if (frames <= 0) return null;
        _pendingFireFrames -= frames;
        string? description = null;
        for (int i = 0; i < frames; i++)
        {
            ref var state = ref _cadence[Math.Min(_weaponIndex, _cadence.Length - 1)];
            var cadence = Weapon.Cadence;
            // The cycle counter starts at the entry's burst byte (func_8006BA50:211 copies it back on
            // every reload); a fresh slot has to be seeded before the first decrement.
            if (state.Cycle <= 0) state.Cycle = cadence.Burst;
            WeaponTrigger.DecrementTimer(ref state, RetailClock.TicksPerFrame);
            if (WeaponTrigger.TryFire(ref state, cadence))
                description = Fire();
        }
        return description;
    }

    private void AdvanceShots(TimeSpan now)
    {
        double seconds = (now - _lastShotAdvance).TotalSeconds;
        _lastShotAdvance = now;
        if (_shots.Count == 0 || seconds <= 0) return;
        _pendingShotTicks += seconds * PlayerForm.TicksPerSecond;
        int ticks = (int)_pendingShotTicks;
        if (ticks <= 0) return;
        _pendingShotTicks -= ticks;
        AdvanceShotTicks(ticks);
    }

    /// <summary>
    /// Rebuilds the five instances for the current form progress. The camera is only refitted
    /// when the subject changes, so an ongoing transition does not make the view jump.
    /// </summary>
    private void RebuildAssembly(bool refit = false)
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
        AdvanceShots(now);
        AdvanceAutoFire((now - _lastRender).TotalSeconds);
        _lastRender = now;

        Array.Clear(Rgb);
        var view = ViewTransform.Turn(degrees, _cameraElevation, _cx, _cy, _cz, _dist);
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
            FaceCount += DrawShotsWire(view);
            return;
        }

        var sv = _scripted
            ? ScriptRender.Project(_model, _verts, _boneFrame, view, _focal, _offsetX, _offsetY, 1)
            : ModelRender.Project(Gte.Load(view), _verts, _focal, _offsetX, _offsetY, 1);
        var wire = ModelRender.Walk(_model.Polygons, sv, _widths, 0xFF, 0);
        foreach (var face in wire)
            foreach (var (x0, y0, x1, y1) in ModelRender.Edges(face))
                Line(x0, y0, x1, y1, Palette[face.Group & 7]);
        FaceCount = wire.Count + DrawShotsWire(view);
    }

    /// <summary>
    /// Draws every live projectile as wireframe edges in the projectile colour. The shot matrix is a
    /// translation of the projectile position in model space composed with the view transform, i.e.
    /// the same pipeline the mech parts use.
    /// </summary>
    private int DrawShotsWire(Gte.Mat view)
    {
        int faces = 0;
        foreach (var shot in _shots)
        {
            if (!shot.Model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
            var matrix = ShotMatrix(shot, view);
            var projected = shot.Model.IsScripted
                ? ScriptRender.Project(shot.Model, verts, 0, matrix, _focal, _offsetX, _offsetY, 1)
                : ModelRender.Project(Gte.Load(matrix), verts, _focal, _offsetX, _offsetY, 1);
            var widths = shot.Model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();
            var walk = ModelRender.Walk(shot.Model.Polygons, projected, widths, 0xFF, 0);
            faces += walk.Count;
            foreach (var face in walk)
                foreach (var (x0, y0, x1, y1) in ModelRender.Edges(face))
                    Line(x0, y0, x1, y1, Palette[2]);
        }
        ShotFaceCount = faces;
        return faces;
    }

    /// <summary>
    /// View matrix of a projectile: the retail basis of func_800193F8 built from the flight vector,
    /// scaled by the viewer knob and placed at the shot position. Retail rebuilds the same basis
    /// every tick (func_80035B7C:554-568), so the model nose (model +Z) always points along the
    /// velocity; without it the rocket keeps pointing at model +Z and flies sideways.
    /// </summary>
    private Gte.Mat ShotMatrix(Shot shot, Gte.Mat view)
    {
        int scale = (int)Math.Round(_shotScale * 0x10000);
        var basis = ShotMotion.FlightBasis(shot.Direction);
        static int Apply(int value, int factor) => (int)(((long)value * factor) >> 16);
        var local = new Gte.Mat(new[]
        {
            Apply(basis[0], scale), Apply(basis[1], scale), Apply(basis[2], scale),
            Apply(basis[3], scale), Apply(basis[4], scale), Apply(basis[5], scale),
            Apply(basis[6], scale), Apply(basis[7], scale), Apply(basis[8], scale),
        }, shot.X << 4, shot.Y << 4, shot.Z << 4);
        return ScriptRender.Compose(local, view);
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

        CollectShots(view, allFaces);

        // Painter's order (largest Z first), matching the PS1 ordering table.
        allFaces.Sort((a, b) => b.Face.Work.Z.CompareTo(a.Face.Work.Z));
        foreach (var (face, model) in allFaces)
            DrawTexturedFace(face, model);
        FaceCount = allFaces.Count;
    }

    /// <summary>Adds every live projectile to the textured face list (same walk, mask 0xFF).</summary>
    private void CollectShots(Gte.Mat view, List<(TexturedFace Face, Model Model)> faces)
    {
        int before = faces.Count;
        foreach (var shot in _shots)
        {
            if (!shot.Model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
            var matrix = ShotMatrix(shot, view);
            var projected = shot.Model.IsScripted
                ? ScriptRender.Project(shot.Model, verts, 0, matrix, _focal, _offsetX, _offsetY, 1)
                : ModelRender.Project(Gte.Load(matrix), verts, _focal, _offsetX, _offsetY, 1);
            var widths = shot.Model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();
            foreach (var face in ModelRender.WalkTextured(shot.Model.Polygons, projected, widths, 0xFF, 0))
                faces.Add((face, shot.Model));
        }
        ShotFaceCount = faces.Count - before;
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
            RasterizeTriangle(x0, y0, u0, v0, x1, y1, u1, v1, x2, y2, u2, v2, hasRec, rec, face.Semi);
        }
        else if (face.Type == 4)
        {
            // Work holds (v0, v1, v3, v2) in (Xy0, Xy1, Xy2, Xy3).
            RasterizeTriangle(x0, y0, u0, v0, x1, y1, u1, v1, x2, y2, u3, v3, hasRec, rec, face.Semi);
            RasterizeTriangle(x1, y1, u1, v1, x3, y3, u2, v2, x2, y2, u3, v3, hasRec, rec, face.Semi);
        }
        else
        {
            // Billboards (type 5/6) store (top-left, top-right, bottom-left, bottom-right).
            RasterizeTriangle(x0, y0, u0, v0, x1, y1, u1, v1, x2, y2, u2, v2, hasRec, rec, face.Semi);
            RasterizeTriangle(x1, y1, u1, v1, x3, y3, u3, v3, x2, y2, u2, v2, hasRec, rec, face.Semi);
        }
    }

    private void RasterizeTriangle(
        int x0, int y0, int u0, int v0,
        int x1, int y1, int u1, int v1,
        int x2, int y2, int u2, int v2,
        bool hasRec, TextureRecord rec, bool semi = false)
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
                ushort raw = _textures!.Vram.SampleRaw(rec.Tpage, rec.Clut, u, v);
                var (r, g, b, transparent) = Vram.Abgr1555ToRgb(raw);
                if (transparent) continue;

                int dst = (y * Width + x) * 3;
                // Semi-transparent face: PS1 blends the texel when its STP bit (15) is set, using the
                // ABR mode of the texture page (tpage bits 5..6). See LevelScene.Tri for the same table.
                if (semi && (raw & 0x8000) != 0)
                {
                    int br = Rgb[dst], bg = Rgb[dst + 1], bb = Rgb[dst + 2];
                    switch ((rec.Tpage >> 5) & 3)
                    {
                        case 0:
                            r = (byte)((br + r) >> 1); g = (byte)((bg + g) >> 1); b = (byte)((bb + b) >> 1); break;
                        case 1:
                            r = (byte)Math.Min(255, br + r); g = (byte)Math.Min(255, bg + g); b = (byte)Math.Min(255, bb + b); break;
                        case 2:
                            r = (byte)Math.Max(0, br - r); g = (byte)Math.Max(0, bg - g); b = (byte)Math.Max(0, bb - b); break;
                        default:
                            r = (byte)Math.Min(255, br + (r >> 2)); g = (byte)Math.Min(255, bg + (g >> 2)); b = (byte)Math.Min(255, bb + (b >> 2)); break;
                    }
                }
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

    /// <summary>
    /// Frames the camera on the union of the poses a form change passes through. Fitting only the
    /// current pose would move the camera on every tick, and fitting only the walker would push the
    /// lifted hover body out of the frame.
    /// </summary>
    private void FitAssembly()
    {
        var points = new List<(double X, double Y, double Z)>();
        if (_form is not null)
        {
            foreach (int progress in new[] { 0, 0x4000, 0x8000, 0xC000, PlayerForm.FullProgress })
                CollectAssemblyPoints(PlayerForm.Build(_actors[_actorIndex], _file.Models, progress), points);
        }
        else
        {
            CollectAssemblyPoints(_parts!, points);
        }
        (var cx, var cy, var cz, var radius) = BoundsOf(points.ToArray());
        SetFraming(cx, cy, cz, radius);
    }

    private static void CollectAssemblyPoints(IReadOnlyList<PlayerPart> parts,
        List<(double X, double Y, double Z)> points)
    {
        foreach (var part in parts)
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
        int auto = Math.Max(Math.Max(MinDistance, depthDriven), sizeDriven);
        // An explicit distance in metres wins over the auto framing, but never moves the camera
        // inside the near guard.
        int wanted = _cameraDistanceMetres > 0
            ? (int)Math.Round(_cameraDistanceMetres * ModelUnitsPerMetre)
            : 0;
        _dist = Math.Clamp(wanted > 0 ? wanted : auto, depthDriven, Math.Max(depthDriven, MaxDistance));
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
        if (points.Length == 0) return (0, 0, 0, 1);   // an actor without vertices has no extent
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
