using System.Globalization;

namespace WWM_ASP.Middleware;

/// <summary>
/// Reads the "locale" session key and sets the thread culture.
/// Mirrors Laravel's lang switcher: GET /lang/{locale}
/// </summary>
public class LocalizationMiddleware(RequestDelegate next)
{
    private static readonly string[] Supported = ["vi", "en"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var locale = ctx.Session.GetString("locale") ?? "vi";
        if (!Supported.Contains(locale)) locale = "vi";

        var culture = new CultureInfo(locale == "vi" ? "vi-VN" : "en-US");
        CultureInfo.CurrentCulture   = culture;
        CultureInfo.CurrentUICulture = culture;

        await next(ctx);
    }
}
