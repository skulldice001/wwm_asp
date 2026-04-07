using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Controllers;

[Route("library")]
public class LibraryController(AppDbContext db) : Controller
{
    private const int PageSize = 12;

    // ─── Index / Search ───────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search)
    {
        if (!string.IsNullOrEmpty(search))
        {
            var q       = search.ToLower();
            int page    = int.TryParse(Request.Query["page"], out var p) ? p : 1;
            int total   = await db.LibraryArticles
                .Where(a => a.Status == "published" &&
                            (a.Title.ToLower().Contains(q) || a.Content.ToLower().Contains(q)))
                .CountAsync();

            var articles = await db.LibraryArticles
                .Where(a => a.Status == "published" &&
                            (a.Title.ToLower().Contains(q) || a.Content.ToLower().Contains(q)))
                .OrderByDescending(a => a.PublishedAt)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Page   = page;
            ViewBag.Pages  = (int)Math.Ceiling((double)total / PageSize);
            ViewBag.Total  = total;
            return View("~/Views/Library/Search.cshtml", articles);
        }

        var counts = new Dictionary<string, int>();
        foreach (var cat in LibraryArticle.Categories.Keys)
        {
            counts[cat] = await db.LibraryArticles
                .CountAsync(a => a.Status == "published" && a.Category == cat);
        }

        ViewBag.Counts = counts;
        return View("~/Views/Library/Index.cshtml");
    }

    // ─── Category ─────────────────────────────────────────────────────────

    [HttpGet("{category}")]
    public async Task<IActionResult> Category(string category)
    {
        if (!LibraryArticle.Categories.ContainsKey(category))
            return NotFound();

        int page  = int.TryParse(Request.Query["page"], out var p) ? p : 1;
        int total = await db.LibraryArticles
            .CountAsync(a => a.Status == "published" && a.Category == category);

        var articles = await db.LibraryArticles
            .Where(a => a.Status == "published" && a.Category == category)
            .Include(a => a.Creator)
            .OrderByDescending(a => a.PublishedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        ViewBag.Category = category;
        ViewBag.Page     = page;
        ViewBag.Pages    = (int)Math.Ceiling((double)total / PageSize);
        ViewBag.Total    = total;
        return View("~/Views/Library/Category.cshtml", articles);
    }

    // ─── Show ─────────────────────────────────────────────────────────────

    [HttpGet("{category}/{id:long}")]
    public async Task<IActionResult> Show(string category, long id)
    {
        var article = await db.LibraryArticles
            .Include(a => a.Creator)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (article == null || !article.IsPublished || article.Category != category)
            return NotFound();

        ViewBag.Category = category;
        return View("~/Views/Library/Show.cshtml", article);
    }
}
