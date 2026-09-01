# Hierarchical Sequence Editor Design QA

- Source visual truth: `docs/design/approved-hierarchical-sequence-editor.png`
- Implementation screenshot: `docs/design/hierarchical-sequence-implementation.png`
- Full comparison: `docs/design/hierarchical-sequence-comparison.png`
- Focused table comparison: `docs/design/hierarchical-sequence-focused-comparison.png`
- Viewport: 1704 × 923 desktop, light theme, 96 DPI
- Source pixels: 1704 × 923
- Implementation pixels: 1704 × 923
- Density normalization: none
- State: edit mode; module library closed; one flow instance expanded through three hierarchy levels

## Findings

No actionable P0, P1, or P2 visual or interaction mismatch remains.

- Fonts and typography: native Microsoft YaHei UI / Segoe UI rendering preserves the compact 12–14 px engineering-tool hierarchy. Native WPF rasterization is sharper than the generated source but the weights, wrapping, and hierarchy match.
- Spacing and layout rhythm: the old lower configuration region is absent. The hierarchy table owns the full content height. Row density is 34 px for STEP rows and 40 px for module rows.
- Colors and visual tokens: white base, pale gray module rows, pale blue selection, blue hierarchy icons, green action icons, orange wait icons, red breakpoint state, and thin gray-blue separators match the approved direction.
- Image and icon fidelity: the screen contains no photographic assets. Native Segoe MDL2 Assets glyphs are used; no screenshot background is used.
- Copy and content: no visible rows named “参数设置”, “LIMIT判断”, “条件与断点”, “模块参数”, or “高级设置”. Every displayed child is a real STEP or module reference.

## Hierarchy and Data Fidelity

- Level 1 displays the current SEQ function-block instance with an editable display name and explicit binding source.
- Level 2 displays every direct STEP and nested module in execution order.
- Level 3 and deeper recursively display every STEP inside nested module references.
- Standard module references show `标准模块 · 模板只读`; their library definitions and defaults remain immutable.
- Current-SEQ values are stored by flow instance and hierarchy path, not in the standard-module definition.
- Module-library definitions are recursively snapshotted when inserted into a SEQ. Later library edits do not silently alter existing SEQs.
- `更新到模块库最新版本` is an explicit context-menu action.
- Platform SEQ output remains the existing flat STEP JSON; editor-only snapshots and overrides never leak into it.

## Interaction Coverage

- Scalar parameters use inline editors and validate numeric types.
- Invalid scalar input receives a red field state and is not committed.
- LIMIT lower/upper/compare/unit values edit on the real STEP row.
- Breakpoint, enabled/disabled, status, product, and current value are on the same row.
- Complex DBC/Locator/table rows show a summary and `配置…`, opening the existing complete configuration workflow.
- Module-reference rows show the SEQ display name and bound module separately; `绑定…` can rebind the current instance without changing the library definition.
- Edit mode hides run actions. Debug mode adds row-level run actions while preserving whole-flow run, step, continue, and safe stop.
- Top-level instances support drag/drop reorder, library drop insertion, copy, move, disable, delete, and explicit module-version update.
- Module-library management retains new, import, export, copy, delete, right-click operations, and nested module insertion.
- Standard modules are read-only and provide `复制为自定义`.

## Comparison History

### Iteration 1

- P1: the first concept showed exposed parameters but could imply that non-parameter STEP rows were hidden.
- Fix: recursively flatten every real STEP and module reference into one ordered hierarchy.

### Iteration 2

- P1: current-SEQ edits could have mutated nested standard-module values through shared reference dictionaries.
- Fix: added path-scoped STEP overrides and path-scoped reference-parameter overrides.

### Iteration 3

- P1: editing a module-library definition previously synchronized existing flow snapshots automatically.
- Fix: added recursive module snapshots and removed automatic synchronization. Existing SEQs update only through the explicit update command.
- P2: inherited 46 px DataGrid rows displayed fewer hierarchy levels than the target.
- Fix: set native auto row sizing with 34 px STEP minimum and 40 px module minimum.

### Final Evidence

- `docs/design/hierarchical-sequence-comparison.png` confirms the same full-window composition and table hierarchy.
- `docs/design/hierarchical-sequence-focused-comparison.png` confirms indentation, inline values, LIMIT columns, breakpoint state, nested standard-module identity, and complex configuration affordance.

## Verification

- Release x86 build: 0 warnings, 0 errors.
- Debug x86 independent-output build: 0 warnings, 0 errors.
- Core regression suite: 366 assertions passed.
- Studio UI, studio project, full startup, old SEQ compile, save/reopen, and private editor-state persistence: passed.

final result: passed
