# Activity Rail Design QA

- Source visual truth: `docs/design/approved-activity-rail.png`
- Implementation screenshot: `docs/design/activity-rail-implementation.png`
- Full comparison: `docs/design/activity-rail-comparison.png`
- Focused navigation comparison: `docs/design/activity-rail-nav-comparison.png`
- Viewport: 1690 × 930 desktop, light theme, 96 DPI
- Source pixels: 1690 × 930
- Implementation pixels: 1690 × 930
- Density normalization: none; both artifacts are equal-size 96 DPI captures
- State: sequence/module editor workspace selected; first activity item selected

## Findings

No actionable P0, P1, or P2 differences remain in the approved navigation scope.

- Fonts and typography: Microsoft YaHei UI/Segoe UI hierarchy, compact labels, weights, and wrapping match the established application style. The generated reference contains slightly softened raster text; the implementation intentionally keeps native WPF text rendering.
- Spacing and layout rhythm: the page-tab row is removed; the activity rail is 64 px, the content gap is 8 px, each activity item is 62 × 78 px, and the module library begins at the same horizontal position as the reference. The STEP/configuration split was corrected to approximately 55/45.
- Colors and visual tokens: white rail, pale-blue selected fill, blue 3 px indicator, blue selected icon/text, gray inactive icon/text, and existing industrial light-gray surfaces match the reference.
- Image and icon fidelity: no screenshot background or rasterized UI is used. Both navigation icons use the native Segoe MDL2 Assets icon library. The sequence icon uses the closest native list glyph to the reference's generated list/play composite.
- Copy and content: the two destinations remain “序列编辑与调试” and “高级工具”. Existing workspace content and commands are unchanged.

## Focused Evidence

The focused comparison confirms the rail width, selected indicator, two-item vertical arrangement, 280 px module library, 8 px gutter, search control alignment, and bottom library actions. A focused crop was required because these details are too small to judge reliably in the full-window comparison.

## Comparison History

### Iteration 1

- P2: the lower configuration region was about 90 px taller than the approved image, reducing visible STEP-list space.
- Fix: changed the default editor region from 470/420 px to a 360–420 px responsive target with a 340 px minimum while preserving the draggable splitter and internal scrolling.

### Iteration 2

- Post-fix evidence: `docs/design/activity-rail-comparison.png` and `docs/design/activity-rail-nav-comparison.png` show the corrected STEP/configuration balance and matching navigation geometry.
- Remaining differences are content-state differences in test data, plus native WPF text/icon rendering versus the generated raster reference; neither changes the approved layout or interaction.

## Interaction Checks

- Sequence activity item opens the unified sequence editor and shows the blue selected state.
- Advanced Tools activity item opens the existing advanced workspace and transfers the selected state.
- Existing edit/debug mode, module navigation, bindings, commands, drag/drop, save format, and instrument pages remain intact.
- The removed TabControl header is replaced by a content-only template; no invisible blank tab row remains.

## Final Result

final result: passed
