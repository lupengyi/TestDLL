# Instrument Center Design QA

- Source visual truth:
  - `C:\Users\2501000857\AppData\Local\Temp\codex-clipboard-be29db42-c17a-490a-8af0-f2453ea0f7e4.png`
  - `C:\Users\2501000857\AppData\Local\Temp\codex-clipboard-d0fbe851-47ce-402b-9a28-ce65fc26b5dd.png`
- Implementation screenshots:
  - `E:\FST\TestDLL\ManualCanDebug\ManualCanDebug\bin\CodexVerify\Debug\InstrumentWorkspaceQA\single-project.png`
  - `E:\FST\TestDLL\ManualCanDebug\ManualCanDebug\bin\CodexVerify\Debug\InstrumentWorkspaceQA\single-station-final.png`
- Source pixels: 1920 x 1080 for each screen.
- Implementation pixels and viewport: 1600 x 900 at 96 DPI for each screen.
- Normalization: both are 16:9 desktop captures at density 1; implementation was compared proportionally at 83.33% of source dimensions.
- States:
  - Project instrument screen with DMM selected and generated methods visible.
  - Current-project station screen with the single station selected.

## Full-view comparison evidence

- Project screen preserves the source's two-column shared/template hierarchy, top discovery summary, bottom definition/method regions, blue primary actions, light borders, and dense Windows engineering-tool spacing.
- Station screen adapts the source hierarchy to the actual current project: one station, real `InstrumentConfig.json` devices, shared PLC/LVDC bindings, independent instrument tags, selected-station outline, bottom editor, and copy/conflict/save actions. Increasing the station-count selector expands the same layout.
- Live discovered counts intentionally replace mock values: 22 drivers and 418 public methods instead of 13 and 86.

## Focused-region evidence

Separate crops were not needed because the original-resolution implementation captures keep the smallest method-table text, station tags, resource dropdowns, and wiring endpoints legible. The bottom method region and the station 03 editor were inspected at original resolution.

## Required fidelity surfaces

- Fonts and typography: Microsoft YaHei UI / existing Studio theme, compact 11-15 px hierarchy, semibold section titles, and truncation on long driver names match the established application. No unreadable wrapping remains.
- Spacing and layout rhythm: major region ratios, 12 px section gaps, thin borders, compact rows, 2 x 3 station grid, and fixed bottom editor follow the references. Persistent actions remain visible at 1600 x 900.
- Colors and visual tokens: white base, pale blue selected state, #1F6FE8 primary actions, blue power wiring, green PLC wiring, gray-blue separators, and dark neutral text match the source direction.
- Image and asset fidelity: the references contain no photographic or illustrative assets. Standard application controls are used; no raster placeholders or decorative replacement assets were introduced.
- Copy and content: terminology uses “分配仪器”, “选择要生成的方法”, “已生成方法”, and “生成方法”; no “添加DLL”, “暴露”, or “包装” language appears in the new screens.

## Comparison history

### Iteration 1

- P1: generated-method table columns collapsed and became unreadable.
- P1: resource dropdowns displayed internal CLR type names.
- P1: station wiring was absent in the initial render.
- P2: several icon glyphs rendered as square placeholders.

Fixes:

- Assigned explicit method-table widths and removed row-header width.
- Replaced binding-only resource options with display-safe option objects.
- Deferred wiring until render and added deterministic orthogonal paths.
- Removed unsupported glyph text and retained plain-language actions.

Post-fix evidence: `project-2.png` and `station-final-3.png` show readable columns, named resources, visible wiring, and no placeholder glyphs.

### Iteration 2

- P2: the first wiring route placed vertical segments through station content.

Fix:

- Routed all vertical segments in the palette gutter and aligned horizontal segments with shared-resource chips; blue and green endpoints use separate offsets.

Post-fix evidence: `station-final-3.png` keeps independent-instrument content unobstructed while shared-resource relationships remain visible.

### Iteration 3

- P1: the first implementation incorrectly treated the future six-station example as the current project default.
- P1: wiring was visual output only and did not provide an obvious way to move or disconnect an existing connection.

Fixes:

- Changed schema version 3 to initialize from the real `Config\InstrumentConfig.json` and default the current project to one station.
- Shared resources now come from actual PLC/LVDC definitions; all other actual instruments are assigned as station-01 independent instances.
- Shared chips are draggable endpoints, station cards highlight on drag-over, dropping moves/rebinds the connection, and right-click provides `取消连接`.

Post-fix evidence: `single-station-final.png` shows the real one-station topology and visible PLC/LVDC lines; workspace verification confirms schema version 3, one station, three current shared definitions, and ten independent definitions.

### Iteration 4

- P1: current-project ownership was corrected again: PLC is the only shared resource; LVDC and LVDC_KL15 are station-independent instruments.
- P1: clicking another instrument changed the editor but left the old row visually highlighted.

Fixes:

- Schema version 4 now derives the project with PLC as the sole shared definition and places both low-voltage supplies in station 01.
- Selection styling is updated immediately across both lists with one pale-blue row, a blue left indicator, and cleared styling on all other rows.

Post-fix evidence: `E:\FST\TestDLL\ManualCanDebug\ManualCanDebug\bin\CodexVerify\Debug\InstrumentWorkspaceQA\selected-hvdc.png` shows HVDC as the only highlighted row while the editor also displays HVDC. Verification confirms version 4, one station, and shared resource `PLC` only.

## Follow-up polish

- P3: isolated QA captures include the retained compatibility and live-control tabs, which are not shown in the conceptual mock but preserve existing product functionality.
- P3: real driver names are longer than mock labels and therefore truncate in narrow dropdowns; full values remain selectable.

## Implementation checklist

- [x] Automatic DLL and public-method discovery.
- [x] Shared instrument and independent template definition.
- [x] Persistent connection parameters and method selections.
- [x] Generated MainTest public methods and SEQ action definitions.
- [x] Six-station drag/drop allocation and station editing.
- [x] Shared power-channel and PLC DB-offset binding.
- [x] Copy, remove, save, and conflict-check behavior.
- [x] 1-12 station-count support with scrolling.
- [x] Project, runtime, and UI regression verification.

final result: passed
