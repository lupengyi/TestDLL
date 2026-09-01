# Design QA

- Reference: `C:\Users\2501000857\AppData\Local\Temp\codex-clipboard-13cec40b-4722-4208-ab1c-6e2ef41d43ca.png`
- Locator dialog capture: `C:\Users\2501000857\AppData\Local\Temp\locator-faithful-qa.png`
- DBC dialog capture: `C:\Users\2501000857\AppData\Local\Temp\dbc-faithful-final.png`
- Main summary capture: `C:\Users\2501000857\AppData\Local\Temp\locator-summary-implemented.png`

## Comparison

- P0: none.
- P1: none. The dense inline signal table is removed; signal selection and rule editing are functional modal workflows.
- P2: none. The borderless rounded window, integrated close action, blue segmented filters, selected-row highlight, blue checkboxes, segmented result mode, judgment switch, fixed footer, and primary hierarchy match the supplied target.
- P3: Numeric LIMIT fields use direct text entry rather than spinner arrow buttons; keyboard entry and validation are fully functional.

## Functional checks

- Existing `SignalChecksJson` restores into the Locator modal and rebuilds without schema changes.
- Locator read-table defaults new selections to information-only and supports optional numeric/string judgment.
- Locator write-table selections preserve write values and readback verification.
- DBC send/periodic modes support multi-signal selection and value editing.
- DBC read mode enforces one selected signal and keeps the existing result/LIMIT editor.
- Main screens show summaries instead of wide signal tables.

final result: passed
