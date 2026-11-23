using System.Globalization;
using FutureCop.Assets;
using FutureCop.View;

// First runnable start of a Precinct Assault map: retail terrain plus the player walker in the
// centre, written as 24-bit BMP files (one per yaw angle).
//
//   FcLevelStart [--disc <build/disc/files>] [--mis <file.MIS>] [--entry <0..4>] [--out <prefix>]
//                [--yaw a,b,...] [--elev deg] [--dist units] [--scale n] [--no-mech] [--hud]
//
// Without --mis the mission comes from the Precinct Assault list D_8009B72C[entry] -> mission
// record D_8009B4E4 -> name + ".mis" -> func_800833A8 directory prefix.
static string? Arg(string[] a, string name)
{
    int i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

string disc = Arg(args, "--disc") ?? Path.Combine(Directory.GetCurrentDirectory(), "build", "disc", "files");
int entry = int.Parse(Arg(args, "--entry") ?? "0", CultureInfo.InvariantCulture);
string? mis = Arg(args, "--mis");
if (mis == null)
{
    var record = PrecinctAssaultData.MissionRecords[PrecinctAssaultData.PrecinctAssaultMissionList[entry]];
    // Where retail appends ".mis" to the record name is still open (docs/precinct-assault-path.md).
    string module = record.Name + ".mis";
    mis = ModulePath.Resolve(disc, module)
        ?? throw new FileNotFoundException($"{ModulePath.Prefix(module)}{module} not found under {disc}");
    Console.WriteLine($"entry {entry}: list value {PrecinctAssaultData.PrecinctAssaultMissionList[entry]} -> record " +
                      $"'{record.Name}' -> {ModulePath.Prefix(module)}{module} -> {mis}");
}
string prefix = Arg(args, "--out") ?? "level";
var yaws = (Arg(args, "--yaw") ?? "30").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
double elev = double.Parse(Arg(args, "--elev") ?? "35", CultureInfo.InvariantCulture);
int dist = int.Parse(Arg(args, "--dist") ?? "7168", CultureInfo.InvariantCulture);
int scale = int.Parse(Arg(args, "--scale") ?? "2", CultureInfo.InvariantCulture);

var scene = new LevelScene(mis) { DrawMech = !args.Contains("--no-mech") };

// Sample state for the --hud preview: the plates and numbers are drawn with placeholder values
// because the port has no match scoring yet. The gauge words are the retail pair - the value and the
// Q16 reciprocal 0x600000 / divisor (func_8005A0C4.c:44-48) - so full health gives the full height.
var hudGame = new HudGameBlock
{
    Health = 100,
    MaxHealth = 100,
    Player = new HudPlayerBlock { Value58 = 100, Value5C = 0x600000 / 100, Value64 = 2 },
};
var hudSession = new HudSession
{
    Mode = 1, Stage = 1,
    Score0 = 1234, Score1 = 380, Count1 = 2, Count2 = 5, Count3 = 1, Count4 = 3,
};
var hudGlobals = new HudGlobals
{
    ScreenState = 4, PlayerCount = 1, FrameTime = 0x10, EnvMapTexpageWord = scene.HudTexpageWord,
};
var h = scene.Map.Header;
Console.WriteLine($"map: GRDB {h.Width}x{h.Height} cells, {scene.Map.Sections.Count} Ctil sections, clip rect " +
                  string.Join(",", h.WorldClipRect.Select(v => "0x" + v.ToString("X"))));
Console.WriteLine($"mech: actor {scene.Actor.Id} ({string.Join(",", scene.Actor.Resources.Select(r => $"{r.Type}:{r.Id}"))}) " +
                  $"at world ({scene.MechWorld.X},{scene.MechWorld.Y},{scene.MechWorld.Z}), ground attribute {scene.GroundAttribute}, " +
                  $"local y {scene.MechBounds.Min:F0}..{scene.MechBounds.Max:F0}");
foreach (double yaw in yaws)
{
    int prims = scene.Render(new LevelCamera(yaw, elev, dist));
    if (args.Contains("--hud")) InGameHud.Paint(scene.Rgb, hudGame, hudSession, hudGlobals, scene.Vram);
    string file = yaws.Length == 1 ? prefix + ".bmp" : $"{prefix}-{(int)yaw:000}.bmp";
    var rgb = scene.Rgb;
    int w = LevelScene.Width * scale, hh = LevelScene.Height * scale;
    var scaled = new byte[w * hh * 3];
    for (int y = 0; y < hh; y++)
        for (int x = 0; x < w; x++)
        {
            int s = ((y / scale) * LevelScene.Width + x / scale) * 3, d = (y * w + x) * 3;
            scaled[d] = rgb[s]; scaled[d + 1] = rgb[s + 1]; scaled[d + 2] = rgb[s + 2];
        }
    TextureBank.WriteBmp24(file, w, hh, scaled);
    Console.WriteLine($"yaw {yaw}: eye ({scene.Eye.X},{scene.Eye.Y},{scene.Eye.Z}), {scene.VisibleCells} visible cells, " +
                      $"sections [{string.Join(",", scene.DrawnSections)}], {scene.TilePackets} terrain packets, " +
                      $"{scene.MechFaces} mech faces, {prims} primitives -> {file}");
}
