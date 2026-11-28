# Missing files must be reported

## Problem

When the disc archive is missing, the menu silently shows placeholder texts (`Weapon: id 0x41`) and pretends everything is fine. The actual start then fails later without a clear message.

## Evidence

- `tools/FcMainMenu/MenuLabels.cs:28` writes `Menu text unavailable: ...` to the error stream only, visible in the terminal.
- `tools/FcMainMenu/MenuFlow.cs:294` reports `Cannot load mission ...` there as well.
- Reproduced: without `build/` exactly these placeholder texts appear in the window while the message only lands in the log.

## Task

Surface missing or unreadable files inside the game window, not only in the log. The notice must name the missing file and how to generate it (`python3 scripts/fc_extract.py ... build/disc/files`). No silent fallback to placeholder texts without a notice.

## Acceptance

- Without the archive the window shows an understandable message including the file path.
- With the archive no message appears.
- A test covers the case without the archive.

## Done

Already fixed with 5461592 and only re-verified here: `MenuLabels.Load` fills
`MenuLabels.UnavailablePath`, `MenuFlow.LastLoadErrorPath` the mission page, and
`X11PanelLayout.FileNotice`/`Window.DrawFileNotice` draw the path and the extraction
command (`python3 scripts/fc_extract.py "<disc>.bin" build/disc/files`) in the window.

Tests: `X11MenuPanelTests.UnavailableFrontEndArchiveProducesVisibleExtractionNotice` (missing and
unreadable), `X11MenuPanelTests` with an existing archive (empty notice) and
`MenuFlowTests` for the mission path.
