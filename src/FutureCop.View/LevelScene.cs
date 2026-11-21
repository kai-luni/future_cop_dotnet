using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>Camera of the level start view: orbit around the mech pivot.</summary>
/// <param name="Yaw">Degrees about the world y axis.</param>
/// <param name="Elevation">Degrees above the horizon.</param>
/// <param name="Distance">Eye distance from the pivot in model units (world units / 16).</param>
public sealed record LevelCamera(double Yaw = 30.0, double Elevation = 35.0, int Distance = 0x1C00);

/// <summary>
/// First runnable start of a Precinct Assault map: the retail terrain of the mission (GRDB grid +
/// Ctil sections, drawn through the ported layer-1 path: cell window func_80064460, grid projection
/// func_800646BC, cell walk func_80065134 and packet emit func_800648E8) with the player walker
/// (PlayerForm) at the clip-rect centre in the static viewer, or at the mission spawn for WALK.
/// The software rasterizer only turns the emitted packets into pixels; it is a viewer, not a GPU port.
/// </summary>
public sealed class LevelScene
{
    public const int Width = 320;
    public const int Height = 240;
    /// <summary>GTE H. func_80063FF8 encodes the 1-player view cone |x| &lt;= 0.3125 z, |y| &lt;= 0.234 z,
    /// i.e. 160/H and 120/H with H = 0x200 on the 320 x 240 screen.</summary>
    public const int Focal = 0x200;
    private const int Ofx = 160 << 16, Ofy = 120 << 16;

    private readonly MisFile _file;
    private readonly TextureBank _textures;
    private IReadOnlyList<PlayerPart> _parts;
    private readonly Dictionary<int, Dictionary<int, TextureRecord>> _texByModel = new();
    private readonly uint[] _texpages = TerrainRender.DefaultTexpages();
    private readonly int _mechCx, _mechCy, _mechCz;

    public LevelMap Map { get; }
    public string MissionPath { get; }
    /// <summary>World position in world units, y up.</summary>
    public (int X, int Y, int Z) MechWorld { get; private set; }
    private MechBody? _walker;
    private FutureCop.Game.Mech.MechWorld? _walkerWorld;
    private bool _animateWalker;

    /// <summary>Use the mission's player record and body model for the interactive WALK viewer.</summary>
    public void EnableMechMovement()
    {
        AttachWalker(MechSpawn.CreateWalker(Actor, _parts[0].Model, Map));
        _animateWalker = true;
        UpdateWalkerPose();
        var (x, y, z) = MechWorld;
        LevelGround.Height(Map, x, y, z, out int attribute);
        GroundAttribute = attribute;
    }

    private LevelWeapons? _weapons;
    private PickupItems? _pickups;

    /// <summary>
    /// The weapon pickups lying on this mission's map, or null until firing is enabled. The field is
    /// built from the type 0x10 records of the archive (func_8002FA20), so the items sit where the
    /// retail level puts them.
    /// </summary>
    public PickupItems? Pickups => _pickups;

    /// <summary>
    /// The fire path of the running level, or null when it was not enabled. Created lazily from the
    /// mission's definitions so a static preview does not pay for the scan.
    /// </summary>
    public LevelWeapons? Weapons
    {
        get
        {
            if (_weapons is null && _fireEnabled) _weapons = new LevelWeapons(_file);
            return _weapons;
        }
    }

    private bool _fireEnabled;

    /// <summary>Enables firing for the interactive viewer; the scene then advances live projectiles.</summary>
    public void EnableFiring()
    {
        _fireEnabled = true;
        _ = Weapons;
        // func_8002FC90:188-199 refills the control block the fire path spends from, and the gauge
        // bits at +0x1C/+0x1E raise the same health word the walker carries.
        _pickups = new PickupItems(_file, Map,
            PlayerResupplyState.ForWalker(Weapons!.Slots, () => Walker!.Health,
                v => Walker!.Health = (short)v, 0x7F00));
    }

    /// <summary>
    /// Opt in to a caller-initialized one-player walker. Initialization and clip records must be
    /// supplied from a verified game state; this viewer does not invent a retail spawn state.
    /// Platforms, dynamic objects, 2P, script/sound side effects and rendered pose are not integrated.
    /// </summary>
    public void AttachWalker(MechBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Port != 0) throw new NotSupportedException("Only player port 0 is integrated");
        _animateWalker = false;
        _walker = body;
        _walkerWorld = new FutureCop.Game.Mech.MechWorld(Map);
        MechWorld = (body.X, body.Y, body.Z);
    }

    /// <summary>The attached walker body, so callers can read its health for the HUD.</summary>
    public MechBody? Walker => _walker;

    /// <summary>Advance the persistent walker by caller-supplied retail steps and input flags.</summary>
    public int AdvanceWalker(int steps, uint flags, AnalogPad pad)
    {
        if (_walker is null || _walkerWorld is null) throw new InvalidOperationException("Attach a walker first");
        _walkerWorld.Pads[0] = pad;
        int result = MechWalker.Tick(_walker, _walkerWorld, steps, flags);
        WalkerSteps += steps;
        MechWorld = (_walker.X, _walker.Y, _walker.Z);
        // The trigger and the projectile clock run on retail frames, not on locomotion substeps: the
        // wireframe viewer ticks once per 1/60 s frame and subtracts RetailClock.TicksPerFrame from
        // the reload timer (func_8006C3E0). Passing substeps here made both run eight times too fast.
        int ticks = steps * RetailClock.LocomotionTicks;
        int frames = ticks / RetailClock.TicksPerFrame;
        _weapons?.AdvanceShots(frames);
        // Beams live on the 60 Hz frame, like the trigger that spawns them.
        for (int f = 0; f < frames; f++) _weapons?.AdvanceBeams();
        // func_8002FF6C runs per frame against the player's position.
        _pickups?.Update(_walker.X, _walker.Y, _walker.Z);
        // func_800588D4 frame order: :287 gSlewEntityAim composes the arm instances and decays the recoil
        // timers (func_80057E40:201-215), :290 gWeaponSlotInput fires from those instances. The pose is
        // therefore built BEFORE the trigger, and the muzzle of this frame's shot sits on the displaced
        // arm - a kick stamped by the shot itself first moves the arm on the next frame.
        _weapons?.AdvanceRecoil(RetailClock.TicksPerFrame * frames);
        UpdateWalkerPose();
        _weapons?.Advance(_walker, _parts, _padWord, RetailClock.TicksPerFrame * frames);
        return result;
    }

    /// <summary>
    /// Pad buttons held this frame, for the HUD and for callers that only need the raw set.
    /// </summary>
    public PadButtons HeldButtons { get; set; }

    private uint _padWord;

    /// <summary>
    /// The translated pad word gWeaponSlotInput reads (func_800576A8, stored at state + 0x30). The
    /// caller sets this before <see cref="AdvanceWalker"/>: it is the same word
    /// <see cref="MechInput.TranslatePadButtons"/> produced for the walker, not the raw button set.
    /// </summary>
    public uint HeldPadWord
    {
        get => _padWord;
        set => _padWord = value;
    }

    private void UpdateWalkerPose()
    {
        if (_animateWalker && _walker is { } walker && _parts[0].Frame != walker.Track.Frame)
            RebuildParts(walker.Track.Frame);
        ApplyRecoil();
    }

    /// <summary>The matrices <see cref="PlayerAssembly"/> composed, before any recoil of this frame.</summary>
    private Gte.Mat[] _poseMatrices = Array.Empty<Gte.Mat>();

    /// <summary>Rebuilds the five instances and caches their unrecoiled matrices.</summary>
    private void RebuildParts(int mainFrame)
    {
        _parts = PlayerAssembly.Build(Actor, _file.Models, mainFrame: mainFrame);
        _poseMatrices = _parts.Select(p => p.Matrix).ToArray();
    }

    /// <summary>
    /// func_80057E40:202-215: the two arm instances (<c>parts[2]</c> = state + 0x178,
    /// <c>parts[3]</c> = state + 0x1C4) are displaced along their own Z axis by the recoil timers of
    /// state + 0x174 / + 0x176. The displacement is applied to the cached joint matrices, never to the
    /// already displaced ones, because the C recomposes the instance from the joint chain
    /// (func_80057E40:201) before every application.
    /// </summary>
    private void ApplyRecoil()
    {
        if (_weapons is not { } weapons) return;
        var parts = _parts.ToArray();
        for (int side = 0; side < WeaponRecoil.Count; side++)
        {
            int index = 2 + side;
            if (index >= parts.Length || index >= _poseMatrices.Length) break;
            parts[index] = parts[index] with { Matrix = weapons.Recoil.Apply(_poseMatrices[index], side) };
        }
        _parts = parts;
    }

    /// <summary>
    /// Displacement the render actually applies to the two arms this frame, in world position units:
    /// the drawn arm matrix minus the unrecoiled one that the joint chain composed. This is the barrel
    /// travel of func_80057E40 - the only visible body reaction retail has to a shot.
    /// </summary>
    public ((int X, int Y, int Z) Right, (int X, int Y, int Z) Left) ArmRecoil
    {
        get
        {
            if (_weapons is null || _poseMatrices.Length < 4) return ((0, 0, 0), (0, 0, 0));
            Gte.Mat Delta(int index)
            {
                var drawn = _parts[index].Matrix;
                var rest = _poseMatrices[index];
                return drawn with { Tx = drawn.Tx - rest.Tx, Ty = drawn.Ty - rest.Ty, Tz = drawn.Tz - rest.Tz };
            }
            var right = Delta(2);
            var left = Delta(3);
            return ((right.Tx, right.Ty, right.Tz), (left.Tx, left.Ty, left.Tz));
        }
    }

    /// <summary>Cumulative retail locomotion substeps supplied to the walker.</summary>
    public long WalkerSteps { get; private set; }

    /// <summary>Walker state that affects the projected geometry and camera.</summary>
    public ((int X, int Y, int Z) Position, int Angle, int Frame) WalkerRenderPose =>
        (MechWorld, _animateWalker ? _walker!.Angle : 0, _parts[0].Frame);

    /// <summary>Ground attribute at the scene's initial placement.</summary>
    public int GroundAttribute { get; private set; }
    public PlayerActor Actor { get; }

    public byte[] Rgb { get; } = new byte[Width * Height * 3];
    public int VisibleCells { get; private set; }
    public int TilePackets { get; private set; }
    public int MechFaces { get; private set; }
    public IReadOnlyList<int> DrawnSections { get; private set; } = Array.Empty<int>();
    public (int X, int Y, int Z) Eye { get; private set; }

    /// <summary>
    /// Ramp table D_800A2630 for the k == 6/7 colour variants. Viewer placeholder, not retail:
    /// gBuildTilePattern func_800654F8 (which fills it from the SLFX definition and the frame time)
    /// is not ported, so every entry is the neutral modulation 0x80.
    /// </summary>
    public byte[] Ramp { get; } = Enumerable.Repeat((byte)0x80, 256).ToArray();

    public LevelScene(string misPath)
    {
        MissionPath = misPath;
        _file = new MisFile(misPath);
        Map = LevelMap.FromMisFile(misPath);
        _textures = TextureBank.FromMisFile(misPath);
        // D_800A8EA4 = gTexpageBaseTable[9] = the packed word of Cbmp slot 9 (80063C5C.c:144).
        var hudPage = _textures.Resources.FirstOrDefault(r => r.Id == HudTexpageResourceId);
        HudTexpageWord = hudPage is null ? 0u : ((uint)hudPage.Clut << 16) | hudPage.Tpage;
        Actor = _file.PlayerActors.FirstOrDefault() ?? throw new InvalidDataException("mission has no player actor");
        _parts = PlayerForm.Build(Actor, _file.Models, 0);
        _poseMatrices = _parts.Select(p => p.Matrix).ToArray();

        var clip = Map.Header.WorldClipRect;
        int x = (clip[2] + clip[3]) / 2, z = (clip[0] + clip[1]) / 2;
        // Probe from well above so the highest surface below the ceiling wins (func_80066F78 keeps
        // the greatest height below position[1] + 0x200).
        int ground = LevelGround.Height(Map, x, 0x100000, z, out int attribute);
        MechWorld = (x, ground, z);
        GroundAttribute = attribute;
        (_mechCx, _mechCy, _mechCz) = MechCentre();
    }

    private (int, int, int) MechCentre()
    {
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
        double z0 = double.MaxValue, z1 = double.MinValue;
        foreach (var part in _parts)
        {
            // A model without the requested frame chunk has no points to measure.
            if (!part.Model.Vertices.TryGetValue(part.Model.IsScripted ? 1 : part.Frame + 1, out var verts)) continue;
            var points = part.Model.IsScripted
                ? ScriptRender.PoseVertices(part.Model, verts, part.Frame, part.Matrix)
                : verts.Select(v => (
                    (part.Matrix.W[0] * (double)v.X + part.Matrix.W[3] * (double)v.Y + part.Matrix.W[6] * (double)v.Z) / 65536 + part.Matrix.Tx / 16.0,
                    (part.Matrix.W[1] * (double)v.X + part.Matrix.W[4] * (double)v.Y + part.Matrix.W[7] * (double)v.Z) / 65536 + part.Matrix.Ty / 16.0,
                    (part.Matrix.W[2] * (double)v.X + part.Matrix.W[5] * (double)v.Y + part.Matrix.W[8] * (double)v.Z) / 65536 + part.Matrix.Tz / 16.0)).ToArray();
            foreach (var (px, py, pz) in points)
            {
                x0 = Math.Min(x0, px); x1 = Math.Max(x1, px);
                y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
                z0 = Math.Min(z0, pz); z1 = Math.Max(z1, pz);
            }
        }
        MechBounds = (y0, y1);
        return ((int)((x0 + x1) / 2), (int)((y0 + y1) / 2), (int)((z0 + z1) / 2));
    }

    /// <summary>Draw the walker (on by default); off lets a check isolate the mech pixels.</summary>
    public bool DrawMech { get; set; } = true;

    /// <summary>
    /// When set, <see cref="Render"/> emits the triangles to this sink instead of rasterizing them
    /// into <see cref="Rgb"/>. Geometry, culling, ordering and camera maths are unchanged.
    /// </summary>
    public IPrimitiveSink? Sink { get; set; }

    /// <summary>The mission's texture memory, i.e. the source the GPU texture cache decodes from.</summary>
    public Vram Vram => _textures.Vram;

    /// <summary>
    /// <c>D_800A8EA4</c> (<c>gEnvMapTexpageWord</c>): the packed <c>(CLUT &lt;&lt; 16) | TPAGE</c> word of
    /// <c>gTexpageBaseTable[9]</c>. The two tables are the same array -
    /// <c>gTexpageBaseTable</c> is <c>D_800A8E80</c> (80063C5C.c:27) and <c>D_800A8EA4</c> is 0x24 bytes
    /// further, i.e. element 9 - written by <c>gLoadCbmpTexture</c> for <c>s0 = unk1C - 1</c>
    /// (80063C5C.c:78,96,144), i.e. the tenth uploaded Cbmp. The HUD's slot-icon emitter reads
    /// <c>[0]</c> of it (8006BF44.c:8,23). Zero when the mission ships no tenth Cbmp, which leaves the
    /// HUD on its solid-block fallback.
    /// </summary>
    public uint HudTexpageWord { get; }

    /// <summary>
    /// Cbmp resource id whose packed word is <c>gTexpageBaseTable[9]</c> = <c>D_800A8EA4</c>.
    /// <c>gLoadCbmpTexture</c> indexes the table with <c>unk1C - 1</c>, so table slot 9 is the tenth
    /// uploaded texture (80063C5C.c:78,144).
    /// </summary>
    public const int HudTexpageResourceId = 10;

    /// <summary>Local y range of the assembled walker in model units (min, max).</summary>
    public (double Min, double Max) MechBounds { get; private set; }

    /// <summary>Renders one frame. Returns the number of drawn primitives.</summary>
    public int Render(LevelCamera camera)
    {
        if (Sink is null) Array.Clear(Rgb);
        // View of the mech-local frame (model units, pivot = walker centre). Viewer camera, not the
        // retail chase camera func_800569EC.
        var view = ViewTransform.Turn(camera.Yaw, camera.Elevation, _mechCx, _mechCy, _mechCz, camera.Distance);

        // Eye in mech-local model units: camera = A p + T/16 vanishes at the eye -> e = -A^T T / 16.
        double[] t = { view.Tx / 16.0, view.Ty / 16.0, view.Tz / 16.0 };
        double A(int i, int j) => view.W[i + 3 * j] / 65536.0;
        double ex = 0, ey = 0, ez = 0;
        for (int i = 0; i < 3; i++) { ex -= A(i, 0) * t[i]; ey -= A(i, 1) * t[i]; ez -= A(i, 2) * t[i]; }
        // Mech-local model units -> world units (x16); the mech stands at MechWorld with its local
        // origin on the ground (world y up). The local frame is not rotated against the world.
        var eye = (MechWorld.X + (int)Math.Round(ex * 16), MechWorld.Y + (int)Math.Round(ey * 16),
            MechWorld.Z + (int)Math.Round(ez * 16));
        Eye = eye;
        var pivotWorld = (X: MechWorld.X + _mechCx * 16, Z: MechWorld.Z + _mechCz * 16);

        var rotation = Gte.Load(view with { Tx = 0, Ty = 0, Tz = 0 });
        var gte = new TerrainRender.GteSetup(rotation, Focal, Ofx, Ofy, 1);

        // gUpdateCameraAnchor: window origin = (look-at ground point >> 17) - 2 on both axes.
        int originZ = (pivotWorld.Z >> 17) - 2, originX = (pivotWorld.X >> 17) - 2;
        var window = CellWindow.Sample(Map, eye.Item1, eye.Item2, eye.Item3, originZ, originX, 1, rotation);

        var faces = new List<(int Key, Action Draw)>();
        var ctx = new TerrainRender.EmitContext(_texpages, Ramp, 0);
        var drawn = new List<int>();
        int packetsTotal = 0;
        VisibleCells = window.Count(e => e.Visible);
        for (int j = 0; j < 5; j++)
        {
            for (int i = 0; i < 5; i++)
            {
                var entry = window[j * 5 + i];
                if (entry.Mask == 0) continue;
                // A window cell whose section is missing in the map (corrupt or partial level data)
                // is drawn as empty instead of failing the whole frame.
                if (Map.Section(entry.SectionId) is not { } section) continue;
                drawn.Add(entry.SectionId);
                var buffer = new byte[TerrainRender.VertexBufferBytes];
                TerrainRender.ProjectGrid(section, buffer, entry.X, entry.Y, entry.Z, (uint)entry.Mask << 16, gte);
                // func_8006531C: camera position relative to the region in 1/16 tile units.
                int a = (eye.Item3 >> 9) - (originZ << 8) - 0x100 * i;
                int b = (eye.Item1 >> 9) - (originX << 8) - 0x100 * j;
                var packets = new List<uint[]>();
                uint mask = (uint)entry.Mask;
                for (int ra = 0xC0; ra >= 0; ra -= 0x40)
                    for (int rb = 0xC; rb >= 0; rb -= 4)
                    {
                        if ((mask & 1) != 0) TerrainRender.EmitCell(section, buffer, a, b, (uint)(ra + rb), ctx, packets);
                        mask >>= 1;
                    }
                packetsTotal += packets.Count;
                foreach (var p in packets)
                {
                    var packet = p;
                    faces.Add(((int)(packet[0] & 0xFFFFFF) << 8 >> 8, () => DrawPacket(packet)));
                }
            }
        }
        DrawnSections = drawn;
        TilePackets = packetsTotal;

        // The walker: mech-local parts composed with the view, the same path as WireframeScene.
        int mechFaces = 0;
        var bodyView = view;
        if (_animateWalker && _walker is { Angle: not 0 } walker)
        {
            var (sin, cos) = MechMath.SinCos(walker.Angle);
            var heading = new Gte.Mat(new[] { cos, 0, -sin, 0, 0x10000, 0, sin, 0, cos }, 0, 0, 0);
            bodyView = ScriptRender.Compose(heading, view);
        }
        foreach (var part in DrawMech ? _parts : Array.Empty<PlayerPart>())
        {
            // A model without the requested frame chunk is left out of the frame.
            if (!part.Model.Vertices.TryGetValue(part.Model.IsScripted ? 1 : part.Frame + 1, out var partVerts)) continue;
            var composed = ScriptRender.Compose(part.Matrix, bodyView);
            var projected = part.Model.IsScripted
                ? ScriptRender.Project(part.Model, partVerts, part.Frame, composed, Focal, Ofx, Ofy, 1)
                : ModelRender.Project(Gte.Load(composed), partVerts, Focal, Ofx, Ofy, 1);
            var widths = part.Model.Widths.TryGetValue(1, out var pw) ? pw.ToArray() : Array.Empty<short>();
            foreach (var face in ModelRender.WalkTextured(part.Model.Polygons, projected, widths, part.Mask, 0))
            {
                var f = face;
                var model = part.Model;
                faces.Add((f.Work.Z, () => DrawModelFace(f, model)));
                mechFaces++;
            }
        }
        MechFaces = mechFaces;

        // Live projectiles, after the walker so they sort into the same painter's-order list and end
        // up behind or in front of the terrain and the mech by their own depth.
        if (_weapons is { Shots.Count: > 0 } weapons)
        {
            foreach (var shot in weapons.Shots)
            {
                if (!shot.Model.Vertices.TryGetValue(1, out var shotVerts) || shotVerts.Count == 0) continue;
                var shotMatrix = ShotMatrix(shot, view);
                var projected = shot.Model.IsScripted
                    ? ScriptRender.Project(shot.Model, shotVerts, 0, shotMatrix, Focal, Ofx, Ofy, 1)
                    : ModelRender.Project(Gte.Load(shotMatrix), shotVerts, Focal, Ofx, Ofy, 1);
                var shotWidths = shot.Model.Widths.TryGetValue(1, out var sw) ? sw.ToArray() : Array.Empty<short>();
                foreach (var face in ModelRender.WalkTextured(shot.Model.Polygons, projected, shotWidths, 0xFF, 0))
                {
                    var f = face;
                    var model = shot.Model;
                    faces.Add((f.Work.Z, () => DrawModelFace(f, model)));
                }
            }
        }

        // Weapon pickups lying on the map (func_8002FA20). Same pipeline as the shots: local offset
        // from the mech origin, no rotation, sorted into the shared list by depth.
        if (_pickups is { RemainingCount: > 0 } pickups)
        {
            foreach (var item in pickups.Items)
            {
                if (item.Collected) continue;
                var model = _file.Models.FirstOrDefault(m => m.Id == item.ModelId);
                if (model is null) continue;
                if (!model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
                var pickupMatrix = PickupMatrix(item, view);
                var projected = model.IsScripted
                    ? ScriptRender.Project(model, verts, 0, pickupMatrix, Focal, Ofx, Ofy, 1)
                    : ModelRender.Project(Gte.Load(pickupMatrix), verts, Focal, Ofx, Ofy, 1);
                var widths = model.Widths.TryGetValue(1, out var pw) ? pw.ToArray() : Array.Empty<short>();
                foreach (var face in ModelRender.WalkTextured(model.Polygons, projected, widths, 0xFF, 0))
                {
                    var f = face;
                    var m = model;
                    faces.Add((f.Work.Z, () => DrawModelFace(f, m)));
                }
            }
        }

        // Painter's order, far first, stable for equal keys (submission order). Viewer convention:
        // retail sorts into an ordering table whose bucket mapping is not ported; both keys are SZ3
        // averages. An unstable sort reorders coplanar terrain packets whenever the mech is added.
        var ordered = faces.Select((f, i) => (f.Key, f.Draw, Index: i))
            .OrderByDescending(f => f.Key).ThenBy(f => f.Index).ToList();
        foreach (var f in ordered) f.Draw();

        // Live beams (func_8006B23C) are drawn after the sorted faces: the ribbon builder retail uses
        // is not ported, so each beam is one line from muzzle to far end and must sit on top.
        if (_weapons is { Beams.Count: > 0 } beams) BeamLines(beams, view);
        return faces.Count;
    }

    private static int Sx(uint xy) => (short)(xy & 0xFFFF);
    private static int Sy(uint xy) => (short)(xy >> 16);

    /// <summary>Decodes one func_800648E8 packet (codes 0x24/0x2C/0x34/0x3C, bit 1 = semi-transparent).</summary>
    private void DrawPacket(uint[] p)
    {
        byte code = (byte)(p[1] >> 24);
        bool semi = (code & 2) != 0;
        ushort clut = (ushort)(p[3] >> 16);
        byte[] Rgb3(uint w) => new[] { (byte)w, (byte)(w >> 8), (byte)(w >> 16) };
        switch (code & ~2)
        {
            case 0x24:
            {
                ushort tpage = (ushort)(p[5] >> 16);
                var c = Rgb3(p[1]);
                Tri(p[2], p[3], c, p[4], p[5], c, p[6], p[7], c, tpage, clut, semi);
                break;
            }
            case 0x2C:
            {
                ushort tpage = (ushort)(p[5] >> 16);
                var c = Rgb3(p[1]);
                Tri(p[2], p[3], c, p[4], p[5], c, p[6], p[7], c, tpage, clut, semi);
                Tri(p[4], p[5], c, p[8], p[9], c, p[6], p[7], c, tpage, clut, semi);
                break;
            }
            case 0x34:
            {
                ushort tpage = (ushort)(p[6] >> 16);
                Tri(p[2], p[3], Rgb3(p[1]), p[5], p[6], Rgb3(p[4]), p[8], p[9], Rgb3(p[7]), tpage, clut, semi);
                break;
            }
            case 0x3C:
            {
                ushort tpage = (ushort)(p[6] >> 16);
                Tri(p[2], p[3], Rgb3(p[1]), p[5], p[6], Rgb3(p[4]), p[8], p[9], Rgb3(p[7]), tpage, clut, semi);
                Tri(p[5], p[6], Rgb3(p[4]), p[11], p[12], Rgb3(p[10]), p[8], p[9], Rgb3(p[7]), tpage, clut, semi);
                break;
            }
        }
    }

    /// <summary>Textured, colour-modulated triangle; xy = packed screen word, uv = low 16 bits (u | v &lt;&lt; 8).</summary>
    private void Tri(uint xy0, uint uv0, byte[] c0, uint xy1, uint uv1, byte[] c1, uint xy2, uint uv2, byte[] c2,
        ushort tpage, ushort clut, bool semi)
    {
        int x0 = Sx(xy0), y0 = Sy(xy0), x1 = Sx(xy1), y1 = Sy(xy1), x2 = Sx(xy2), y2 = Sy(xy2);
        int area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        if (area == 0) return;
        int u0 = (int)(uv0 & 0xFF), v0 = (int)((uv0 >> 8) & 0xFF), u1 = (int)(uv1 & 0xFF), v1 = (int)((uv1 >> 8) & 0xFF);
        int u2 = (int)(uv2 & 0xFF), v2 = (int)((uv2 >> 8) & 0xFF);
        // GPU path: hand the triangle over instead of rasterizing it here. The order is the same,
        // because every consumer walks the same painter's-order list built in Render().
        if (Sink is not null)
        {
            Sink.TexturedTriangle(
                x0, y0, u0, v0, c0[0], c0[1], c0[2],
                x1, y1, u1, v1, c1[0], c1[1], c1[2],
                x2, y2, u2, v2, c2[0], c2[1], c2[2],
                tpage, clut, semi);
            return;
        }
        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2))), maxX = Math.Min(Width - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2))), maxY = Math.Min(Height - 1, Math.Max(y0, Math.Max(y1, y2)));
        if (minX > maxX || minY > maxY) return;
        int abr = (tpage >> 5) & 3;
        double inv = 1.0 / area;
        Span<int> rgb = stackalloc int[3];
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                int w0 = (x1 - x) * (y2 - y) - (y1 - y) * (x2 - x);
                int w1 = (x2 - x) * (y0 - y) - (y2 - y) * (x0 - x);
                int w2 = (x0 - x) * (y1 - y) - (y0 - y) * (x1 - x);
                if (area > 0 ? (w0 < 0 || w1 < 0 || w2 < 0) : (w0 > 0 || w1 > 0 || w2 > 0)) continue;
                double b0 = w0 * inv, b1 = w1 * inv, b2 = w2 * inv;
                int u = (int)Math.Round(b0 * u0 + b1 * u1 + b2 * u2), v = (int)Math.Round(b0 * v0 + b1 * v1 + b2 * v2);
                ushort raw = _textures.Vram.SampleRaw(tpage, clut, u, v);
                if (raw == 0) continue; // PS1: texel 0x0000 is fully transparent
                var (tr, tg, tb, _) = Vram.Abgr1555ToRgb(raw);
                int o = (y * Width + x) * 3;
                rgb[0] = tr; rgb[1] = tg; rgb[2] = tb;
                for (int ch = 0; ch < 3; ch++)
                {
                    double cm = b0 * c0[ch] + b1 * c1[ch] + b2 * c2[ch];
                    int value = Math.Min(255, (int)(rgb[ch] * cm / 128.0));
                    if (semi && (raw & 0x8000) != 0)
                    {
                        int back = Rgb[o + ch];
                        value = abr switch
                        {
                            0 => (back + value) >> 1,
                            1 => Math.Min(255, back + value),
                            2 => Math.Max(0, back - value),
                            _ => Math.Min(255, back + (value >> 2)),
                        };
                    }
                    Rgb[o + ch] = (byte)value;
                }
            }
    }

    /// <summary>
    /// View matrix of a projectile: the retail basis of func_800193F8 built from the flight vector,
    /// placed where the shot is. Retail rebuilds the same basis every tick (func_80035B7C:554-568), so
    /// the model nose (model +Z) points along the velocity.
    /// </summary>
    /// <remarks>
    /// The shot lives in world position units, but <paramref name="view"/> is the mech-local frame the
    /// level draws everything else in, so the position is converted here: subtract the walker's world
    /// position and divide by 16 (one model unit is 16 position units, func_80010750). Writing the raw
    /// world position with &lt;&lt; 4 put every projectile a few map widths away, which is why nothing
    /// showed up. The scale factor is 1 like the original - the wireframe viewer's 1.5 is its own
    /// convention (REPORT-feuern.md).
    /// </remarks>
    private Gte.Mat ShotMatrix(Shot shot, Gte.Mat view)
    {
        var basis = ShotMotion.FlightBasis(shot.Direction);
        int lx = (shot.X - MechWorld.X) >> 4;
        int ly = (shot.Y - MechWorld.Y) >> 4;
        int lz = (shot.Z - MechWorld.Z) >> 4;
        var local = new Gte.Mat(new[]
        {
            basis[0], basis[1], basis[2],
            basis[3], basis[4], basis[5],
            basis[6], basis[7], basis[8],
        }, lx << 4, ly << 4, lz << 4);
        return ScriptRender.Compose(local, view);
    }

    /// <summary>
    /// Model matrix of a pickup: the item's map position relative to the mech origin, in model units,
    /// with the identity orientation. func_8002FA20:92-113 stores position and extents only, so the
    /// retail item is not rotated.
    /// </summary>
    private Gte.Mat PickupMatrix(WeaponPickup item, Gte.Mat view)
    {
        int lx = (item.X - MechWorld.X) >> 4;
        int ly = (item.Y - MechWorld.Y) >> 4;
        int lz = (item.Z - MechWorld.Z) >> 4;
        // 0x10000 is 1.0 in engine matrix units (Gte.Load shifts the rotation right by 4).
        var local = new Gte.Mat(new[]
        {
            0x10000, 0, 0,
            0, 0x10000, 0,
            0, 0, 0x10000,
        }, lx << 4, ly << 4, lz << 4);
        return ScriptRender.Compose(local, view);
    }

    /// <summary>
    /// Draws every live beam as a line from muzzle to far end, projected with the level camera. The
    /// beam is the one thing retail hands to a ribbon builder the port does not have, so the geometry
    /// is retail and only the look is the viewer's.
    /// </summary>
    private void BeamLines(LevelWeapons weapons, Gte.Mat view)
    {
        foreach (var beam in weapons.Beams)
        {
            var a = ProjectWorld(beam.X, beam.Y, beam.Z, view);
            var b = ProjectWorld(beam.End.X, beam.End.Y, beam.End.Z, view);
            if (a is null || b is null) continue;
            // Pale blue, the tracer colour func_8006B23C:418-421 asks for (0x300/0x200 with 0x30).
            Line(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y, new byte[] { 0xB0, 0xD8, 0xFF });
        }
    }

    /// <summary>Projects one world position to screen pixels, or null when it is behind the camera.</summary>
    private (int X, int Y)? ProjectWorld(int x, int y, int z, Gte.Mat view)
    {
        int lx = (x - MechWorld.X) >> 4;
        int ly = (y - MechWorld.Y) >> 4;
        int lz = (z - MechWorld.Z) >> 4;
        var local = new Gte.Mat(new[]
        {
            0x10000, 0, 0,
            0, 0x10000, 0,
            0, 0, 0x10000,
        }, lx << 4, ly << 4, lz << 4);
        var point = ModelRender.Project(Gte.Load(ScriptRender.Compose(local, view)),
            new[] { new Vertex(0, 0, 0) }, Focal, Ofx, Ofy, 1);
        if (point.Length == 0 || (point[0].Clip & 1) != 0) return null;
        return (Sx((uint)point[0].Xy), Sy((uint)point[0].Xy));
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

    private Dictionary<int, TextureRecord> TextureLookup(Model model)
    {
        if (!_texByModel.TryGetValue(model.Id, out var map))
        {
            map = model.Textures.GroupBy(t => t.Offset).ToDictionary(g => g.Key, g => g.First());
            _texByModel[model.Id] = map;
        }
        return map;
    }

    /// <summary>Mech face, drawn like WireframeScene.DrawTexturedFace (3DTL record, unmodulated texels).</summary>
    private void DrawModelFace(TexturedFace face, Model model)
    {
        bool hasRec = TextureLookup(model).TryGetValue(face.TextureOffset, out var rec);
        uint uvA = hasRec ? rec.Uv0 : 0, uvB = hasRec ? rec.Uv1 : 0;
        uint UV(int i) => i switch
        {
            0 => uvA & 0xFFFF, 1 => uvA >> 16, 2 => uvB & 0xFFFF, _ => uvB >> 16,
        };
        bool flat = hasRec && rec.TypeLoaded is 0 or 1;
        if (!hasRec || flat)
        {
            byte[] c = hasRec ? new[] { (byte)(rec.Rgb >> 16), (byte)(rec.Rgb >> 8), (byte)rec.Rgb } : new byte[] { 255, 0, 255 };
            FlatFace(face, c);
            return;
        }
        var w = new byte[] { 0x80, 0x80, 0x80 };
        uint a = (uint)face.Work.Xy0, b = (uint)face.Work.Xy1, c2 = (uint)face.Work.Xy2, d = (uint)face.Work.Xy3;
        if (face.Type == 3)
            Tri(a, UV(0), w, b, UV(1), w, c2, UV(2), w, rec.Tpage, rec.Clut, false);
        else if (face.Type == 4)
        {
            // Work holds (v0, v1, v3, v2).
            Tri(a, UV(0), w, b, UV(1), w, c2, UV(3), w, rec.Tpage, rec.Clut, false);
            Tri(b, UV(1), w, d, UV(2), w, c2, UV(3), w, rec.Tpage, rec.Clut, false);
        }
        else
        {
            Tri(a, UV(0), w, b, UV(1), w, c2, UV(2), w, rec.Tpage, rec.Clut, false);
            Tri(b, UV(1), w, d, UV(3), w, c2, UV(2), w, rec.Tpage, rec.Clut, false);
        }
    }

    private void FlatFace(TexturedFace face, byte[] colour)
    {
        void Fill(uint p0, uint p1, uint p2)
        {
            int x0 = Sx(p0), y0 = Sy(p0), x1 = Sx(p1), y1 = Sy(p1), x2 = Sx(p2), y2 = Sy(p2);
            int area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
            if (area == 0) return;
            if (Sink is not null)
            {
                Sink.FlatTriangle(x0, y0, x1, y1, x2, y2, colour[0], colour[1], colour[2]);
                return;
            }
            for (int y = Math.Max(0, Math.Min(y0, Math.Min(y1, y2))); y <= Math.Min(Height - 1, Math.Max(y0, Math.Max(y1, y2))); y++)
                for (int x = Math.Max(0, Math.Min(x0, Math.Min(x1, x2))); x <= Math.Min(Width - 1, Math.Max(x0, Math.Max(x1, x2))); x++)
                {
                    int w0 = (x1 - x) * (y2 - y) - (y1 - y) * (x2 - x);
                    int w1 = (x2 - x) * (y0 - y) - (y2 - y) * (x0 - x);
                    int w2 = (x0 - x) * (y1 - y) - (y0 - y) * (x1 - x);
                    if (area > 0 ? (w0 < 0 || w1 < 0 || w2 < 0) : (w0 > 0 || w1 > 0 || w2 > 0)) continue;
                    int o = (y * Width + x) * 3;
                    Rgb[o] = colour[0]; Rgb[o + 1] = colour[1]; Rgb[o + 2] = colour[2];
                }
        }
        uint a = (uint)face.Work.Xy0, b = (uint)face.Work.Xy1, c = (uint)face.Work.Xy2, d = (uint)face.Work.Xy3;
        Fill(a, b, c);
        if (face.Type != 3) Fill(b, d, c);
    }
}
