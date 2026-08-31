using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Hosts the mock ANAF server in-process for the duration of a test class.
/// </summary>
/// <remarks>
/// In-process rather than a spawned executable: no port to allocate, no readiness race, and a
/// failing assertion surfaces the server's own exception rather than a connection reset.
/// </remarks>
public sealed class MockAnafFixture : WebApplicationFactory<Program>
{
    /// <summary>A CIF with a valid control digit, used as the account under test.</summary>
    public const string Cif = "12345674";

    /// <summary>The API base path, matching ANAF's test environment layout.</summary>
    public const string ApiBase = "/test/FCTEL/rest";

    /// <summary>A client with a bearer token already attached.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "mock-access-token-initial");
        return client;
    }

    /// <summary>Clears mock state so one test cannot leak into another.</summary>
    public async Task ResetAsync()
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("/__mock/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Sets how many polls report "in prelucrare" before an upload resolves.</summary>
    public async Task SetPollsBeforeResolutionAsync(int count)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync($"/__mock/polls-before-resolution/{count}", content: null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Seeds a message that did not come from an upload, as a received invoice would.</summary>
    public async Task<SeededMessage> SeedIncomingMessageAsync(
        string xml,
        bool hideId = false,
        int? createdDaysAgo = null,
        string tip = "FACTURA PRIMITA")
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/__mock/messages", new
        {
            Cif,
            Xml = xml,
            Tip = tip,
            HideId = hideId,
            CreatedDaysAgo = createdDaysAgo,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SeededMessage>())!;
    }

    /// <summary>Identifiers for a seeded message.</summary>
    public sealed record SeededMessage(string Id, string IdSolicitare, bool HideId);
}
