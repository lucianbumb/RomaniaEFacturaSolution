using Microsoft.EntityFrameworkCore;
using RomaniaEFactura;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Persistence;
using SampleWebApp.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

// Mounts /efactura/connect/{cif} and /efactura/callback. The path has to match the redirect URI
// registered with ANAF, which cannot be changed without re-registering the application.
app.MapEFacturaAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
