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