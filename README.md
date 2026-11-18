# future_cop_dotnet

Future Cop: L.A.P.D. (PS1) reimplemented in .NET. Target platforms: desktop Linux
(development) and Samsung Tizen TV (deployment via the Arm32ManualMapper / DALi NUI route).

## Layout

- `src/FutureCop.Assets/` - reader for the retail mission archives (`.MIS`): `Cobj`
  model resources, polygon lists first, then vertices/normals/widths/textures/hierarchy.
- `tools/FcAssetTool/` - CLI to list and dump models from a `.MIS` file.
- `build/disc/files/` - **gitignored** local copy of the extracted disc tree
  (from the decomp project's `tools/extract_mode2.py` pipeline). Retail data, read-only.

No original decompiled C sources or retail assets are committed to this repository.
Format knowledge is documented here as behaviour, with decomp function addresses as
references only.

## Asset format (verified empirically + against decomp references)

All tags are u32 little-endian whose byte reversal spells the ASCII name
(`Cobj`, `CTRL`, `4DGI`, `3DQL`, ...). Chunks are `{tag u32, length u32 (incl. 12-byte
header), index u32, data[length-12]}` (decomp ref: `func_80012E48`).

- `.MIS` top level starts with a `CTRL` chunk whose data holds three section offsets.
- Model records carry tag `Cobj`, a 56-byte record header (`id` at +4, size word at +8),
  then the model chunk group: `4DGI` header, `3DTL` textures, `3DQL` polygons,
  `3DRF` relocation blocks, `4DVL` vertex lists per frame, `4DNL` normals, `3DRL` widths,
  `3DHY` hierarchy, `3DBB` bounds, animation chunks. The group ends at zero padding
  (2048-byte aligned); trailing `SHOC` page blocks are not model geometry.
- `3DQL`: s32 count, then 12-byte polygon records. On-disc flag byte 0: bits 0-2 vertex
  count, bit 3 semi-transparent, bits 4-6 shading mode, bit 7 variant; byte 1: low nibble
  primitive type (3 tri, 4 quad, 5/6 billboards, 7 tapered line, 8 disc), bits 4-6 part
  group, bit 7 extra pass. The retail loader (`func_800131A0`) rewrites these flags at
  load time; this reader exposes the on-disc form plus the decoded fields.
- `4DVL`/`4DNL`/`3DRL`: indexed per animation frame (`index` = frame + 1); data starts
  with an s32 count; vertices are 8-byte `{s16 x, y, z, pad}` records.

## Build & run

```bash
dotnet run --project tools/FcAssetTool -- list build/disc/files/CW/M1A1.MIS
dotnet run --project tools/FcAssetTool -- dump build/disc/files/CW/M1A1.MIS 11
```

## Tests (identical cases in .NET and C)

Shared golden vectors: `tests/golden/3dtl.vectors` (line form `fcop-3dtl-golden/v2`,
schema documented in the file header). Both suites assert the same cases:

```bash
dotnet test tests/FutureCop.Assets.Tests          # .NET xUnit, 61 tests
bash ../future-cop-lapd-ps1/tests/host/build.sh   # C harness vs ORIGINAL decomp C
```

The C harness lives in the decomp repo (`tests/host/`): it compiles the unmodified
decomp functions `800131A0`, `80012E48`, `8001306C`, `80012E1C`, `80063C5C` with
that repo's PS1 cross-gcc (`.toolchain/gcc-2.7.2-cdk` + maspsx + mipsel-binutils)
and runs the freestanding MIPS binary under a user-local qemu-mipsel
(`~/.local/share/qemu-user/`, extracted from Ubuntu `qemu-user` deb; no root
needed). The golden vectors stay in this repo as the shared fixture; the harness
reads them via `VECTORS` (default: sibling checkout of this repo). It feeds
synthetic `Cobj` wrappers around each vector body through the real loader and
compares output hex plus every decoded record field.

A case claims parity only where both sides can measure it: `CASE <id> ok` means
C and .NET assert the same `OUT`/`REC` lines, `c_skip` marks retail behaviour that
is undefined (the C harness skips it), and `c_hang` marks the retail infinite loop
proven by fork+alarm in C. In the two non-parity modes the .NET expectations live
in `NET`/`NET_REC` lines, and the C harness rejects `OUT`/`REC` there, so no
unmeasured parity can slip through. The harness build is test-only (own flags, own
output dir `tests/host/build/`): the production matching build is untouched and
still reproduces retail bytecode byte-exactly (subset verify 7/7 PASS).

`tests/golden/compose.vectors` (`fcop-compose-golden/v2`) and
`tests/golden/joints.vectors` (`fcop-joints-golden/v2`) drive the bone compose
(`func_80013CB8`, `func_8001006C`) and the joint chain (`func_80052960`). Each row
carries the complete input - `TABLE` directive, parent/root matrix, entry or record
fields, offset - next to the expected matrices, so the .NET suite and the C
harnesses (`compose_test.sh`, `joints_test.sh`) execute identical cases instead of
re-deriving inputs in code. Their GTE leaf primitives are software models
(`gte_ref.c` on the C side, `Gte.cs` in .NET), so those two files prove
model-against-model parity, not hardware-GTE parity. The joints fixture contains a
chain whose fields are all negative, which only stays inside the table with a
sign-extended `s16` index (`LocalMatrix(..., signedIndices: true)`).

`tests/golden/render.vectors` (`fcop-render-golden/v1`) adds the derived render
path: GTE matrix load / mvmva / rtps+clip (asm units `80010750`, `8001010C`,
`8001047C`, mirrored in `src/FutureCop.Assets/Gte.cs` and the harness'
`gte_ref.c`), winding `80015AF4`, part prep `80015B68`, masked walk `80016A64`
and the loader flag rewrite `800131A0:136-155`. The pure-C originals run
unmodified in the harness; the GTE asm is mirrored identically in both
languages (rounded UNR-equivalent quotient, GTE saturation/FLAG bits, clip
byte semantics incl. "clip 1 = point not written"). `work.xy3` is don't-care
for triangles: retail leaves the walk's stack work block stale there.

## Wireframe viewer

```bash
dotnet run --project tools/FcModelViewer -- build/disc/files/CW/M1A1.MIS 30 out.bmp
```

Reads one `Cobj` model and draws the masked polygon walk as a turning wireframe in
an X11 window (320x240 framebuffer, 2x scale; q/Esc closes). Left-click selects
the previous model and right-click the next model. The model turns about
its own Y axis, pivoting on the bounding-box centre and fitted to the frame, so it
stays centred instead of swinging out of view.

Two draw paths, selected by the model itself:

- **Static** (bit 25 of the 4DGI flag word clear): one vertex list, projected
  through the single-list path of `func_80013F08`
  (`ModelRender.Project`).
- **Scripted / bones** (bit 25 set): the 3DHY entries are posed by
  `func_80013CB8` - per entry a local rotation (`func_80018DC0`), translation
  either inline or sampled from the 3DMI table, composed onto the parent slot of
  the matrix stack - and each entry projects its own vertex range
  (`ScriptRender`). Models that carry bit 25 but no 3DHY chunk have no bone data
  on the disc and fall back to the static list.

The current frame is also written as a 24-bit BMP for headless capture. No
textures, no shading. To preview the experimental player assembly:

```bash
FC_VIEW_ASSEMBLY=1 dotnet run --project tools/FcModelViewer -- build/disc/files/CW/M1A1.MIS 11 out.bmp
```

This is a work-in-progress pose, not a verified reproduction: the original C/.NET
parity tests are incomplete. In the assembly mode the left arrow key transforms
the mech into the floating (**hover**) form and the right button back into the
standing (**walker**) one. The animation is the retail transformation:
`func_80055988` moves the progress `state+0xBC` by `step * 0xA0` per tick, both
animated instances scrub their type-3 clip (`func_80044B34`:
`frame = start + ((end - start) * speed >> 16)` - clip `0xC` on the main model and
clip `2` on the companion), `func_800597C0` selects the group masks
(`0x03/0x05` walker, `0x07/0x0F` mid-transition, `0x05/0x0B` hover) and lifts the
body by `(progress << 12) >> 16`. `PlayerForm` holds that state; the shared scene
advances it from its own clock, so the TV frontend animates it without extra input
code. Progress and masks are read from the decomp, not yet covered by the C
harness (only the clip scrub is: `tests/golden/mech-form.vectors`).

Environment knobs:

- `FC_VIEW_DEG_SEC=<d>` - turn rate in degrees per second (default 20).
- `FC_VIEW_NOWINDOW=1` - no window, BMP only.
- `FC_VIEW_FRAMES=<n>` - render one full turn as `out-000.bmp` .. `out-(n-1).bmp`
  instead of opening a window (for GIFs or frame diffs).
- `FC_VIEW_FRAME=<n>` - animation frame fed to the 3DMI sampling of the bone path
  (default 0). Parts driven by the animation curve move with it.
- `FC_VIEW_STATIC=1` - force the rigid single-list path, to compare a posed model
  against its rest geometry.

### Projectiles (the retail fire path)

The assembly mode can fire the weapon definitions the mission ships, and the
projectiles are drawn as flying models:

```
left / right arrow                              walker <-> hover form
up / down arrow, mouse wheel                    cycle the fire definitions
space / return / middle mouse button            fire one projectile
```

The data path is traced and documented in `docs/weapon-fire-path.md`: the
definitions are the `tACT` records whose following `aRSL` list names the
projectile `Cobj` (`func_8006A918`, looked up by `func_8006AAEC`), the spawn block
is `func_8005E47C`, the muzzle is vertex 0 of the two alternating instances
(`func_80017668`, `state+0x178`), the range is `arc >> 4` (`func_80035904`) and the
position step is the retail integrator `func_8001ACA4` with the mover's per-tick speed
ramp (`func_80035B7C:435-448`), and the projectile is oriented with the retail flight basis
`func_800193F8` rebuilt from its velocity every tick, so the nose points where it flies. `M1A1.MIS` ships eight
definitions with two triggers each (models 59, 60, 62, 64, 65, 66, 68), so
up/down steps through sixteen shots.

Ported with C parity: `WeaponCatalog` (definition and resource reader),
`ShotSpawn.Compose` (`tests/golden/shot.vectors`, 7 cases against the unchanged
`func_8005E47C`) and `ShotMotion.Integrate` (`tests/golden/shot-move.vectors`,
8 cases against `func_8001ACA4`). Not ported: the pursuit logic of the mover,
the lob solver, the beam paths and collision - see the open points in the
document.

Knobs of the fire path:

- `FC_VIEW_SHOT_SPEED=<f>` - multiplier on the retail step (default 0.0625: at
  retail speed a 4Da shot crosses its whole leash radius in eight ticks, and the
  slower flight keeps it readable). 1 reproduces the mover exactly.
- `FC_VIEW_SHOT_AIM=<deg>` - aim in model space (0 = at the viewer, 90 = outward;
  default 90, mirrored per arm - a viewer convention, retail shares one aim vector).
- `FC_VIEW_SHOT_SCALE=<f>` - draw scale of the projectile model (default 1, the
  retail basis, i.e. no scaling).
- `FC_VIEW_SHOTS=<n>` - headless still: fire `n` projectiles one interval apart
  and write the frame with all of them in flight.

Camera knobs (the viewer looks down on the mech at an angle so the flight path stays
readable):

- `FC_VIEW_CAM_ELEV=<deg>` - elevation above the horizon, 0 = level view (default 35).
- `FC_VIEW_CAM_DIST=<m>` - camera distance in metres, 0 = automatic framing (default
  30). Metres are anchored on `WireframeScene.ModelUnitsPerMetre`; the projection cannot
  carry a distance beyond `WireframeScene.MaxDistance`, because the GTE emulation keeps the
  rotated vertex in a 16-bit register and drops every face of a model viewed from further
  out.

## .NET version

All projects target `net10.0` (`dotnet --list-sdks` needs a 10.x SDK; the
`~/.dotnet` install works via `~/.dotnet/dotnet`). `LangVersion` stays pinned to
10 so the source keeps its existing style.

## Build profiles (Linux desktop / Samsung Tizen TV)

The renderer lives in `src/FutureCop.View` and is shared by both frontends, so the
projection, walk and framing are identical everywhere:

```
src/FutureCop.View/        WireframeScene: MIS -> RGB framebuffer, subject selection
tools/FcModelViewer/       Linux profile: X11 window, wheel and keys, BMP capture
tools/FcModelViewer.Tizen/ TV profile: Tizen NUI window, remote-control keys
```

```bash
scripts/build-profiles.sh linux    # -> dist/linux
scripts/build-profiles.sh tizen    # -> dist/tizen  (needs tizen/framework)
scripts/build-profiles.sh all
```

The Tizen profile compiles against the TV's own signed Tizen assemblies; they are
fetched once from the device and stay out of git:

```bash
scripts/fetch-tizen-refs.sh        # fills tizen/framework/ from $B/sdk_tools
```

Refactoring the viewer must stay pixel-identical. Check it against a build of the
previous commit with `FC_VIEW_NOWINDOW=1 FC_VIEW_FRAMES=8` and `md5sum` on the
numbered BMPs (static and `FC_VIEW_ASSEMBLY=1` paths).

### On the TV (GU43U7099F, Tizen 9, armv7l)

The TV runs .NET 6 natively, so the app is hosted by the local `Arm32ManualMapper`
(.NET 10 in anonymous memory) and started through the persistent root agent:

```bash
TVAGENT=... ./scripts/deploy-tizen.sh deploy   # push dlls + M1A1.MIS, copy Tizen deps
TVAGENT=... ./scripts/deploy-tizen.sh start    # default: FC_VIEW_ASSEMBLY=1 FC_VIEW_TEXTURED=1
./scripts/deploy-tizen.sh log                  # /tmp/fcview.log + /tmp/fcview-app.log
./scripts/deploy-tizen.sh shot out.png         # DRM plane readback
./scripts/deploy-tizen.sh stop
```

Remote control (window keeps focus): in the assembly mode left/right transform the mech
into the hover form and back into the walker (`FC_VIEW_TRANSFORM`, 1 = retail ~6.8 s,
default 4), up/down step through the mission's models, OK pauses/resumes the turn, back
closes. A form change keeps the current turn angle and the camera, which is framed once on
the union of the poses the transition passes through, so nothing jumps or rescales.
`FC_VIEW_TEXTURED=0` falls back to the wireframe, `FC_VIEW_PROBE=label|scene|full`
and `FC_VIEW_SELFTEST=1` are bring-up diagnostics.

Presentation note: `NativeImageSource`/`ImageView` stays black under the mapper and a
`TextLabel` deadlocks in its constructor, so the TV frontend hands each frame to DALi
as a rotating ring of BMP files (`FC_VIEW_RING`, default 6) and there is no HUD yet.
Measured on the device: ~30 fps timer ticks with textured polygons, 320x240 raster
scaled up by the ImageView.

The raster stays at the retail 320x240 on every profile: the clip byte built inside
`Gte.Rtps` compares against the fixed PS1 screen constants (`0x140`/`0x80` width mode
and `0xF0` height), so a taller framebuffer silently drops the bottom-most polygons
(at 480x360 the mech's feet disappeared while the face count fell from 79 to 68).
Scaling up is the presenter's job, not the rasterizer's.

## Texture pipeline

Each `.MIS` mission archive carries 10 streamed `Cbmp` texture resources (`id` 1..10)
reassembled from `SHOC`/`SDAT` packet chains (`MisFile.ReassembleStreamedPayload`).
`CbmpContainer` walks each payload (`func_8001996C`: `CCB ` and `PLUT` skip the 4-byte
index word; `PDAT`/`PIX4` start at `+8`), and `TextureBank` uploads the 128×252 halfword
image and 256×1 ABGR1555 palette into a 1024×512 `Vram` (`func_80063C5C`).

| Slot `id` (`s0 = id-1`) | Image rect `(x, y, w×h)` | CLUT rect `(x, y, w×h)` | `tpage` | `clut` |
| --- | --- | --- | --- | --- |
| 1..2 (`s0=0..1`) | `(384, 0/256, 128×252)` | `(384, 252..253, 256×1)` | `0x0086`/`0x0096` | `0x3F18`/`0x3F58` |
| 3..4 (`s0=2..3`) | `(512, 0/256, 128×252)` | `(384, 254..255, 256×1)` | `0x0088`/`0x0098` | `0x3F98`/`0x3FD8` |
| 5..6 (`s0=4..5`) | `(640, 0/256, 128×252)` | `(640, 252..253, 256×1)` | `0x008A`/`0x009A` | `0x3F28`/`0x3F68` |
| 7..8 (`s0=6..7`) | `(768, 0/256, 128×252)` | `(640, 254..255, 256×1)` | `0x008C`/`0x009C` | `0x3FA8`/`0x3FE8` |
| 9..10 (`s0=8..9`) | `(896, 0/256, 128×252)` | `(384, 508..509, 256×1)` | `0x008E`/`0x009E` | `0x7F18`/`0x7F58` |

`WireframeScene` (`FC_VIEW_TEXTURED=1` or `textured` flag in `FcModelViewer`) sorts
accepted faces back-to-front by `Work.Z` and rasterizes affine-mapped triangles into
the 320×240 RGB888 buffer (nearest-neighbour sampling, `0x0000` colour-key transparency,
`(255, 0, 255)` marker for missing pages). Deviations from PS1 hardware: 24-bit RGB888
output without 4×4 dithering, `(c5 << 3) | (c5 >> 2)` 5-to-8-bit expansion, and no
Gouraud/lighting modulation or semi-transparency blending yet.

Verified against `M1A1.MIS` and `tests/golden/cbmp.vectors`: all 10 `Cbmp` resources
reassemble and upload, mech model 11's `tpage=0x008E`/`clut=0x7F18` resolves to `(896, 0)`
/ `(384, 508)` with non-zero texels, and UV byte pairs `(u0,v0,u1,v1)` + `(u2,v2,u3,v3)`
in `3DTL` follow polygon vertex order `V0..V3`. Hypothesis / unverified in this build:
`PIX4` 4-bit containers (none present in `M1A1.MIS`), Gouraud vertex-colour modulation,
and `type 3` semi-transparent blending.

```bash
dotnet run --project tools/FcAssetTool -- textures build/disc/files/CW/M1A1.MIS /tmp/fc_pages
FC_VIEW_TEXTURED=1 dotnet run --project tools/FcModelViewer -- build/disc/files/CW/M1A1.MIS 11 /tmp/mech_textured.bmp
```
