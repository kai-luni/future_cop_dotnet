# Second Level (`SLIM.MIS`) Fix & Verification Guide

## Summary of Findings & Changes
- **Mapper (`tools/Arm32ManualMapper/`)**: Replaced the diagnostic `FCMAP_PHDR_SHIM=1` shim with an unconditional, pure-native `dl_iterate_phdr` hook (`EmitDlIteratePhdr`) using an explicit 40-byte `Arm32DlPhdrInfo` wire struct, full original ELF program headers (`PT_PHDR`, `PT_LOAD`, `PT_DYNAMIC`, `PT_TLS`, `PT_GNU_EH_FRAME`, `PT_ARM_EXIDX`), transactional pre-publish staging and capacity checks in `HookedDlOpen`, per-module `InitLock` outside `_moduleLock`, lock-free `_moduleSnapshot` reads in `HookedDlAddr`/`HookedUnwindFindExidx`, and reverse-P/Invoke exception guards.
- **Game**:
  - `tools/FcMainMenu/MenuFlow.cs`: added `LastLoadError` (set to `Cannot load <FILE>.MIS.` when `StartScene` catches `IOException` / `InvalidDataException` / `UnauthorizedAccessException`, cleared on menu navigation or scene entry) while preserving the `Cannot load mission ...` error log and Loadout fallback.
  - `tools/FcMainMenu.Tizen/TvLevelControl.cs`: added testable `LoadoutStatus` + `RecordGraphicsFault()` so a newer mission load error on retry replaces any older OpenGL ES graphics fault instead of being masked by it.
  - `tools/FcMainMenu.Tizen/Program.cs`: `MenuApp` uses `control.RecordGraphicsFault()` and passes `control.LoadoutStatus` to `BitmapPanel.Render`.
  - Preserved the GPU game path (`FcMainMenu.Tizen`), CPU reference (`FcLevelTv`), and old TV viewer (`FcModelViewer.Tizen` + `scripts/deploy-tizen.sh` / `scripts/build-profiles.sh`).

## Host Verification Commands

Run game commands from the repository root. The mapper lives in `tools/Arm32ManualMapper/`.
1. **Build modified projects**:
   ```bash
   ~/.dotnet/dotnet build tools/Arm32ManualMapper/Arm32ManualMapper.csproj -c Release
   ~/.dotnet/dotnet build tests/FutureCop.Assets.Tests/FutureCop.Assets.Tests.csproj -c Release
   ~/.dotnet/dotnet build tests/FutureCop.Gpu.Tests/FutureCop.Gpu.Tests.csproj -c Release
   ```
   To also build the GPU game Tizen executable (`FcMainMenu.Tizen.dll`, distinct from `FcModelViewer.Tizen.dll` in `deploy-tizen.sh`), ensure `tizen/framework/*.dll` is present (or pass `-p:TizenFrameworkDir=...`):
   ```bash
   ~/.dotnet/dotnet build tools/FcMainMenu.Tizen/FcMainMenu.Tizen.csproj -c Release
   ```

2. **Mapper host tests (platform-independent, safe on Linux x64)**:
   ```bash
   ~/.dotnet/dotnet test tools/Arm32ManualMapper/tests/Arm32ManualMapper.Tests/Arm32ManualMapper.Tests.csproj --filter "Category!=Arm32Device"
   ```

3. **Game unit & regression tests (no retail assets required)**:
   ```bash
   ~/.dotnet/dotnet test tests/FutureCop.Assets.Tests/FutureCop.Assets.Tests.csproj --filter "FullyQualifiedName~MenuFlowTests|FullyQualifiedName~TvLevelControlTests"
   ~/.dotnet/dotnet test tests/FutureCop.Gpu.Tests/FutureCop.Gpu.Tests.csproj --filter "FullyQualifiedName~GpuLevelSessionTests"
   ```

4. **Full game test suites with retail assets** (required fixtures depend on each test; with `FC_REQUIRE_RETAIL=1`, required missing fixtures fail their corresponding tests):
   ```bash
   FC_REQUIRE_RETAIL=1 ~/.dotnet/dotnet test tests/FutureCop.Assets.Tests/FutureCop.Assets.Tests.csproj
   FC_REQUIRE_RETAIL=1 ~/.dotnet/dotnet test tests/FutureCop.Gpu.Tests/FutureCop.Gpu.Tests.csproj
   ```

## TV Verification Notes (`FcMainMenu.Tizen` GPU Game)
- Note: In a short isolated device run on 2026-11-20, the missing-file error was logged without a crash; with the file present, SLIM started and submitted GL frames. On-screen error text, visible gameplay, controls, returning to CONFT after the error, and long-term stability were not verified.
- Note: `scripts/deploy-tizen.sh` targets the old `FcModelViewer.Tizen` viewer (`$B/fcview/app` + `M1A1.MIS`), **not** the `FcMainMenu.Tizen` GPU game.
- Do not overwrite existing TV mapper binaries (`$B/net8-lab/Arm32ManualMapper.dll`) or existing retail asset targets in-place during verification; stage `Arm32ManualMapper.dll` v1.1.0 and `FcMainMenu.Tizen` (`assets/FE.MIS`, `assets/PA/CONFT.MIS`, `assets/menu-0..3.bmp`, plus `assets/PA/SLIM.MIS` for the present-file run) in isolated test directories.
- On ARM32 target, also run:
  ```bash
  ~/.dotnet/dotnet test tools/Arm32ManualMapper/tests/Arm32ManualMapper.Tests/Arm32ManualMapper.Tests.csproj --filter "Category=Arm32Device"
  ```
- Verify the GPU game route `CONFT -> Back -> Back -> Proving Ground -> Enter (Loadout) -> Enter (Start)` (two `Enter`s after zone selection: first enters `Loadout`, second starts the mission) both without `SLIM.MIS` (process stays alive, `Cannot load mission .../PA/SLIM.MIS` logged, `Cannot load SLIM.MIS.` shown, return to `CONFT.MIS` works) and with `SLIM.MIS` (`scene .../PA/SLIM.MIS: OpenGL ES 2.0`, GL frames render).
