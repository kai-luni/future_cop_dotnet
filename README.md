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

Shared golden vectors: `tests/golden/3dtl.vectors` (line form `fcop-3dtl-golden/v1`,
schema documented in the file header). Both suites assert the same cases:

```bash
dotnet test tests/FutureCop.Assets.Tests          # .NET xUnit, 47 tests
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
compares output hex plus every decoded record field; `unknown_type` cases assert
the retail infinite loop via fork+alarm; `index_out_of_range` is skipped in C
(retail reads past the table). The harness build is test-only (own flags, own
output dir `tests/host/build/`): the production matching build is untouched and
still reproduces retail bytecode byte-exactly (subset verify 7/7 PASS).

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
an X11 window (320x240 framebuffer, 2x scale; q/Esc closes). The model turns about
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
textures, no shading. Environment knobs:

- `FC_VIEW_DEG_SEC=<d>` - turn rate in degrees per second (default 20).
- `FC_VIEW_NOWINDOW=1` - no window, BMP only.
- `FC_VIEW_FRAMES=<n>` - render one full turn as `out-000.bmp` .. `out-(n-1).bmp`
  instead of opening a window (for GIFs or frame diffs).
- `FC_VIEW_FRAME=<n>` - animation frame fed to the 3DMI sampling of the bone path
  (default 0). Parts driven by the animation curve move with it.
- `FC_VIEW_STATIC=1` - force the rigid single-list path, to compare a posed model
  against its rest geometry.

## .NET version

All projects target `net10.0` (`dotnet --list-sdks` needs a 10.x SDK; the
`~/.dotnet` install works via `~/.dotnet/dotnet`). `LangVersion` stays pinned to
10 so the source keeps its existing style.