## [Unreleased]

## [0.11.0] - 18/09/2026

### Added

- `StyledProperty`: a component that styles a single bindable property — baseline (literal or theme-linked), sparse per-style overrides, and one transition — without a full `StyleSheet`. Its inspector mirrors the sheet's style list (Baseline row, hold-to-preview, right-click value menu)
- `StyledProperty` style names are reported by `StylingSystem.GetSupportedStyleNames`, its baseline is serialized by the scene/prefab save handler, and sheet recording ignores edits to it

### Changed

- Foldout headers, indented section bodies, per-object expanded state and ghost buttons now come from `com.timbojimbo.core` (`FoldoutGUI`), adopting the taller banded look shared with Localization; `StylingEditorGUI.DrawFoldout` and `StyleSheetEditor.SessionBool` are removed
- Theme-linked cells (sheet table and `StyledProperty`) now draw the real value control, locked, showing the resolved value, with a link chip (icon + key) attached to its right edge; unresolved keys show a warning icon and the literal fallback
- Shared value-cell helpers (`StyleCellEditorGUI`): themed cell drawing, Copy/Paste, Link/Unlink to Theme, and right-click detection are one implementation used by both inspectors
- `StyledProperty` value cells that are not theme-linked show an "unlinked" chip beside the control, so a linkable value no longer looks like a plain field; it dims when no `StyleThemeSource` is in the hierarchy and clicking it opens the same menu as a right-click
- Updated `com.timbojimbo.core` dependency to `0.6.0` (provides `FoldoutGUI`; its linear-light OkLab/OkLCh blending changes the output of OkLab/OkLCh transitions) and `com.timbojimbo.propertybindings` to `0.8.5`
- `package.json`'s `changelogUrl` now points at the `CHANGELOG.md` inside the package folder

### Fixed

- `StyledProperty` inspector: right-clicking a colour (or other) value cell now opens the Copy/Paste/Link menu; the cell's own right-click handler used to take the event first
- `StyledProperty` inspector: picking a property no longer throws a layout-group mismatch from the Transition section, since the new property is applied before the sections lay out

## [0.10.2] - 15/09/2026

### Fixed

- Restored Style Sheet recording startup by updating Property Bindings to filter unsupported Unity-internal serialized properties
- Replaced the obsolete sorted `FindObjectsByType` overload in the Style Theme editor

### Changed

- Updated `com.timbojimbo.propertybindings` dependency to `0.8.4`

## [0.10.1] - 15/09/2026

### Fixed

- Replaced deprecated `GetInstanceID` editor usage with Unity's `GetEntityId` API

### Changed

- Updated `com.timbojimbo.propertybindings` dependency to `0.8.3`

## [0.10.0] - 03/09/2026

### Added

- `StyleTheme` asset (flat key → typed value table) and `StyleThemeSource` component; nearest source up the hierarchy resolves linked cells
- Style and baseline cells can link to a theme key (`StyleSheet.SetThemeKey`, right-click → Link to Theme); the literal is kept as fallback for missing keys or kind mismatches
- Theme swaps re-style dependents and animate through existing transitions

## [0.9.2] - 03/09/2026

### Added

- `SiblingStyleState`: activation source emitting `First`, `Last`, `Odd`, `Even` and optional `Nth` (stride/offset) from sibling position

## [0.9.1] - 03/09/2026

### Added

- `InteractionStyleState`: built-in activation source emitting `Hovered`, `Pressed`, `Selected` and `Disabled` from pointer/selection events; `Disabled` follows `Selectable.interactable` and parent `CanvasGroup` interactability

## [0.9.0] - 03/09/2026

### Breaking changes

- Removed the unused programmatic-authoring layer: `StyleSheetEditSession`, `StyleSheetSnapshot`, `StyleSheetValidationReport`, and `StyleSheet.UpsertStyle/ReplaceStyle/ReplaceAllStyles/ClearStyles/GetOrCreateStyle/ResolveTargetValues/CreateSnapshot/ValidateBindings/HasInvalidBindings`. The editor's authoring API (`CreateStyle`, `EditStyle`, `SetStyleValue`, `SetBaselineValue`, `SetTransition`, ...) is the single mutation surface

### Added

- `StyleGroup.SetActive`, `IsActive`, and `Remove` for driving activations from code
- Behavioral tests covering activation, sheet-order precedence, hierarchy override, override scopes, and frame-driven transitions

### Changed

- Updated `com.timbojimbo.propertybindings` dependency to `0.8.2`

## [0.8.0] - 02/09/2026

### Added

- Added structured, non-mutating `StyleSheet.ValidateBindings()` reports
- Added public transition get/set/remove APIs
- Added idempotent `GetOrCreateStyle` authoring
- Added dry-run target value resolution without scene mutation or transition changes
- Added idempotent `UpsertStyle`, exact `ReplaceStyle`, `ReplaceAllStyles`, and `ClearStyles` operations
- Added immutable `StyleSheetSnapshot` capture for authored, active, resolved, and validation state
- Added transactional Editor authoring through `StyleSheetEditSession` with preflight validation, cancellation, one-step Undo, prefab override recording, and scene dirtying
- Added package-native authoring tests for mutation semantics, snapshots, cancellation, commit, idempotency, and Undo

### Changed

- Updated `com.timbojimbo.propertybindings` dependency to `0.8.0`

## [0.7.0] - 06/07/2026

- Updated `com.timbojimbo.propertybindings` dependency to `0.7.0`
- Per-`StyleSheet` foldout vars are now stored in `SessionPrefs`, so folding one doesnt fold all
- Disallow `StyleSheet` from recording user edits that target `StyleSheet`'s

## [0.6.1] - 21/06/2026

- Updated `com.timbojimbo.propertybindings` dependency to `0.6.1`

## [0.6.0] - 21/06/2026

- Fixed `StyleGroup` not updating when changed via Unity animation (`OnDidApplyAnimationProperties` hook added)
- Updated to use new Property Bindings API (`BulkWriteScope()`, `TryWrite()`, `TryRead()`)
- Updated `com.timbojimbo.propertybindings` dependency to `0.6.0`

## [0.5.0] - 04/06/2026

- Moved yet more shared code to Core package

## [0.4.0] - 04/06/2026

- Allow user to specify default values for transition properties
- Moved some shared code to Core package

## [0.3.1] - 04/06/2026

- Updated Readme.

## [0.3.0] - 03/06/2026
- Transiton options are now per-property-scope instead of style-sheet-scope
- Added more `EaseType`s instead of just a hard-coded OutCubic ease
- Added custom property drawers for `EaseType` and `DiscreteValueSelectionMode`
- Fixed issue where changes to styles via the inspector would not repaint the scene view
- Refined editor UI's

## [0.2.0] - 29/05/2026
- Added `CHANGELOG.md`
- First publish