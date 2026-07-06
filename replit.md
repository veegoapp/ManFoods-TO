# MvcApp

ASP.NET Core 9 MVC Web Application built with C#.

## Run & Operate

- `dotnet run` — run the app
- `dotnet build` — build the project
- `dotnet restore` — restore NuGet packages
- Required env: `DATABASE_URL` — PostgreSQL connection string (optional until DB is used)

## Stack

- ASP.NET Core 9 MVC
- C# / Razor Views (.cshtml)
- Entity Framework Core 9 + Npgsql (PostgreSQL)
- Bootstrap 5 (included in wwwroot/lib)

## Where things live

- `Controllers/` — MVC controllers
- `Models/` — view models and domain models
- `Views/` — Razor views (.cshtml)
- `Data/` — AppDbContext (Entity Framework Core)
- `Services/` — application services (dependency injection)
- `wwwroot/` — static files (css, js, images, libs)
- `Program.cs` — app entry point and DI configuration
- `appsettings.json` — app configuration
- `MvcApp.csproj` — project file
- `MvcApp.sln` — solution file

## User preferences
- الردود دايماً بالعربي البسيط المفهوم، من غير مصطلحات تقنية إلا لما تكون ضرورية

- Pure C# ASP.NET Core MVC — no Node.js, no React, no frontend frameworks.
