# Design QA

- Reference: `C:\Users\2501000857\AppData\Local\Temp\codex-clipboard-13cec40b-4722-4208-ab1c-6e2ef41d43ca.png`
- Locator dialog capture: `C:\Users\2501000857\AppData\Local\Temp\locator-table-dialog-final.png`
- DBC dialog capture: `C:\Users\2501000857\AppData\Local\Temp\dbc-table-dialog-unified.png`
- Main summary capture: `C:\Users\2501000857\AppData\Local\Temp\locator-summary-implemented.png`

## Comparison

- P0: none.
- P1: none. The dense inline signal table is removed; signal selection and rule editing are functional modal workflows.
- P2: none. Locator uses the requested full modal DataGrid with the original columns and bulk actions; DBC uses the same unified modal table styling.
- P3: none.

## Functional checks

- Existing `SignalChecksJson` restores into the Locator modal and rebuilds without schema changes.
- Locator read-table restores and edits result type, numeric/string judgment, LIMIT, compare mode, unit, and description in the modal table.
- Locator write-table shows the editable write-value column and preserves write values and readback verification.
- DBC send/periodic modes use a visually unified modal DataGrid with aligned headers/cells, multi-signal selection, editable values, units, raw ranges, and enum descriptions.
- DBC read mode uses the same modal table, enforces one selected signal, and keeps the existing result/LIMIT editor.
- Main screens show summaries instead of wide signal tables.

final result: passed
