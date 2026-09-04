# Instrument Station Configuration Design QA

- Reference: selected unified station/instrument mockup supplied in the conversation.
- Implementation capture: `artifacts/station-unified-v3.png` at 1600 x 900.
- Compared state: station 01 expanded with a CAN instrument selected and its configuration visible.

## Findings

- P0: none.
- P1: none.
- P2: none. The WPF screen preserves the selected split layout, station hierarchy, dedicated/shared grouping, selection treatment, right-side configuration form, scope control, and bottom actions.
- P3: native WPF typography and control density are slightly more compact than the concept image. This is consistent with the existing FCT Engineering Studio visual system.

## Interaction checks

- Selecting a station refreshes its instrument list.
- Selecting a station or shared instrument refreshes the right-side editor.
- Add, remove, convert to shared, convert to station-only, save, test, and initialize actions are wired.
- Shared instruments are represented once and rendered under every station.
- Connection status is neutral before initialization and green only after MainTest reports the device initialized.

final result: passed
