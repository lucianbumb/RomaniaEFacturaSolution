namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Hands the library a client that talks to the in-process mock.
/// </summary>
/// <remarks>
/// The library asks <see cref="IHttpClientFactory"/> for its client, so substituting the factory
/// is enough to redirect every call without the library knowing it is under test.
/// </remarks>
public sealed class MockHttpClientFactory(MockAnafFixture fixture) : IHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(string name) => fixture.CreateClient();
}
