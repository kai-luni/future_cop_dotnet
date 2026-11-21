# Arm32ManualMapper – Experimental `dl_iterate_phdr` Fix

## Root Cause
When `Arm32ManualMapper` maps `.NET 10` ARM32 native libraries (`libcoreclr.so`, `libclrjit.so`, etc.) into anonymous memory via `mmap`, those modules are absent from glibc's `link_map`. Without `dl_iterate_phdr` interposition, CoreCLR's `libunwind` cannot find `PT_ARM_EXIDX` for mapped frames (such as `IL_Throw`), falls back to the APCS `R11` frame-chain heuristic in `_ULarm_step+0x144`, and either aborts (`SIGABRT`) or segfaults (`SIGSEGV`, e.g. dereferencing UTF-16 path characters in `R11` such as `0x00360032` when `SLIM.MIS` is missing and `FileNotFoundException` is thrown).

## What Changed (vs. Diagnostic Shim)
- **`Arm32ManualMapper.csproj`**:
  - Added `<Version>1.1.0</Version>` (`[Loader] Arm32ManualMapper v1.1.0.0` logged at startup).
  - Excluded `tests/**` from the executable project.
- **`NativeHooks.cs`**:
  - Added `EmitDlIteratePhdr(IntPtr realIterate)` (unconditional pure-native ARM32 hook, no CoreCLR 6 re-entry on foreign threads) backed by an explicit 40-byte `Arm32DlPhdrInfo` wire layout (`uint` 32-bit target pointers, independent of host pointer width).
  - Uses a pre-allocated `Arm32DlPhdrInfo[128]` table and a live `*countPtr` read with `dmb ish` (`0xF57FF05B`).
  - Reports `dlpi_addr`, `dlpi_name`, full original `dlpi_phdr`/`dlpi_phnum`, local monotonic `dlpi_adds`, `dlpi_subs = 0`, and keeps `dlpi_tls_modid = 0` / `dlpi_tls_data = 0` so synthetic mapper TLS IDs (`0x4000+`) never leak to external glibc code.
  - Pre-allocates `EmitExidxFinder` (`MaxExidxEntries = 128`) and `EmitTlsGetAddr` (`maxModules = 128`) and checks capacity before dynamic module publication. The startup population path can still truncate entries above its capacity.
- **`Program.cs`**:
  - Extracted pure-managed `Arm32ElfLoader.ReadAndValidateProgramHeaders` to validate and copy **all** original `Elf32_Phdr` entries (`PT_PHDR`, `PT_LOAD`, `PT_DYNAMIC`, `PT_TLS`, `PT_GNU_EH_FRAME`, `PT_ARM_EXIDX`, etc.).
  - `InstallDlIteratePhdrHook()` runs unconditionally before relocation in `RunDotNet8App` (no `FCMAP_PHDR_SHIM` gate) without calling back into glibc `dl_iterate_phdr` during `dlopen`.
  - `HookedDlOpen` stages (`MapLibraryUnpublishedLocked` -> `RelocateAndProtect(..., false)` -> `EnsureCapacityForDynamicModuleLocked` -> `CommitDynamicModuleLocked`) under `_moduleLock`, cleans up unpublished allocations on any failure, and runs `EnsureModuleInitialized` **outside** `_moduleLock` under per-module `InitLock` (handling concurrent callers and same-thread cyclic `dlopen`).
  - Reverse-P/Invoke hooks `HookedDlOpen`, `HookedDlSym`, `HookedDlClose`, and `HookedDlAddr` catch managed exceptions. The managed TLS fallback does not provide the same guarantee.
  - `HookedDlSym` limits extra interposition strictly to `dl_iterate_phdr`.
  - `HookedDlAddr` and `HookedUnwindFindExidx` iterate an immutable volatile `_moduleSnapshot` array lock-free.

The dynamic-loading path is not production-verified: real ELF staging and lazy loading
still need ARM32 runtime tests. Per-module initialization locks can deadlock on cross-thread
cyclic initialization; symbol binding can see a module whose initializers are still running.
A failure after partial publication can leave a partially registered module reopenable.

## Versioning & Isolated Deployment for Verification
- `Arm32ManualMapper.csproj` sets `<Version>1.1.0</Version>`.
- Build:
  ```bash
  ~/.dotnet/dotnet build tools/Arm32ManualMapper/Arm32ManualMapper.csproj -c Release
  ```
- Output files: `tools/Arm32ManualMapper/bin/Release/net6.0/Arm32ManualMapper.dll`, `.runtimeconfig.json`, `.deps.json`.
- **Do not overwrite** existing TV targets (`$B/net8-lab/Arm32ManualMapper.dll`, `$B/fcview/app`, or existing retail assets) during validation. Stage the new mapper and the `FcMainMenu.Tizen` GPU game (`FcMainMenu.Tizen.dll` + dependencies + `assets/`) into a separate isolated test directory on the TV and launch `/usr/bin/dotnet ./Arm32ManualMapper.dll --run <coreclr10-dir> <isolated-gpu-game-dir>/FcMainMenu.Tizen.dll`.

## Verification Commands (Nova)
### Host Unit Tests (platform-independent, runnable on Linux x64 and ARM32)
```bash
~/.dotnet/dotnet test tools/Arm32ManualMapper/tests/Arm32ManualMapper.Tests/Arm32ManualMapper.Tests.csproj --filter "Category!=Arm32Device"
```

### ARM32 Runtime Integration Tests (must run on ARM32; fails explicitly on x64)
```bash
~/.dotnet/dotnet test tools/Arm32ManualMapper/tests/Arm32ManualMapper.Tests/Arm32ManualMapper.Tests.csproj --filter "Category=Arm32Device"
```

### TV Verification (GPU Game `FcMainMenu.Tizen`, isolated staging directory)

The following is a test procedure, not a claim that every step was verified. In the short
2026-11-20 run, the missing-file error was logged and the process stayed alive; with the
file present the second level started and submitted GL frames. Visible gameplay, controls,
returning to the first mission after the error, and long-term stability remain unverified.

1. **RED (original `Arm32ManualMapper`, `SLIM.MIS` absent)**: `CONFT -> Back -> Back -> Proving Ground -> Enter (Loadout) -> Enter (Start)` -> `SIGSEGV` at `libcoreclr.so+0x3c98d8`.
2. **GREEN 1 (`Arm32ManualMapper` v1.1.0, `SLIM.MIS` absent, no `FCMAP_PHDR_SHIM`)**: same route -> process stays alive, log records `Cannot load mission .../PA/SLIM.MIS`, Loadout displays `Cannot load SLIM.MIS.`, returning to `CONFT.MIS` succeeds.
3. **GREEN 2 (`Arm32ManualMapper` v1.1.0, `SLIM.MIS` present)**: same route -> `scene .../PA/SLIM.MIS: OpenGL ES 2.0`, GL frames render, controls work.
