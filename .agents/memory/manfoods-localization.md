---
name: Manfoods AR/EN localization setup
description: Full architecture for Arabic/English switching in the Manfoods McDonald's MVC app
---

## Cookie
- Name: `mf-lang`, values: `"ar"` or `"en"` (plain string, NOT `c=ar|uic=ar` format)
- Set via `LanguageController.Set(lang, returnUrl)` — POST, 1-year expiry
- Read directly in views/layouts: `Context.Request.Cookies["mf-lang"] ?? "en"` for RTL logic

## Culture Provider
- Custom `MfLangCookieProvider : RequestCultureProvider` in `Extensions/MfLangCookieProvider.cs`
- Reads "mf-lang" cookie, returns `ProviderCultureResult("ar")` or `ProviderCultureResult("en")`
- Must use custom provider — `CookieRequestCultureProvider` expects `c=ar|uic=ar` format and won't parse plain "ar"

**Why:** The standard `CookieRequestCultureProvider.MakeCookieValue()` produces "c=ar|uic=ar" format; if you set just "ar" as cookie value, the standard provider ignores it and falls back to English.

## Resource Files
- `Resources/SharedResource.resx` — English (default/neutral)
- `Resources/SharedResource.ar.resx` — Arabic
- `Resources/SharedResource.cs` — marker class: `namespace MvcApp.Resources { public class SharedResource {} }`
- Satellite DLL at `ar/MvcApp.resources.dll` contains resource named `MvcApp.Resources.SharedResource.ar.resources`
- Arabic output appears as HTML entities (&#xXXX;) in curl — browsers render these correctly as Arabic text

## Program.cs setup
```csharp
builder.Services.AddLocalization(opts => opts.ResourcesPath = "");  // empty string critical!
// ...
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders = new List<IRequestCultureProvider> { new MfLangCookieProvider() }
});
```

## ViewImports
Both `Areas/Admin/Views/_ViewImports.cshtml` and `Areas/Home/Views/_ViewImports.cshtml` have:
```
@using MvcApp.Resources
@inject IStringLocalizer<SharedResource> L
```

## JS localization pattern
Views with inline JS strings use:
```javascript
const _L = { key: '@L["ResourceKey"]' };
// then use _L.key in JS template literals
```

## RTL/LTR in layouts
Layouts read cookie directly (not from ASP.NET culture) for `html lang` and `dir` attributes.
Login.cshtml (Layout=null) also does the same pattern.
