using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Web;
using Markdig;

namespace WWM_ASP.Models;

[Table("library_articles")]
public class LibraryArticle
{
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Column("category")]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("content_format")]
    [MaxLength(20)]
    public string? ContentFormat { get; set; } = "markdown";

    [Column("excerpt")]
    [MaxLength(500)]
    public string? Excerpt { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "draft";   // draft | published

    [Column("discord_message_id")]
    [MaxLength(255)]
    public string? DiscordMessageId { get; set; }

    [Column("discord_author")]
    [MaxLength(255)]
    public string? DiscordAuthor { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    // ─── Navigation ────────────────────────────────────────────────────────
    [ForeignKey(nameof(CreatedBy))]
    public Staff? Creator { get; set; }

    // ─── Category constants ────────────────────────────────────────────────
    public static readonly Dictionary<string, string> Categories = new()
    {
        ["character_development"] = "Character Development",
        ["arena_summary"]         = "Arena Summary",
        ["guild_war_experience"]  = "Guild War",
        ["dungeon_summary"]       = "Dungeon Summary",
        ["general"]               = "General",
    };

    public static readonly Dictionary<string, string> CategoryTags = new()
    {
        ["character_development"] = "Character",
        ["arena_summary"]         = "Arena",
        ["guild_war_experience"]  = "Guild War",
        ["dungeon_summary"]       = "Dungeon",
        ["general"]               = "General",
    };

    public static readonly Dictionary<string, string> CategoryIcons = new()
    {
        ["character_development"] = "fas fa-user-graduate",
        ["arena_summary"]         = "fas fa-trophy",
        ["guild_war_experience"]  = "fas fa-fist-raised",
        ["dungeon_summary"]       = "fas fa-dungeon",
        ["general"]               = "fas fa-book",
    };

    public static readonly Dictionary<string, string> CategoryColors = new()
    {
        ["character_development"] = "info",
        ["arena_summary"]         = "success",
        ["guild_war_experience"]  = "warning",
        ["dungeon_summary"]       = "danger",
        ["general"]               = "secondary",
    };

    public static readonly Dictionary<string, string> CategoryAccents = new()
    {
        ["character_development"] = "#ff4444",
        ["arena_summary"]         = "#c084fc",
        ["guild_war_experience"]  = "#f59e0b",
        ["dungeon_summary"]       = "#22d3ee",
        ["general"]               = "#9ca3af",
    };

    // ─── Helpers ───────────────────────────────────────────────────────────
    public bool IsPublished  => Status == "published";
    public bool IsDeleted    => DeletedAt.HasValue;

    public string CategoryLabel  => Categories.GetValueOrDefault(Category, Category);
    public string CategoryTag    => CategoryTags.GetValueOrDefault(Category, Category);
    public string CategoryIcon   => CategoryIcons.GetValueOrDefault(Category, "fas fa-book");
    public string CategoryColor  => CategoryColors.GetValueOrDefault(Category, "secondary");
    public string CategoryAccent => CategoryAccents.GetValueOrDefault(Category, "#9ca3af");

    public string Preview(int chars = 180)
    {
        var text  = !string.IsNullOrEmpty(Excerpt) ? Excerpt : Content;
        var plain = StripHtmlTags(text);
        return plain.Length > chars ? plain[..chars] + "…" : plain;
    }

    /// <summary>Render content for display (markdown / html / plain).</summary>
    public string RenderedContent()
    {
        var format = ContentFormat ?? "plain";

        if (format == "markdown")
        {
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
            var html = Markdown.ToHtml(Content, pipeline);
            return InjectLibImgClass(html);
        }

        if (format == "html")
        {
            return InjectLibImgClass(Content);
        }

        // plain — Discord-imported text with embedded <img> tags
        var parts  = SplitOnImgTags(Content);
        var output = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
            {
                output.Append(part.Replace("<img", "<img class=\"lib-img\" loading=\"lazy\"",
                    StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var escaped = HttpUtility.HtmlEncode(part);
                escaped = escaped.Replace("--- Hình ảnh ---",
                    "<div class=\"lib-img-section-title\">Hình ảnh</div>");
                escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\n?---\n?",
                    "<hr class=\"lib-divider\">");
                escaped = escaped.Replace("\n", "<br>");
                output.Append(escaped);
            }
        }
        return output.ToString();
    }

    // ─── Private helpers ───────────────────────────────────────────────────

    private static string InjectLibImgClass(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<img(?![^>]*class=)([^>]*)>",
            "<img class=\"lib-img\" loading=\"lazy\"$1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string[] SplitOnImgTags(string input)
    {
        return System.Text.RegularExpressions.Regex.Split(
            input, @"(<img[^>]+>)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string StripHtmlTags(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
    }
}
