namespace FutureCop.View;

/// <summary>
/// Counters of one solo simulation frame (<c>gRunSoloSessionFrame</c>, <c>800271A4.c:177-217</c>),
/// returned by <see cref="LevelScene.AdvanceSoloFrame"/>. They let a host or a test read the
/// per-frame passes off the frame itself instead of deriving them from the locomotion substeps:
/// retail runs each pass once per frame, and <see cref="Steps"/> only says how many substeps of
/// <c>func_800588D4</c> sat inside that one frame.
/// </summary>
/// <param name="Ticks"><c>gFrameDelta</c> of the frame in retail clock ticks (<c>8002703C.c:15,71</c>).</param>
/// <param name="Steps">
/// Locomotion substeps the player update ran in this frame (<c>func_800588D4</c> hands one to
/// <c>func_800549F0</c> per eight ticks).
/// </param>
/// <param name="Dispatched">
/// Queued entity command pairs applied by <c>gDispatchQueuedEntityCommands</c>
/// (<c>800271A4.c:179</c>, <c>func_8004CECC</c>).
/// </param>
/// <param name="Message6">
/// Objects reached by <c>gBroadcastEntityMessage(6, gFrameDelta, 0)</c> (<c>800271A4.c:180</c>,
/// <c>8004CD6C.c:41-54</c>).
/// </param>
/// <param name="Spawned">
/// Units spawned by <c>gUpdateRegisteredSpawners</c> (<c>800271A4.c:210</c>, <c>800616A0.c</c>).
/// </param>
/// <param name="Swept">
/// Entities freed by <c>gSweepRetiredEntities</c> (<c>800271A4.c:212</c>, <c>8004D598.c:39-71</c>).
/// </param>
public readonly record struct SoloFrameResult(
    int Ticks, int Steps, int Dispatched, int Message6, int Spawned, int Swept);
