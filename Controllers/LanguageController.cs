using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Controllers
{
    public class LanguageController : Controller
    {
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Set(string lang, string returnUrl = "/")
        {
            var supported = new[] { "en", "ar" };
            if (!supported.Contains(lang)) lang = "en";

            Response.Cookies.Append("mf-lang", lang, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = true,                // only the server reads it (MfLangCookieProvider / views)
                Secure = Request.IsHttps,       // HTTPS only when served over HTTPS; plain-HTTP dev keeps working
                SameSite = SameSiteMode.Lax
            });

            if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
            return Redirect(returnUrl);
        }
    }
}
