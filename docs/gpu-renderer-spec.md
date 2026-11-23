# GPU renderer (OpenGL ES 2.0)

`src/FutureCop.Gpu` turns the decoded PS1 primitive stream of `FutureCop.View.LevelScene`
(`DrawPacket` / `Tri`) into GL ES 2.0 draw calls. It is a renderer, not a GTE: geometry arrives
already projected into PS1 screen space.

The behaviour below is verified against a real GL ES 2.0 implementation by `tools/FcGpuProbe`
(EGL surfaceless, no X server). See [Verification](#verification) for the measured deviation
against the software reference.

## Tizen scene preview (2026-11-19)

`tools/FcLevelGl.Tizen` renders the actual Precinct Assault `CONFT.MIS` scene using
`SceneCommandEmitter` and `GlEs2Renderer`, without a CPU bitmap. The existing
`FcLevelTv` CPU path and `FcGpuProbe.Tizen` reference remain separate and unchanged.
The tested TV uses `Arm32ManualMapper.dll`, .NET 10, Tizen 9 and Mali-G52.

This is a **static scene preview**, not the live game: camera input and resize wake
an on-demand render burst; when still, the last frame remains visible and rendering
stops. Dynamic game simulation or animations will need their own invalidation policy.
On this TV both `GLView.RenderOnce()` and `Window.RenderOnce()` are needed to resume
rendering. The integrated build stayed alive for 10 minutes 26 seconds including
camera input and was restarted successfully. During active bursts sampled log windows
showed roughly 43–58 FPS; idle means no continuously rendered FPS. This is not a
sustained-load guarantee: long continuous input was not tested. The screenshot was
saved outside the repository as `fc-gpu-integrated-final.png`.

Earlier continuous runs reportedly ended with SIGABRT near `cpuUsageMonitor` messages,
but the available app logs do not establish the sender or cause. A later continuous
run produced kernel SIGSEGV reports for PID 2327; its cause is unknown. A separate
cached-frame experiment also hit a native render-thread SIGSEGV and is **not** included.
See [the investigation report](gpu-tv-investigation-2026-11-19.md) for the attempts,
measurements, limitations and remaining risks. No TV system services were modified.
Tests on this snapshot: 95 GPU tests, 137 asset tests and 141 host C level vectors passed.
Context loss/recreation was covered at texture-upload level in a fake GPU test, not induced on
the TV. The renderer is still experimental and its ordering-table and full visual
fidelity limitations below continue to apply.

## ABI rule (hard requirement)

The target is Samsung Tizen on ARMv7 with **softfp** system libraries while CoreCLR is **hardfp**,
so every float passed by value to `libGLESv2` arrives garbled. `IGpuApi` therefore only uses
`int`, `uint`, `bool`, `IntPtr`, `byte[]` and `string` parameters. `glClearColor`, `glBlendColor`,
`glDepthRangef`, `glLineWidth`, `glTexParameterf` and `glUniform1f/2f/3f/4f` /
`glVertexAttrib*f` do not exist here; float data travels as a pointer with `glUniform1fv`,
`glUniform4fv`, `glUniformMatrix4fv`, `glBufferData` and `glBufferSubData`.
`tests/FutureCop.Gpu.Tests/GpuAbiTests.cs` enforces this by reflection.

`GlEs2Api` is the device implementation of that interface and contains **no `DllImport` stub at
all**. On the TV, both `[DllImport("GLESv2")]` with a `NativeLibrary.SetDllImportResolver` and
`[DllImport("libGLESv2.so")]` ended the process without a message on the first call (`glGetError`,
`glGetString`), while the same entry points through `NativeLibrary.TryLoad` +
`NativeLibrary.TryGetExport` + `Marshal.GetDelegateForFunctionPointer` worked. Every entry point is
therefore a `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]` delegate. Library order is
`libGLESv2.so.2`, then `libGLESv2.so`; a missing library throws a `DllNotFoundException` naming
both candidates, a missing export an `EntryPointNotFoundException` naming the function.
`GlEs2ApiTests.NoDllImportAnywhereInTheGpuAssembly` fails if a stub is ever reintroduced.

## API

```csharp
using var gl = GlEs2Api.Create();              // libGLESv2.so.2, then libGLESv2.so
var renderer = new GlEs2Renderer(gl);          // gl : IGpuApi (a fake in the tests)
renderer.SetBackdropColor(0.10f, 0.25f, 0.45f); // clamped, default opaque black
renderer.SetViewport(640, 480);                // target pixels (primitive space stays 320x240)

var cache = new VramTextureCache(vram);        // one 256x256 RGBA8 decode per (tpage, clut)
cache.Upload(renderer, new TextureKey(tpage, clut));

var stream = new Ps1CommandStream();           // reusable, insertion order is draw order
stream.Add(new Ps1Primitive(key, semi, v0, v1, v2, sortKey));
renderer.Render(stream);                       // or Render(ReadOnlySpan<Ps1Primitive>)

int draws = renderer.LastDrawCalls;            // includes the backdrop quad
int triangles = renderer.LastTriangleCount;    // scene triangles only
renderer.Dispose();
```

- `TextureKey(ushort Tpage, ushort Clut)` — record struct; `Abr`, `PageX`, `PageY`, `Depth` decode
  the tpage word. Equality includes the ABR bits, but the renderer keys its texture table on
  `Tpage & ~0x0060`, so one page used in four ABR variants costs one texture, not four.
- `Ps1Vertex(x, y, u, v, r, g, b)` — screen pixels 0..320 / 0..240, texel coords 0..255,
  modulation colour 0..255.
- `Ps1Primitive(TextureKey, bool SemiTransparent, v0, v1, v2, int SortKey = 0)` — one triangle.
  `SortKey` is advisory; nothing sorts on it.
- `Ps1CommandStream` — `Add`, `AddRange`, `Clear`, `Count`, `Capacity`, indexer; the array grows
  and is reused, no per-frame allocation in steady state.
- `VramTextureCache` — `GetOrDecode`, `Decode`, `Upload`, `Invalidate`, `Invalidate(key)`.

## Vertex stream

One dynamic VBO, 7 floats per vertex, interleaved:

| offset | attribute | size | meaning |
| --- | --- | --- | --- |
| 0 | `aPosition` (loc 0) | 2 | screen pixels, origin top-left |
| 8 | `aTexCoord` (loc 1) | 2 | texel coords 0..255 |
| 16 | `aColor` (loc 2) | 3 | modulation colour 0..255 |

Stride 28 bytes. Attribute locations are fixed with `glBindAttribLocation` **before** `glLinkProgram`,
so the shader cannot be given different slots than the pointer setup uses; the pointers themselves
are (re-)specified after every `glBufferData`, because GL ES 2.0 keeps bound-pointer state globally
and re-specifying buffer storage does not require it but re-binds nothing either.
Uniform locations are read after linking and validated — a `-1` (a uniform the compiler dropped)
throws instead of silently rendering black. Primitives are expanded to triangles in stream order and
consecutive triangles with the same state (texture id, blend mode, alpha mode) become one
`glDrawArrays`. Because a run only ever grows at the tail, batches never cross a primitive boundary,
so draw order equals stream order. No depth buffer, no depth test, no sorting.

## Frame backdrop

`GLView` owns and clears its own framebuffer, and fragments without geometry keep showing the
window background — a level with a missing node would show the TV's window, not black. Setup also
cannot use `glClearColor` (float parameters arrive as 0.0 on the device, measured:
`glClearColor(0.10, 0.25, 0.45, 1)` + `glClear` produced a black frame). Every `Render` therefore
starts with a two-triangle quad at `(0,0)-(320,240)` in primitive space, i.e. exactly the NDC
square, and the fragment shader writes the `uBackdrop` vec4 for `uAlphaMode > 2.5`. The colour
reaches the shader through `glUniform4fv` on a pinned `float[4]`; alpha is always 1. The quad is a
normal run in the same VBO, so an empty frame costs exactly one `glDrawArrays`, and it never merges
with scene geometry because its alpha mode differs.

## Shaders (`Ps1Shaders`)

Vertex stage:

```glsl
attribute vec2 aPosition;   // PS1 screen pixels, origin top-left
attribute vec2 aTexCoord;   // page-local texel coordinates 0..255
attribute vec3 aColor;      // modulation colour 0..255

uniform vec4 uScreen;       // 320.0, 240.0, 0.0, 0.0

varying highp vec2 vTexCoord;
varying highp vec3 vColor;

void main()
{
    // Screen coordinates name pixel columns/rows, but GL samples a fragment at (x + 0.5, y + 0.5).
    // Adding half a pixel moves the sample point onto the integer coordinate LevelScene.Tri
    // evaluates, so coverage and the interpolated UVs match the software reference exactly.
    // w stays 1.0: no perspective correction, so texture coordinates are interpolated affinely.
    vec2 sample = aPosition + vec2(0.5, 0.5);
    vec2 ndc = vec2(sample.x / uScreen.x * 2.0 - 1.0,
                    1.0 - sample.y / uScreen.y * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
}
```

Fragment stage:

```glsl
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#define FC_VARYING highp
#else
precision mediump float;
#define FC_VARYING mediump
#endif

uniform sampler2D uTexture;
uniform float uAlphaMode;   // 0 = single pass, 1 = semi pass, 2 = opaque pass, 3 = backdrop
uniform float uColorScale;  // 1.0 normally, 0.25 for ABR3 (quarter addition)
uniform vec4 uBackdrop;     // frame background, written by the backdrop quad

varying FC_VARYING vec2 vTexCoord;
varying FC_VARYING vec3 vColor;

void main()
{
    if (uAlphaMode > 2.5)
    {
        gl_FragColor = uBackdrop;
        return;
    }

    // Texel coordinates name texel centres, so shift by half a texel before normalising: this makes
    // the sampler pick round(u, v) like LevelScene.Tri, not floor(u, v).
    vec4 texel = texture2D(uTexture, (vTexCoord + vec2(0.5, 0.5)) * (1.0 / 256.0));

    // alpha 0 = texel 0x0000 (transparent), 0.5 = STP bit set, 1.0 = opaque.
    if (uAlphaMode < 0.5)
    {
        if (texel.a < 0.25) discard;                      // single pass: drop 0x0000 only
    }
    else if (uAlphaMode < 1.5)
    {
        if (texel.a < 0.25 || texel.a > 0.75) discard;     // semi pass: STP texels only
    }
    else
    {
        if (texel.a < 0.75) discard;                      // opaque pass: non-STP texels only
    }

    // PS1 modulation: texel * (colour / 128). The colour range is 0..255, so the factor reaches 2.0
    // and must NOT be clamped - LevelScene.Tri computes min(255, texel * cm / 128), i.e. it
    // saturates the result. Clamping the factor caps every bright texel at its raw value.
    vec3 modulate = vColor * (1.0 / 128.0);
    vec3 rgb = min(texel.rgb * modulate, vec3(1.0));
    gl_FragColor = vec4(rgb * uColorScale, texel.a);
}
```

`uScreen` is `(320, 240)`; `SetViewport` only changes `glViewport`. The varying precision is `highp`
where the GPU offers fragment `highp`, because `mediump` on a tile GPU is often fp16: at a texture
coordinate of 255 an fp16 varying has a step of 0.125 texel, which flips samples onto neighbouring
texels along every texture edge. Measured on llvmpipe, `mediump` varyings change 721 of 76,800
pixels, 350 of them by more than 2 LSB; `highp` costs nothing and removes that class of error.

The alpha tail of 0.5 makes ABR0 a half blend with `SRC_ALPHA`. ABR3 multiplies the
saturated foreground by 0.25 so the blend stays `ONE/ONE`.

## Alpha and blending mapping

| primitive | passes | discard | blending |
| --- | --- | --- | --- |
| backdrop quad | 1 (`Backdrop`) | none, writes `uBackdrop` | off |
| without semi flag | 1 (`Single`) | `alpha < 0.25` | off |
| with semi flag | 2a (`Semi`) | `alpha < 0.25` or `alpha > 0.75` | by ABR |
| with semi flag | 2b (`Opaque`) | `alpha < 0.75` | off |

Both passes of a semi primitive are emitted back to back in stream order; the semi pass comes
first, so it sees the colour written by everything before the primitive.

| ABR (tpage bits 5..6) | blend equation | factors | shader scale |
| --- | --- | --- | --- |
| 0 `(back+fg)/2` | `FUNC_ADD` | `SRC_ALPHA`, `ONE_MINUS_SRC_ALPHA` | 1.0 |
| 1 `min(255, back+fg)` | `FUNC_ADD` | `ONE`, `ONE` | 1.0 |
| 2 `max(0, back-fg)` | `FUNC_REVERSE_SUBTRACT` | `ONE`, `ONE` | 1.0 |
| 3 `min(255, back+fg/4)` | `FUNC_ADD` | `ONE`, `ONE` | 0.25 |

`glBlendFunc` / `glBlendEquation` / `glEnable(GL_BLEND)` are only re-issued when the mode changes.

Setup also disables `GL_DITHER` (the 8-bit conversion would no longer be a plain rounding, and the
modulation result must be exact), `GL_SCISSOR_TEST` (it clips to the window rectangle, not to the
target), `GL_DEPTH_TEST` and `GL_CULL_FACE` (the PS1 has neither, and triangle winding from the
packet stream is not consistent).

## Textures

`VramTextureCache` decodes each `(tpage, clut)` pair once into 256x256 RGBA8: RGB from
`Vram.Abgr1555ToRgb`, alpha 0 for raw texel `0x0000`, 128 when `0x8000` (STP) is set, 255
otherwise. Upload through `glTexImage2D` with `GL_NEAREST`, `GL_CLAMP_TO_EDGE`, no mipmaps.
No dependent reads and no CLUT lookup happen in the fragment shader. A primitive whose texture was
never uploaded draws with a built-in 1x1 white texture instead of throwing.

## Error paths

The renderer throws on setup and frame errors:

- Shader compile and program link failures throw with the driver's info log; a link failure also
  carries the `glGetError` value, because some drivers report the reason only there.
- A missing uniform (`glGetUniformLocation` returning -1) throws and names the uniform instead of
  silently rendering black.
- Every texture upload checks `glGetError` and names the `(tpage, clut)` pair.
- Vertex buffer growth, vertex uploads, and the completed draw batch check `glGetError` before a
  frame is counted as submitted.

## Deliberate deviations from PS1 hardware

- **No mask bit / no draw-to-VRAM / no texture window.** These need framebuffer and VRAM feedback
  the GLES2 path does not model; the cache guarantees texels come from a static decode.
- **Coverage at exact edge hits.** A fragment whose sample point lies exactly on a triangle edge is
  filled by GL's fill rule and by `Tri`'s `w >= 0` test only if both agree; on the probe scene 81 of
  76,800 pixels disagree this way. This is a lattice coincidence, not an ordering error.
- **Rounding.** `Tri` truncates the modulated colour (`(int)`), the GPU's 8-bit attachment rounds,
  and ABR0 blends with `SRC_ALPHA = 128/255` where the hardware uses an exact 0.5. Result: ±1 LSB on
  blended pixels, ±2 where two such roundings stack. Measured in [Verification](#verification).
- **No dithering**, no 24-bit output path, no per-vertex fog.
- **No Z-buffer and no depth test** — order is the caller's painter order, as in the packet stream.
- **UV range** is clamped to the 256x256 page instead of PS1's modulo-256 wrapping; with UVs
  in 0..255 the results are identical, and interpolation outside the page clamps.
- **No framebuffer alpha semantics**: the destination alpha is never read, only the fragment alpha.
- **Semi-transparent primitives cost two draw calls** and never merge with neighbours of the other
  pass, by design: per-primitive order must not change.

## Verification

`tools/FcGpuProbe` is a host-only diagnostic (net10.0, Linux). It creates a GL ES 2.0 context
through EGL without a window system — `eglGetPlatformDisplay(EGL_PLATFORM_SURFACELESS_MESA)`, then
`eglGetPlatformDisplay(EGL_PLATFORM_DEVICE_EXT)` and `eglGetDisplay` with a pbuffer as fallbacks —
loads `libGLESv2.so.2` directly, and drives the real `GlEs2Renderer` through it. Nothing in this
tool ships to the device; it exists to prove `Ps1Shaders` and the state machine on a real driver.
It is deliberately not referenced by `scripts/build-profiles.sh`, so it never ends up in
`dist/linux` or `dist/tizen`.

```sh
cd tools/FcGpuProbe
dotnet run -- all --out /tmp/probe               # hardware GL, ES3.2 context
dotnet run -- all --out /tmp/probe --gles2       # force a GL ES 2.0 context
dotnet run -- all --out /tmp/probe --software    # force llvmpipe
dotnet run -- abr3 --software --gles2           # check saturated foreground against the CPU
```

Modes: `shaders` (triangle + textured quad, shader logs, `glGetError` after every stage),
`ps1` (the fixed primitive list through `GlEs2Renderer`), `ref` (the `Tri` rules on the same
primitives), `diff`, `uvmap` (texture-coordinate probe), `abr3` (saturated modulation pixel), `all`.

Each `Render` call is one frame and therefore includes the backdrop quad, so the probe draws the
whole scene in a single call. The reported `draws` count includes that one extra draw call; the
pixel comparison below was re-measured afterwards and is unchanged, because the default backdrop is
opaque black and the reference clears to black as well.

The scene is deliberately small and deterministic: a 256x256 15-bit page in fake VRAM holding
gradient, 4-texel checker, a raw `0x0000` band and a raw `0x8000` block, drawn by five triangles —
opaque modulated, semi ABR0, semi ABR2 (subtract), semi ABR1 (add), semi ABR3 (quarter add).

### Measured deviation (320x240 = 76,800 pixels)

Intel Arc via Mesa 26.2.6, `GL_VERSION: OpenGL ES 2.0 Mesa 26.2.6 - kisak-mesa PPA`, GLSL ES 1.0.16.
llvmpipe gives 14,937 / 9,174 / 85 for the same three rows, i.e. the counts move by a few hundred
pixels because the two drivers disagree on 344 pixels of the frame themselves (see
[Cross-implementation check](#cross-implementation-check)).

| comparison | differing | mean abs delta | > 1 LSB | > 2 LSB |
| --- | --- | --- | --- | --- |
| GPU vs `Tri` exactly (truncating) | 15,058 (19.6 %) | 0.195 | 876 | 86 |
| GPU vs `Tri` with rounded modulation | 8,851 (11.5 %) | 0.135 | 772 | 86 |
| GPU delayed by half a pixel vs reference | 16,531 (21.5 %) | 4.516 | 5,285 | 4,648 |

The third row is the control: without the `+ vec2(0.5, 0.5)` in the vertex shader the two paths
disagree six times as much and 54 times as often by more than 2 LSB, so the sample-point fix is
what makes the alignment correct rather than accidental.

Bit-identical pixels, split by the primitive that covers them (rounded reference):

| region | identical | ±1 LSB | ±2 LSB | > 2 LSB |
| --- | --- | --- | --- | --- |
| opaque modulated triangle | 56,626 | 17 | 0 | 8 |
| semi ABR0 (half) | 5,085 | 1,543 | 0 | 13 |
| semi ABR2 (subtract) | 4,185 | 5,850 | 672 | 9 |
| semi ABR1 (add) | 1,687 | 1 | 0 | 33 |
| semi ABR3 (quarter add) | 366 | 668 | 14 | 23 |

88.5 % of all pixels are bit-identical. The opaque path is exact to ±1 (17 pixels, all at texture
edges); every remaining difference is inside a blended region, which is where the fixed-function
blend cannot reproduce `Tri`'s integer arithmetic: `Tri` computes an integer foreground and then
blends integers, GL blends floats and rounds once when writing the attachment. ABR0 additionally
blends with `128/255` where the hardware uses 0.5, which is why it carries the most ±1 pixels.

The 86 pixels above 2 LSB are all border effects: 81 lie exactly on a triangle edge (fill-rule
versus `w >= 0`), and 5 are isolated pixels whose colour is identical to that of an immediate
neighbour — a one-texel flip in the sample, four of them where the interpolated texel coordinate is
exactly a half texel (a `Math.Round` half-to-even tie in the reference versus `floor(c + 0.5)` in
fp32 on the GPU). Because the synthetic page contains a raw `0x0000` band, such a flip shows up as
"discard versus coloured texel" rather than as a one-LSB shift, which is why those five look large.
No difference remains that points at a formula error — all four ABR modes, the two-pass semi order,
the `0x0000` and STP rules and the modulation curve are reproduced.

### Cross-implementation check

The same build was run against two drivers in the same GL ES 2.0 context. llvmpipe and the Intel
GPU disagree on 344 of 76,800 pixels, 343 of them by 1 LSB and one by 4 — i.e. the renderer is
deterministic up to the drivers' own interpolation, and nothing in the output depends on driver
specifics. The one exception is the mediump experiment quoted above, which is why the varyings are
declared `highp`.

What this does **not** prove: the ARMv7/Tizen behaviour. llvmpipe and Intel both use 32-bit floats
for `mediump` in the paths measured here, so the softfp ABI, the tile GPU's precision behaviour and
the TV's framebuffer format remain untested on this host.
