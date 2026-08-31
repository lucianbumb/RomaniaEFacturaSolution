using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RomaniaEFactura;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Persistence;
using SampleWebApp.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Connecting a company writes an ANAF authorization into the token store, so the library requires
// an authenticated user on its endpoints and refuses to map them without an authorization service.
// A real application already has an identity system; this sample has to bring the smallest thing
// that is still a real one.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = "/sign-in");
builder.Services.AddAuthorization();

// This is the whole registration. Options bind from the "EFactura" section of appsettings; the
// second argument says where the library keeps its own data — authorizations, tracked submissions
// and the inbox record.
builder.AddRomaniaEFactura(
    configureDatabase: options => options.UseSqlite(
        builder.Configuration.GetConnectionString("EFactura")
        ?? "Data Source=efactura-sample.db"));

var app = builder.Build();

// SQLite in a sample, so the schema is created on start. A real application would run this once
// during deployment rather than on every boot.
await app.Services.EnsureEFacturaSchemaAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// Stands in for this sample's missing identity system. A real application deletes this and uses
// its own sign-in; nothing here is a pattern worth copying except that a person is signed in
// before they can connect a company.
app.MapGet("/sign-in", async (HttpContext context, string? returnUrl) =>
{
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "sample-administrator"), new Claim(ClaimTypes.Name, "Sample administrator")],
        CookieAuthenticationDefaults.AuthenticationScheme);

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.LocalRedirect(returnUrl ?? "/");
});

// Mounts /efactura/connect/{cif} and /efactura/callback. The path has to match the redirect URI
// registered with ANAF, which cannot be changed without re-registering the application. Both
// endpoints require an authenticated user; the library will not map them otherwise.
app.MapEFacturaAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
