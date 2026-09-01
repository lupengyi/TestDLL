# Design QA

- Reference: `C:\Users\2501000857\.codex\generated_images\019fd4c1-954e-7bb3-a420-907a94fbbbdb\exec-d8e38e92-6bc5-44c5-81b5-6464c1906e89.png`
- Locator dialog capture: `C:\Users\2501000857\AppData\Local\Temp\locator-signal-dialog-implemented-v2.png`
- DBC dialog capture: `C:\Users\2501000857\AppData\Local\Temp\dbc-signal-dialog-implemented.png`
- Main summary capture: `C:\Users\2501000857\AppData\Local\Temp\locator-summary-implemented.png`

## Comparison

- P0: none.
- P1: none. The dense inline signal table is removed; signal selection and rule editing are functional modal workflows.
- P2: none. The primary hierarchy, left signal list, right detail form, summary counts, selected-signal chips, search/filter controls, and apply/cancel actions match the selected direction.
- P3: WPF uses native radio buttons instead of the mockup's fully segmented result-mode control. This preserves keyboard behavior and is acceptable polish for the current industrial desktop design system.

## Functional checks

- Existing `SignalChecksJson` restores into the Locator modal and rebuilds without schema changes.
- Locator read-table defaults new selections to information-only and supports optional numeric/string judgment.
- Locator write-table selections preserve write values and readback verification.
- DBC send/periodic modes support multi-signal selection and value editing.
- DBC read mode enforces one selected signal and keeps the existing result/LIMIT editor.
- Main screens show summaries instead of wide signal tables.

final result: passed

