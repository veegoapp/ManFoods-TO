using Microsoft.AspNetCore.Localization;

namespace MvcApp.Extensions
{
    public class MfLangCookieProvider : RequestCultureProvider
    {
        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            var lang = httpContext.Request.Cookies["mf-lang"];
            if (lang == "ar" || lang == "en")
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(lang));
            return NullProviderCultureResult;
        }
    }
}
