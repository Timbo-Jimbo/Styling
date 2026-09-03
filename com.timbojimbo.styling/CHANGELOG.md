## [Unreleased]

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