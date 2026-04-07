using Microsoft.AspNetCore.Mvc;

namespace WWM_ASP.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();

    [Route("lang/{locale}")]
    public IActionResult SwitchLang(string locale)
    {
        if (locale is "vi" or "en")
            HttpContext.Session.SetString("locale", locale);

        return Redirect(Request.Headers.Referer.FirstOrDefault() ?? "/");
    }

    [Route("error")]
    public IActionResult Error() => View();
}
