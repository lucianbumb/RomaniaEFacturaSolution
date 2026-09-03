namespace RomaniaEFactura.Configuration;

/// <summary>
/// Decides which company a call is for.
/// </summary>
/// <remarks>
/// The order is most specific first, and it is shared by the service and the transport so the two
/// cannot disagree about whose invoice is being sent.
/// </remarks>
internal static class CompanyResolution
{
    /// <summary>
    /// The failure when nothing names a company. Names every way of supplying one, because the
    /// mistake is a wiring mistake and the reader is looking at a call site that says nothing.
    /// </summary>
    public const string NothingToResolveMessage =
        "No CIF was supplied, no IEFacturaCompanyProvider named one for this scope, and none is "
        + "configured. Pass a cif to this call, register an IEFacturaCompanyProvider for an "
        + "application serving several companies, or set EFacturaOptions.Cif for one that serves one.";

    /// <summary>
    /// Resolves the company: the argument, then the scope's provider, then the configuration.
    /// </summary>
    /// <remarks>
    /// The argument wins so a caller can always be explicit — a background job settling one
    /// company's submission while the ambient scope says nothing, or an administrative screen
    /// acting across companies. The provider comes next because a request that concerns a business
    /// should not silently fall back to whichever company happens to be configured.
    /// </remarks>
    public static string? Resolve(string? cif, IEFacturaCompanyProvider? provider, EFacturaOptions options)
    {
        if (!string.IsNullOrWhiteSpace(cif)) return cif;

        var scoped = provider?.GetCurrentCif();
        if (!string.IsNullOrWhiteSpace(scoped)) return scoped;

        return options.Cif;
    }
}
