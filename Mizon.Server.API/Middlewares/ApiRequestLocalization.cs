using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;

namespace Mizon.Server.API.Middlewares;

public static class ApiRequestLocalization
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Persian = CultureInfo.GetCultureInfo("fa-IR");

    public static IApplicationBuilder UseMizonRequestLocalization(this IApplicationBuilder app)
    {
        var formatCulture = CultureInfo.CurrentCulture;
        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(formatCulture, English),
            SupportedCultures = [formatCulture],
            SupportedUICultures = [English, Persian],
            ApplyCurrentCultureToResponseHeaders = true,
            RequestCultureProviders = [new ApiLanguageProvider()]
        };
        app.Use((context, next) =>
        {
            context.Response.Headers.AppendCommaSeparatedValues(HeaderNames.Vary, HeaderNames.AcceptLanguage);
            return next(context);
        });
        return app.UseRequestLocalization(options);
    }

    private sealed class ApiLanguageProvider : IRequestCultureProvider
    {
        private static readonly Task<ProviderCultureResult?> EnglishResult = Task.FromResult<ProviderCultureResult?>(new("", "en-US"));
        private static readonly Task<ProviderCultureResult?> PersianResult = Task.FromResult<ProviderCultureResult?>(new("", "fa-IR"));

        public Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext context)
        {
            var isPersian = false;
            var bestQuality = 0d;

            if (context.Request.Headers.ContainsKey(HeaderNames.AcceptLanguage))
            {
                foreach (var candidate in context.Request.GetTypedHeaders().AcceptLanguage)
                {
                    var quality = candidate.Quality ?? 1d;
                    if (quality <= bestQuality) continue;

                    var tag = candidate.Value.AsSpan();
                    var separator = tag.IndexOf('-');
                    var language = separator >= 0 ? tag[..separator] : tag;
                    if (language.Equals("fa", StringComparison.OrdinalIgnoreCase))
                        isPersian = true;
                    else if (language.Equals("en", StringComparison.OrdinalIgnoreCase))
                        isPersian = false;
                    else
                        continue;

                    bestQuality = quality;
                    if (bestQuality == 1d) break;
                }
            }

            return isPersian ? PersianResult : EnglishResult;
        }
    }
}
