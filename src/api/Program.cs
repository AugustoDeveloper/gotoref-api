using GotoRef.Api.Endpoints;
using GotoRef.Api.Domain.Services;
using GotoRef.Api.Registries;
using GotoRef.Api.Parsers;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
 
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CacheService>();
 
builder.Services.AddHttpClient("nuget", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "GoToRef/1.0 (https://gotoref.dev)");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddEnvironmentVariables();

// ── Services ───────────────────────────────────────────────────────────────
builder.Services.AddSingleton<NuGetRegistry>();
builder.Services.AddSingleton<IPackageRegistry>(sp => sp.GetRequiredService<NuGetRegistry>());
builder.Services.AddSingleton<ReflectionParser>();

// ── Rate Limiting ──────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", o =>
    {
        o.PermitLimit = 30;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 5;
    });

    options.RejectionStatusCode = 429;
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please wait before retrying." }, token);
    };
});

// ── CORS ───────────────────────────────────────────────────────────────────

var origins = builder.Configuration.GetValue<string>("Cors:AllowedOrigins", "http://localhost:3000")!.Split(',', StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {

        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ── Security Headers ───────────────────────────────────────────────────────
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    await next();
});

app.UseCors("Frontend");
app.UseRateLimiter();
 
 
app.MapGet("/health", () => Results.Ok("healthy"))
   .WithName("Health")
   .ExcludeFromDescription();
 
app.MapPackageEndpoints();
 
app.Run();
