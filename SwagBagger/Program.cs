using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SwagBagger.Components;
using SwagBagger.Models;
using SwagBagger.Services;
using System.Security.Claims;
using System.Threading.RateLimiting;
using t0m.Ting;

// Create builder and add services
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<TotpService>();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/login";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
    {
        string clientIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, partitionKey => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.Redirect("/too-many-attempts");
        return ValueTask.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<QBittorrentClient>();
builder.Services.AddSingleton<ProwlarrClient>();
builder.Services.AddSingleton<MediaPathBuilder>();
builder.Services.AddSingleton<MagnetLinkParser>();
builder.Services.AddTingClient(builder.Configuration);
builder.Services.AddSingleton<TorrentMonitorService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TorrentMonitorService>());
builder.Services.AddSingleton<PlexClient>();

// Create app and configure features
WebApplication app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Validates the Turnstile challenge and a submitted TOTP code, then issues a sign-in cookie on success
app.MapPost("/account/login", async (HttpContext context, TotpService totpService, IConfiguration config, IHttpClientFactory httpClientFactory) =>
{
    IFormCollection form = await context.Request.ReadFormAsync();
    string? code = form["code"];
    string? turnstileToken = form["cf-turnstile-response"];
    string turnstileSecret = config["Turnstile:SecretKey"] ?? throw new InvalidOperationException("Turnstile:SecretKey is not configured.");

    HttpClient client = httpClientFactory.CreateClient();
    Dictionary<string, string> verifyPayload = new() { ["secret"] = turnstileSecret, ["response"] = turnstileToken ?? "" };
    HttpResponseMessage verifyResponse = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(verifyPayload));
    TurnstileVerifyResult? verifyResult = await verifyResponse.Content.ReadFromJsonAsync<TurnstileVerifyResult>();
    if (verifyResult is null || !verifyResult.Success)
    {
        return Results.Redirect("/login?failed=true");
    }

    string secret = config["Totp:Secret"] ?? throw new InvalidOperationException("Totp:Secret is not configured.");
    if (code is not null && totpService.ValidateCode(secret, code))
    {
        ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "tom")], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/");
    }
    return Results.Redirect("/login?failed=true");
}).RequireRateLimiting("login");

// Clears the sign-in cookie and returns to the login page
app.MapPost("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.Run();