---
name: ASP.NET Core Localization ResourcesPath trap
description: ResourcesPath="" not "Resources" when namespace matches folder name to avoid double-path lookup
---

When using `IStringLocalizer<MvcApp.Resources.SharedResource>` with resources in `Resources/SharedResource.resx`:

**Rule:** Set `ResourcesPath = ""` (empty string), NOT `"Resources"`.

**Why:** ASP.NET Core computes the resource lookup path as:
  `ResourcesPath + "/" + (TypeFullName minus RootNamespace) with dots→slashes`

With `RootNamespace = MvcApp`, type `MvcApp.Resources.SharedResource`:
- Stripped: `Resources.SharedResource` → `Resources/SharedResource`
- With `ResourcesPath = "Resources"`: looks for `Resources/Resources/SharedResource.resx` ← WRONG (double folder)
- With `ResourcesPath = ""`: looks for `Resources/SharedResource.resx` ← CORRECT

**How to apply:** Any time the namespace subfolder matches the ResourcesPath value, use empty string. The namespace-based path already encodes the folder structure.
