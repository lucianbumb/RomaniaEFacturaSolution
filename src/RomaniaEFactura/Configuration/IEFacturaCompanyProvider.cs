namespace RomaniaEFactura.Configuration;

/// <summary>
/// Tells the library which company the current scope is acting for.
/// </summary>
/// <remarks>
/// <para>
/// A single-company application configures <see cref="EFacturaOptions.Cif"/> and needs none of
/// this. An application where each of its own registered businesses connects its own e-Factura
/// authorization has no single company to configure: the CIF belongs to whichever business the
/// request concerns, and only the host knows how a signed-in person maps to one.
/// </para>
/// <para>
/// Register it scoped, and resolve it from whatever already establishes the current business —
/// the route, a claim, a tenant context. Every call the library makes then uses that company
/// without each call site having to thread a CIF through.
/// </para>
/// <para>
/// <b>Deliberately synchronous.</b> It is consulted from <c>BuildAuthorizationUrl</c> and from the
/// transport's own company resolution, neither of which is async, and making them so to
/// accommodate a lookup here would push an await into every caller for a value the host has
/// almost always already resolved. If yours needs I/O, do it once when the scope is created and
/// return the cached answer.
/// </para>
/// </remarks>
public interface IEFacturaCompanyProvider
{
    /// <summary>
    /// The company this scope acts for, or <see langword="null"/> when there is none — an
    /// anonymous request, or a background scope that has not been told.
    /// </summary>
    /// <remarks>
    /// Returning null is not an error. It falls back to <see cref="EFacturaOptions.Cif"/>, and
    /// only when that is empty too does a call fail, naming both.
    /// </remarks>
    string? GetCurrentCif();
}
