using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Controllers.Admin;

[Authorize(Policy = "StaffAuth")]
[Route("admin/library")]
public class LibraryController(AppDbContext db, IWebHostEnvironment env) : Controller
{
    private const int PageSize = 20;

    private long CurrentStaffId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ─── Index ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, string? category, string? search, int page = 1)
    {
        var query = db.LibraryArticles.IgnoreQueryFilters()
            .Where(a => a.DeletedAt == null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.Category == category);

        if (!string.IsNullOrEmpty(search))
        {
            var q = search.ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(q) ||
                                     a.Content.ToLower().Contains(q));
        }

        int total    = await query.CountAsync();
        var articles = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        ViewBag.Counts = new Dictionary<string, int>
        {
            ["all"]       = await db.LibraryArticles.IgnoreQueryFilters().CountAsync(a => a.DeletedAt == null),
            ["draft"]     = await db.LibraryArticles.IgnoreQueryFilters().CountAsync(a => a.DeletedAt == null && a.Status == "draft"),
            ["published"] = await db.LibraryArticles.IgnoreQueryFilters().CountAsync(a => a.DeletedAt == null && a.Status == "published"),
        };
        ViewBag.Status   = status;
        ViewBag.Category = category;
        ViewBag.Search   = search;
        ViewBag.Page     = page;
        ViewBag.Pages    = (int)Math.Ceiling((double)total / PageSize);

        return View("~/Views/Admin/Library/Index.cshtml", articles);
    }

    // ─── Create ───────────────────────────────────────────────────────────

    [HttpGet("create")]
    public IActionResult Create() =>
        View("~/Views/Admin/Library/Create.cshtml");

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Store(
        string  title,
        string  category,
        string  content,
        string? contentFormat,
        string? excerpt)
    {
        if (!LibraryArticle.Categories.ContainsKey(category))
            ModelState.AddModelError("category", "Invalid category.");

        if (!ModelState.IsValid)
            return View("~/Views/Admin/Library/Create.cshtml");

        db.LibraryArticles.Add(new LibraryArticle
        {
            Title         = title,
            Category      = category,
            Content       = content,
            ContentFormat = contentFormat ?? "markdown",
            Excerpt       = excerpt,
            Status        = "draft",
            CreatedBy     = CurrentStaffId,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        TempData["Success"] = "Article created.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Edit ─────────────────────────────────────────────────────────────

    [HttpGet("{id:long}/edit")]
    public async Task<IActionResult> Edit(long id)
    {
        var article = await db.LibraryArticles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null) return NotFound();

        return View("~/Views/Admin/Library/Edit.cshtml", article);
    }

    [HttpPost("{id:long}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        long    id,
        string  title,
        string  category,
        string  content,
        string? contentFormat,
        string? excerpt)
    {
        var article = await db.LibraryArticles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null) return NotFound();

        article.Title         = title;
        article.Category      = category;
        article.Content       = content;
        article.ContentFormat = contentFormat ?? article.ContentFormat ?? "markdown";
        article.Excerpt       = excerpt;
        article.UpdatedAt     = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Changes saved.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    // ─── Publish / Unpublish ──────────────────────────────────────────────

    [HttpPost("{id:long}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(long id)
    {
        var article = await db.LibraryArticles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null) return NotFound();

        article.Status      = "published";
        article.PublishedAt ??= DateTime.UtcNow;
        article.UpdatedAt   = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Article published.";
        return Redirect(Request.Headers.Referer.FirstOrDefault() ?? "/admin/library");
    }

    [HttpPost("{id:long}/unpublish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(long id)
    {
        var article = await db.LibraryArticles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null) return NotFound();

        article.Status    = "draft";
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Moved to draft.";
        return Redirect(Request.Headers.Referer.FirstOrDefault() ?? "/admin/library");
    }

    // ─── Delete (soft) ────────────────────────────────────────────────────

    [HttpPost("{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Destroy(long id)
    {
        var article = await db.LibraryArticles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null) return NotFound();

        article.DeletedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Article deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Image upload ─────────────────────────────────────────────────────

    [HttpPost("upload-image")]
    public async Task<IActionResult> UploadImage(IFormFile image)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "No file." });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var ext     = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext))
            return BadRequest(new { error = "Invalid file type." });

        if (image.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "File too large (max 5MB)." });

        var dir      = Path.Combine(env.WebRootPath, "uploads", "library");
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var path     = Path.Combine(dir, fileName);

        await using var stream = System.IO.File.Create(path);
        await image.CopyToAsync(stream);

        return Ok(new { url = $"/uploads/library/{fileName}" });
    }
}
