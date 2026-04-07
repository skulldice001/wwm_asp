using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Middleware;
using WWM_ASP.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Database ───────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─── Authentication — two separate cookie schemes ────────────────────────────
// Scheme "user"  → normal members (Discord OAuth)
// Scheme "staff" → admin panel (username + password)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "user";
})
.AddCookie("user", options =>
{
    options.LoginPath        = "/login";
    options.LogoutPath       = "/logout";
    options.AccessDeniedPath = "/login";
    options.Cookie.Name      = builder.Configuration["Auth:UserCookieName"] ?? "wwm_user";
    options.ExpireTimeSpan   = TimeSpan.FromDays(
        int.Parse(builder.Configuration["Auth:CookieExpireDays"] ?? "30"));
    options.SlidingExpiration = true;
})
.AddCookie("staff", options =>
{
    options.LoginPath        = "/admin/login";
    options.LogoutPath       = "/admin/logout";
    options.AccessDeniedPath = "/admin/login";
    options.Cookie.Name      = builder.Configuration["Auth:StaffCookieName"] ?? "wwm_staff";
    options.ExpireTimeSpan   = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// ─── Authorization policies ──────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UserAuth",  p => p.AddAuthenticationSchemes("user").RequireAuthenticatedUser());
    options.AddPolicy("StaffAuth", p => p.AddAuthenticationSchemes("staff").RequireAuthenticatedUser());
    options.AddPolicy("AdminRole", p => p.AddAuthenticationSchemes("staff")
        .RequireAuthenticatedUser()
        .RequireClaim("role", "master", "admin"));
    options.AddPolicy("MasterRole", p => p.AddAuthenticationSchemes("staff")
        .RequireAuthenticatedUser()
        .RequireClaim("role", "master"));
});

// ─── MVC + Razor ─────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();  // hot-reload views in development

builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly  = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout      = TimeSpan.FromMinutes(60);
});

// ─── Application services ────────────────────────────────────────────────────
builder.Services.AddHttpClient<DiscordAuthService>();
builder.Services.AddScoped<ZooCoinService>();

var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

// Language switcher middleware (reads session["locale"])
app.UseMiddleware<LocalizationMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// ─── Routes ──────────────────────────────────────────────────────────────────
app.MapControllerRoute(
    name: "admin",
    pattern: "admin/{controller=Dashboard}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
