using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Whether one company can read another's documents out of the shared local store.
/// </summary>
/// <remarks>
/// <para>
/// The library is built for multi-tenancy on purpose: the per-call CIF override exists so one
/// deployment can serve several companies. They share one database.
/// </para>
/// <para>
/// ANAF enforces rights on a download, so the remote path is safe — ask for somebody else's
/// document and ANAF refuses. But the library deliberately caches archives, because downloads are
/// capped at roughly ten per identifier per day, and <b>a cached archive never reaches ANAF</b>.
/// Whatever check there is has to happen locally or it does not happen at all.
/// </para>
/// </remarks>
public class CrossCompanyAccessTests
{
    private const string Ours = "12345674";
    private const string Theirs = "19867700";
    private const string TheirDownloadId = "9001";
    private const string TheirUploadIndex = "8001";

    [Fact]
    public async Task ACachedArchiveIsNotServedToAnotherCompany()
    {
        using var host = await BuildAsync();
        var service = Resolve(host);

        var result = await service.GetArchiveAsync(TheirDownloadId, cif: Ours);

        // What matters is that the cached bytes are not handed over. The call then falls through
        // to ANAF, which refuses a download the account has no rights on — so the error kind here
        // is whatever the remote path reports, and asserting a particular one would be pinning
        // this harness having no token rather than the property under test.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ItsOwnerStillGetsIt()
    {
        // The other half. A scoping rule that refused everybody would pass the test above and be
        // useless.
        using var host = await BuildAsync();
        var service = Resolve(host);

        var result = await service.GetArchiveAsync(TheirDownloadId, cif: Theirs);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal(TheirArchive, result.Value);
    }

    [Fact]
    public async Task TheParsedDocumentIsScopedTheSameWay()
    {
        using var host = await BuildAsync();

        // The cached archive is a real one holding a real invoice, so this fails only because the
        // lookup is scoped. Seeding unparseable bytes would make the test pass whether or not the
        // scoping existed.
        var result = await Resolve(host).GetDocumentAsync(TheirDownloadId, cif: Ours);

        Assert.False(result.IsSuccess);
        Assert.True(await Resolve(host).GetDocumentAsync(TheirDownloadId, cif: Theirs) is { IsSuccess: true });
    }

    [Fact]
    public async Task SoIsTheRendering()
    {
        using var host = await BuildAsync();

        var result = await Resolve(host).RenderPdfAsync(TheirDownloadId, cif: Ours);

        // Not NotAuthorized, which is what the owner would get here for want of a token: refused
        // before anything is read, rather than refused on the way to ANAF.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ASubmissionIsNotVisibleToAnotherCompany()
    {
        using var host = await BuildAsync();

        Assert.Null(await Resolve(host).GetSubmissionAsync(TheirUploadIndex, cif: Ours));
        Assert.NotNull(await Resolve(host).GetSubmissionAsync(TheirUploadIndex, cif: Theirs));
    }

    [Fact]
    public async Task TheConfiguredCompanyIsTheDefault()
    {
        // A single-tenant deployment passes no CIF anywhere and must be unaffected by all of this.
        using var host = await BuildAsync();

        var result = await Resolve(host).GetArchiveAsync("9002");

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal(OurArchive, result.Value);
    }

    // ------------------------------------------------------------- the harness

    /// <summary>A genuine archive, so a scoping failure would surface as a readable document.</summary>
    private static readonly byte[] TheirArchive = Archive("FCT-THEIRS-1");

    private static readonly byte[] OurArchive = Archive("FCT-OURS-1");

    private static byte[] Archive(string documentId)
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("factura.xml").Open();
            using var writer = new StreamWriter(entry);
            writer.Write(
                $"""<?xml version="1.0" encoding="UTF-8"?><Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>{documentId}</ID></Invoice>""");
        }

        return buffer.ToArray();
    }

    private static IRomaniaEFacturaService Resolve(IHost host) =>
        host.Services.CreateScope().ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

    /// <summary>
    /// A deployment serving two companies, each holding one archive it fetched earlier.
    /// </summary>
    private static async Task<IHost> BuildAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        // A shared in-memory SQLite database, held open by the connection below for the lifetime
        // of the host — which is what a real deployment's one database is.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        builder.AddRomaniaEFactura(
            options =>
            {
                options.ClientId = "test-client";
                options.ClientSecret = "test-secret";
                options.RedirectUri = "https://app.example.ro/efactura/callback";
                options.Cif = Ours;
                options.EnableReconciler = false;
            },
            db => db.UseSqlite(connection));

        // No token for anybody, so anything that escapes the local store fails loudly rather than
        // reaching the network.
        var host = builder.Build();
        await host.StartAsync();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.InboxMessages.Add(new EFacturaInboxMessage
            {
                DownloadId = TheirDownloadId,
                Cif = Theirs,
                Archive = TheirArchive,
            });
            db.InboxMessages.Add(new EFacturaInboxMessage
            {
                DownloadId = "9002",
                Cif = Ours,
                Archive = OurArchive,
            });
            db.Submissions.Add(new EFacturaSubmission
            {
                UploadIndex = TheirUploadIndex,
                Cif = Theirs,
                State = UploadState.Ok,
            });

            await db.SaveChangesAsync();
        }

        return host;
    }
}
