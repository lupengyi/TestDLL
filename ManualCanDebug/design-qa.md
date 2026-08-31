# Design QA

- Reference: `C:\Users\2501000857\AppData\Local\Temp\codex-clipboard-e7afdacb-553c-44b3-85b9-38e92e088c7d.png`
- Implementation capture: `E:\FST\.codex-build\topbar-check\full2.png`
- Scope: top workspace context bar and selected-row emphasis.

## Comparison

- PASS: FCT mark, product context, SEQ context, separators and dirty-state badge follow the reference hierarchy.
- PASS: right-side workspace state uses warning/check icon plus one-line user-facing status.
- PASS: initialize action is solid blue; safe shutdown is white with red outline and shield icon.
- PASS: spacing, border, white background and command-bar height match the reference density.
- PASS: saved-path text is intentionally retained after the save badge per the user requirement; it hides while modified.
- PASS: selected rows use bold text across Studio flow, custom STEP, parameter, breakpoint and Locator tables.
- PASS: bottom status bar remains removed.

final result: passed
