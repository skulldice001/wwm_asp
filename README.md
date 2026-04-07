# WWM Control — ASP.NET Core

ASP.NET Core 9 MVC rewrite of the WWM Control system (originally Laravel).

## Stack

- **ASP.NET Core 9** — MVC + Razor Views
- **Entity Framework Core 9** + **Npgsql** — PostgreSQL
- **BCrypt.Net-Next** — password hashing (compatible with Laravel bcrypt)
- **AdminLTE 3** — admin panel UI (CDN)
- **Discord OAuth2** — member login

## Project structure

```
WWM_ASP/
├── Controllers/
│   ├── Admin/
│   │   ├── AdminAuthController.cs   # /admin/login, /admin/logout
│   │   ├── DashboardController.cs   # /admin/dashboard
│   │   ├── UsersController.cs       # /admin/users
│   │   └── StaffController.cs      # /admin/staff
│   ├── AuthController.cs            # /login, /logout, /auth/discord
│   └── HomeController.cs
├── Data/
│   └── AppDbContext.cs
├── Middleware/
│   └── LocalizationMiddleware.cs    # session-based locale (vi/en)
├── Models/
│   ├── User.cs
│   ├── Staff.cs
│   └── ZooCoinTransaction.cs
├── Services/
│   ├── DiscordAuthService.cs        # OAuth2 manual flow
│   └── ZooCoinService.cs
├── Views/
│   ├── Admin/
│   │   ├── Auth/Login.cshtml
│   │   ├── Dashboard/Index.cshtml
│   │   ├── Staff/                   # Index, Create, Edit
│   │   └── Users/                   # Index, Show, Create, CoinHistory
│   ├── Auth/Login.cshtml
│   ├── Home/Index.cshtml
│   └── Shared/
│       ├── _AdminLayout.cshtml
│       └── _Layout.cshtml
├── wwwroot/
├── Program.cs
└── appsettings.json
```

## Setup

### 1. Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- PostgreSQL (same database as the Laravel app)

### 2. Configure

Edit `appsettings.json` (or create `appsettings.Local.json`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=the_zoo;Username=postgres;Password=yourpassword"
  },
  "Discord": {
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "RedirectUri": "https://localhost:5001/auth/discord/callback"
  }
}
```

### 3. Run

```bash
cd WWM_ASP
dotnet run
```

App starts at `https://localhost:5001` (or `http://localhost:5000`).

## Auth

| Panel | URL | Scheme |
|-------|-----|--------|
| Member login | `/login` | Discord OAuth or username/password |
| Admin login | `/admin/login` | Username + password (staff table) |

Two independent cookie schemes: `wwm_user` and `wwm_staff`. Sessions do not interfere.

## Staff roles

| Role | Permissions |
|------|-------------|
| `master` | Full access, can manage all staff |
| `admin` | Manage users and observer/librarian staff |
| `observer` | Read-only |
| `librarian` | Manage library content |

## Zoo-coins

Coin adjustments go through `ZooCoinService` which records every change in `zoo_coin_transactions`. Direct balance updates without a transaction record are not allowed. The `freeze` mechanic uses a separate `z_coins_frozen` column; `AvailableZCoins = ZCoins - ZCoinsFrozen`.

## Soft delete

Both `users` and `staffs` tables use `deleted_at` for soft delete. EF Core global query filters exclude soft-deleted records automatically; controllers use `IgnoreQueryFilters()` where needed.
