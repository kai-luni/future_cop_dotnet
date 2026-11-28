using System.Diagnostics;
using System.Globalization;
using FcMainMenu;
using FutureCop.Game.Mech;
using FutureCop.View;

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string ResolveFrontEnd(string? explicitDisc)
{
    if (explicitDisc is not null)
    {
        string p = Path.Combine(explicitDisc, "CW", "FE.MIS");
        if (File.Exists(p)) return p;
        throw new FileNotFoundException($"FE.MIS not found under {explicitDisc}", p);
    }

    for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "build", "disc", "files", "CW", "FE.MIS");
        if (File.Exists(candidate)) return candidate;
    }

    throw new FileNotFoundException("Could not locate build/disc/files/CW/FE.MIS from working directory.");
}

int frames = int.Parse(Arg(args, "--frames") ?? "600", CultureInfo.InvariantCulture);
int warmup = int.Parse(Arg(args, "--warmup") ?? "60", CultureInfo.InvariantCulture);
string zoneArg = Arg(args, "--zone") ?? "0";
int[] zones = string.Equals(zoneArg, "all", StringComparison.OrdinalIgnoreCase)
    ? new[] { 0, 1, 2, 3, 4 }
    : new[] { int.Parse(zoneArg, CultureInfo.InvariantCulture) };
string mode = Arg(args, "--mode") ?? "play";
double dt = double.Parse(Arg(args, "--dt") ?? "0.016666666666666666", CultureInfo.InvariantCulture);
bool noBlit = args.Contains("--no-blit");
bool noRender = args.Contains("--no-render");

string fePath = ResolveFrontEnd(Arg(args, "--disc"));

foreach (int zone in zones)
{
    int rc = RunZone(fePath, zone, mode, warmup, frames, dt, noBlit, noRender);
    if (rc != 0) return rc;
}

return 0;

static int RunZone(string fePath, int zone, string mode, int warmup, int frames, double dt, bool noBlit, bool noRender)
{
const uint KeyEnter = 36, KeyRight = 114, KeyDown = 116;
long loadStart = Stopwatch.GetTimestamp();
var flow = new MenuFlow(fePath, SceneKind.Level, TextWriter.Null, Console.Error, enableLevelMovement: true);
flow.HandleKey(KeyDown);
flow.HandleKey(KeyDown);
flow.HandleKey(KeyEnter);
flow.HandleKey(KeyEnter);
flow.HandleKey(KeyEnter);
for (int i = 0; i < zone; i++) flow.HandleKey(KeyRight);
flow.HandleKey(KeyEnter);
flow.HandleKey(KeyEnter);
double loadMs = Stopwatch.GetElapsedTime(loadStart).TotalMilliseconds;

if (flow.Screen != MenuScreen.Scene || flow.Level is null)
{
    Console.Error.WriteLine($"Failed to start level scene (screen={flow.Screen}, error={flow.LastLoadErrorPath}).");
    return 1;
}

var level = flow.Level;
var bgrx = new byte[LevelScene.Width * 2 * LevelScene.Height * 2 * 4];
MenuFlow.Profile = true;

PadButtons ButtonsForFrame(int frame)
{
    if (string.Equals(mode, "idle", StringComparison.OrdinalIgnoreCase))
        return PadButtons.None;

    // Deterministic active gameplay input: walk forward, gently weave left/right, and fire bursts.
    PadButtons b = PadButtons.Up;
    int cycle = frame % 120;
    if (cycle is >= 30 and < 60) b |= PadButtons.Right;
    else if (cycle is >= 90 and < 120) b |= PadButtons.Left;
    if ((frame % 40) < 12) b |= PadButtons.Square;

    if (string.Equals(mode, "combat", StringComparison.OrdinalIgnoreCase))
    {
        if ((frame % 30) < 10) b |= PadButtons.Cross;
        if ((frame % 45) < 10) b |= PadButtons.Circle;
        if (frame is 150 or 350) b |= PadButtons.L1 | PadButtons.R1;
    }

    return b;
}

void StepOne(int frameIndex, out double advMs, out long advBytes, out double lvlMs, out long lvlBytes,
    out double hudMs, out long hudBytes, out double blitMs, out long blitBytes)
{
    PadButtons buttons = ButtonsForFrame(frameIndex);

    long t0 = Stopwatch.GetTimestamp();
    long a0 = GC.GetAllocatedBytesForCurrentThread();
    flow.Advance(dt, buttons, AnalogPad.Digital);
    advBytes = GC.GetAllocatedBytesForCurrentThread() - a0;
    advMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

    if (noRender)
    {
        lvlMs = hudMs = blitMs = 0;
        lvlBytes = hudBytes = blitBytes = 0;
        return;
    }

    flow.RenderScene(bgrx, noBlit ? 1 : 2);
    lvlMs = MenuFlow.MsLevel;
    lvlBytes = MenuFlow.BytesLevel;
    hudMs = MenuFlow.MsHud;
    hudBytes = MenuFlow.BytesHud;
    blitMs = MenuFlow.MsBlit;
    blitBytes = MenuFlow.BytesBlit;
}

for (int i = 0; i < warmup; i++)
{
    StepOne(i, out _, out _, out _, out _, out _, out _, out _, out _);
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

int gc0Start = GC.CollectionCount(0);
int gc1Start = GC.CollectionCount(1);
int gc2Start = GC.CollectionCount(2);

var totalFrameMs = new double[frames];
double sumAdvMs = 0, sumLvlMs = 0, sumHudMs = 0, sumBlitMs = 0;
long sumAdvBytes = 0, sumLvlBytes = 0, sumHudBytes = 0, sumBlitBytes = 0;
long sumPackets = 0, sumCells = 0, sumMechFaces = 0, sumSpawnedPrims = 0;

long benchStart = Stopwatch.GetTimestamp();
for (int i = 0; i < frames; i++)
{
    StepOne(warmup + i, out double advMs, out long advBytes, out double lvlMs, out long lvlBytes,
        out double hudMs, out long hudBytes, out double blitMs, out long blitBytes);

    totalFrameMs[i] = advMs + lvlMs + hudMs + blitMs;
    sumAdvMs += advMs;
    sumLvlMs += lvlMs;
    sumHudMs += hudMs;
    sumBlitMs += blitMs;
    sumAdvBytes += advBytes;
    sumLvlBytes += lvlBytes;
    sumHudBytes += hudBytes;
    sumBlitBytes += blitBytes;
    sumPackets += level.TilePackets;
    sumCells += level.VisibleCells;
    sumMechFaces += level.MechFaces;
    sumSpawnedPrims += level.SpawnedUnitPrimitives;
}
double wallMs = Stopwatch.GetElapsedTime(benchStart).TotalMilliseconds;

int gc0 = GC.CollectionCount(0) - gc0Start;
int gc1 = GC.CollectionCount(1) - gc1Start;
int gc2 = GC.CollectionCount(2) - gc2Start;

Array.Sort(totalFrameMs);
double meanMs = totalFrameMs.Average();
double p50Ms = totalFrameMs[frames / 2];
double p95Ms = totalFrameMs[(int)((frames - 1) * 0.95)];
double maxMs = totalFrameMs[^1];
double totalKbPerFrame = (sumAdvBytes + sumLvlBytes + sumHudBytes + sumBlitBytes) / (1024.0 * frames);

Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
    "MISSION={0} MODE={1} WARMUP={2} FRAMES={3} LOAD_MS={4:F1}",
    Path.GetFileName(level.MissionPath), mode, warmup, frames, loadMs));
Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
    "FRAME_MS mean={0:F3} p50={1:F3} p95={2:F3} max={3:F3} wall_ms={4:F1} fps={5:F1}",
    meanMs, p50Ms, p95Ms, maxMs, wallMs, frames * 1000.0 / wallMs));
Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
    "SPLIT_MS adv={0:F3} lvl={1:F3} hud={2:F3} blit={3:F3}",
    sumAdvMs / frames, sumLvlMs / frames, sumHudMs / frames, sumBlitMs / frames));
Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
    "ALLOC_KB/frame total={0:F1} adv={1:F1} lvl={2:F1} hud={3:F1} blit={4:F1} gc={5}/{6}/{7}",
    totalKbPerFrame,
    sumAdvBytes / (1024.0 * frames),
    sumLvlBytes / (1024.0 * frames),
    sumHudBytes / (1024.0 * frames),
    sumBlitBytes / (1024.0 * frames),
    gc0, gc1, gc2));
Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
    "COUNTS cells={0:F1} packets={1:F1} mech={2:F1} spawned_prims={3:F1} pad_lines={4} units={5} active={6} pos=({7},{8},{9})",
    sumCells / (double)frames,
    sumPackets / (double)frames,
    sumMechFaces / (double)frames,
    sumSpawnedPrims / (double)frames,
    level.PadMarkerLines,
    level.Entities?.Units.Count ?? 0,
    level.Entities?.Active.Count ?? 0,
    level.MechWorld.X, level.MechWorld.Y, level.MechWorld.Z));

return 0;
}
