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