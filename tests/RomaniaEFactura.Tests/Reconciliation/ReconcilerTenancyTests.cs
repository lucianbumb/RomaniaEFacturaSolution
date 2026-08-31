using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Reconciliation;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Tests.Reconciliation;

/// <summary>
/// Which company's authorization the reconciler polls a submission with.
/// </summary>
/// <remarks>
/// <para>
/// <c>stareMesaj</c> and <c>descarcare</c> used to resolve the configured company and ignore the
/// submission's own. In a deployment serving several companies — which the per-call CIF override
/// exists for — every submission but one was therefore polled with the wrong account.
/// </para>
/// <para>
/// ANAF answers <c>NoRights</c> to that, so the submission is retried on its widening schedule and
/// never settles, while the log says a rights problem: it reads as misconfiguration rather than as
/// a bug, which is what makes it worth a test rather than a comment.
/// </para>
/// </remarks>
public class ReconcilerTenancyTests
{
    private const string Configured = "12345674";
    private const string Other = "19867700";

    [Fact]
    public async Task ASubmissionIsPolledAsTheCompanyThatMadeIt()
    {
        var api = new RecordingApi();
        var reconciler = await BuildAsync(api, submissionCif: Other);

        await reconciler.RunOnceAsync();

        Assert.Equal(Other, api.StatusCif);
    }

    [Fact]
    public async Task ItsArchiveIsFetchedAsThatCompanyToo()
    {
        var api = new RecordingApi();
        var reconciler = await BuildAsync(api, submissionCif: Other);

        await reconciler.RunOnceAsync();

        Assert.Equal(Other, api.DownloadCif);
    }

    [Fact]
    public async Task TheConfiguredCompanyIsStillUsedForItsOwn()
    {
        // A single-tenant deployment must see no change at all.
        var api = new RecordingApi();
        var reconciler = await BuildAsync(api, submissionCif: Configured);

        await reconciler.RunOnceAsync();

        Assert.Equal(Configured, api.StatusCif);
    }

    // ------------------------------------------------------------- the harness

    private static async Task<EFacturaReconciler> BuildAsync(IAnafApiClient api, string submissionCif)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<EFacturaDbContext>().UseSqlite(connection).Options;
        var db = new EFacturaDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Submissions.Add(new EFacturaSubmission
        {
            UploadIndex = "5001",
            Cif = submissionCif,
            State = UploadState.InProgress,
            NextPollAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        return new EFacturaReconciler(
            new SingleScopeFactory(db, api),
            Options.Create(new EFacturaOptions { Cif = Configured }),
            TimeProvider.System,
            NullLogger<EFacturaReconciler>.Instance);
    }

    /// <summary>Records which company each call was made for, and answers as ANAF would.</summary>
    private sealed class RecordingApi : IAnafApiClient
    {
        public string? StatusCif { get; private set; }

        public string? DownloadCif { get; private set; }

        public Task<AnafResult<MessageStatus>> GetStatusAsync(
            string uploadIndex, string? cif = null, CancellationToken cancellationToken = default)
        {
            StatusCif = cif;
            return Task.FromResult(AnafResult<MessageStatus>.Success(
                new MessageStatus(UploadState.Ok, "9001", "ok")));
        }

        public Task<AnafResult<byte[]>> DownloadArchiveAsync(
            string downloadId, string? cif = null, CancellationToken cancellationToken = default)
        {
            DownloadCif = cif;
            return Task.FromResult(AnafResult<byte[]>.Success([1, 2, 3]));
        }

        public Task<AnafResult<UploadReceipt>> UploadAsync(
            string xml, string? cif = null, UploadOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnafResult<IReadOnlyList<AnafMessage>>> ListMessagesAsync(
            int days, string? cif = null, MessageFilter filter = MessageFilter.All,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnafResult<MessagePage>> ListMessagesAsync(
            DateTimeOffset from, DateTimeOffset to, int page = 1, string? cif = null,
            MessageFilter filter = MessageFilter.All,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnafResult<AnafValidationOutcome>> ValidateAsync(
            string xml, AnafStandard standard = AnafStandard.Ubl,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnafResult<byte[]>> RenderPdfAsync(
            string xml, AnafStandard standard = AnafStandard.Ubl, bool skipValidation = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Hands the reconciler the one context and API this test set up.</summary>
    private sealed class SingleScopeFactory(EFacturaDbContext db, IAnafApiClient api)
        : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(EFacturaDbContext)) return db;
            if (serviceType == typeof(IAnafApiClient)) return api;
            return null;
        }

        public void Dispose()
        {
            // The context outlives each scope here on purpose: one test, one database.
        }
    }
}
