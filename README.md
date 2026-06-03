# Timbo Jimbo - Styling

A CSS-inspired styling system for Unity.

It lets you define named styles for a `GameObject` subtree, switch those styles on and off through simple activation sources, and smoothly transition individual properties between states.

✨ **Sparse Styles**

Each style only stores the properties it actually overrides. Everything else falls back to a shared baseline.

🎛️ **Authoring-Friendly**

Create and edit styles by recording changes directly in the Scene, then fine tune values and transitions in a unified property table.

⚡ **Per-Property Transitions**

Control duration, easing, and interpolation per property.

🧩 **Built on Property Bindings**

Uses the [Property Bindings package](https://github.com/Timbo-Jimbo/PropertyBindings) under the hood, so styles can drive the same bindable properties that package exposes.

# Installation

This package is available on [OpenUPM](https://openupm.com/packages/com.timbojimbo.styling)

1. Add the Scoped Registry:
	- Open **Edit > Project Settings > Package Manager**
	- Add a new Scoped Registry (or append the missing scope if you already have one):
		- Name: `OpenUPM`
		- URL: `https://package.openupm.com/`
		- Scope(s): `com.timbojimbo`
2. Install the package
	- Open **Window > Package Manager**
	- Click Add and select **Add package by name...**
	- Paste name: `com.timbojimbo.styling`

Done!


> [!WARNING]
> This package is new - use at your own risk! :)

<details>
<summary>Install from GitHub instead (Not Recommended)</summary>

You can also add it directly from GitHub on Unity 2019.4+. Note that you won't be able to receive updates through Package Manager this way, you'll have to update manually.

- First follow the installation instructions for the [Property Bindings package](https://github.com/Timbo-Jimbo/PropertyBindings)
- Once installed, open **Window > Package Manager** 
- Click add and select **Add package from git URL...**
- Paste `https://github.com/Timbo-Jimbo/Styling.git?path=Packages/com.timbojimbo.styling`
</details>

# Usage

## Quick Start

The usual workflow looks like this:

1. Add a `StyleSheet` component to the root of the object hierarchy you want to style.
2. Use the `StyleSheet` inspector to create your styles.
3. Add a `StyleGroup` on the same object or on a parent object.
4. Enable or disable style names in that `StyleGroup` to drive the sheet.

In most UI setups, you will put the `StyleSheet` on a panel or widget root and use styles for things like hover, selected, disabled, danger, success, and so on. You could also use it to theme entire screens as ie. "Dark Mode", "Horizontal"/"Vertical", etc.

## Core Concepts

### `StyleSheet`

`StyleSheet` is the component that actually holds and applies styled values.

It stores:

- a **baseline** value for each managed property
- a list of named **styles**
- per-property **transition settings**

At runtime, the sheet resolves the currently active style names and produces a target set of values like this:

1. Start with baseline values
2. Apply each active style that exists on the sheet
3. If multiple active styles write the same property, the later style in the sheet wins

Each style is intentionally sparse. If a style does not define a value for a property, that property simply inherits from the baseline or from a previously applied active style.

### Baseline

The baseline is the default look of the styled object subtree.

Think of it as the “no styles active” state. Every property used by any style also exist in baseline.

Typical baseline values are things like:

- normal button colors
- default scale
- default rotation
- default sprite
- default alpha

### `Style`

A style is just a named set of property overrides.

Examples:

- `Hover`
- `Pressed`
- `Disabled`
- `Danger`
- `Selected`
- `DarkMode`

Styles are identified by string name. A style does not need to contain every property in the sheet - only the ones it wants to change.

### `StyleGroup`

`StyleGroup` is the built-in activation source.

It exposes a list of `StyleActivation` entries, where each entry contains:

- a style name
- whether that style is active

`StyleGroup` participates in hierarchy resolution, so closer groups override farther ancestor groups. That makes it easy to establish shared state at a parent level and then override it deeper in the tree when needed.

Example: a parent `StyleGroup` could activate `Hover` and `Selected` for the whole subtree, then a child `StyleGroup` could deactivate `Hover` for just its own subtree.

## Authoring Workflow

### Create a Style

In the `StyleSheet` inspector:

- click **Add** in the **Styles** section
- Unity enters a recording session
- make changes in the Scene view or inspector
- save to create a new sparse style containing only the properties you actually edited

### Edit an Existing Style

Each style row has:

- **Preview** — hold to temporarily preview that style
- **Edit** — start a recording session for that style
- **✕** — delete the style

Editing an existing style only records the properties you changed during that session.

### Edit the Baseline

The baseline row works the same way:

- **Preview** shows the baseline-only state
- **Edit** records changes into the baseline

Use this when you want to redefine the default appearance without touching the style-specific overrides.

### Property Table

Below the styles list, the inspector exposes a property table with two views:

### Values View

The Values view shows:

- the property name
- the baseline value
- one column per style

Notes:

- `inherit` means that style does not override that property
- right click cells to add, remove, copy, or paste values
- right click a property, component, or `GameObject` label to remove those properties from the sheet

### Transitions View

The Transitions view configures animation per property.

Each property can define:

- **Duration**
- **Ease Type**
- **Interpolation** for supported continuous value types
- **Discrete Value Selection** for non-continuous value types

If `Duration` is `0`, the property applies instantly. Transition is considered 'disabled' in this case.

Continuous values like floats, vectors, colors, and quaternions can animate over time. Discrete values can still transition by deciding whether the left, right or nearest side “wins” during the blend window. (Useful to control *when* a discrete value acutally swaps over to the new value during a transition)

## Runtime Behavior

When style activations change, `StyleSheet` recalculates its target values and applies them.

On live instances, animated properties interpolate over time using their configured transition settings.

In non-live contexts such as prefab editing or other editor-only situations, values are applied instantly instead of animating.

## Scripting API

Most runtime interaction goes through `StyleGroup`, `StyleSheet`, and `StylingSystem`.

### Mark Style Sources Dirty

If you write your own activation source, call `StylingSystem.MarkDirty(...)` when its effective activations change.

```csharp
using System.Collections.Generic;
using TimboJimbo.Styling;
using UnityEngine;

public class ExampleStyleSource : MonoBehaviour, IStyleActivationSource
{
	[SerializeField] private bool _hovered;

	public void SetHovered(bool hovered)
	{
		if (_hovered == hovered)
			return;

		_hovered = hovered;
		StylingSystem.MarkDirty(this);
	}

	public void GetStyleActivations(List<StyleActivation> activations)
	{
		activations.Clear();
		activations.Add(new StyleActivation("Hover", _hovered));
	}
}
```

### Query Supported Style Names

Use `StylingSystem.GetSupportedStyleNames` to discover which style names exist on a root object.

```csharp
using System.Collections.Generic;
using TimboJimbo.Styling;
using UnityEngine;

var supported = new List<string>();
StylingSystem.GetSupportedStyleNames(gameObject, supported, includeChildren: true);

foreach (var styleName in supported)
{
	Debug.Log(styleName);
}
```

### Query Resolved Activations

Use `StylingSystem.GetStyleActivations` to inspect the current resolved activation state for a root.

```csharp
using System.Collections.Generic;
using TimboJimbo.Styling;
using UnityEngine;

var activations = new List<StyleActivation>();
StylingSystem.GetStyleActivations(gameObject, activations);

foreach (var activation in activations)
{
	Debug.Log($"{activation.Name}: {activation.Active}");
}
```

### Temporarily Override Active Styles

Use `StylingSystem.StylingOverrideScope(...)` when you want to force a temporary set of active styles for a subtree.

```csharp
using TimboJimbo.Styling;

using var preview = StylingSystem.StylingOverrideScope(gameObject, new[] { "Hover", "Selected" });

// Styles remain overridden until the scope is disposed.
```

Overrides follow the same hierarchy idea as other style sources:

- overrides closer to the root win over more distant overrides
- if multiple overrides exist on the same root, newer ones win over older ones

## Tips

- Put your `StyleSheet` on the root of the subtree you want to style.
- Keep baseline representative of the true default state.
- If two active styles fight over the same property, the later style in the sheet wins.

# Extra

This package is built on top of `TimboJimbo.PropertyBindings`, so if you want to understand which properties can be styled - or add support for more property types - that package is the next place to look.

# AI Usage Disclosure

The overall architecture and serialized data structures designed by a human. An LLM was used in some Runtime logic, and used extensively in Editor Tooling and Documentation.