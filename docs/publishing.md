# Publishing, and consuming what is published

Two packages go to **GitHub Packages**, together, from a tag:

```
RomaniaEFacturaLibrary.Abstractions
RomaniaEFacturaLibrary
```

Nothing goes to nuget.org yet. The run against ANAF's own test environment
([#10](https://github.com/lucianbumb/RomaniaEFacturaSolution/issues/10)) is the gate, because it is
the only thing that proves the mock server is faithful — and publishing publicly before that would
be publishing a claim nothing supports.

## Cutting a release

The version lives in one place, `Directory.Build.props`:

```xml
<VersionPrefix>3.0.0</VersionPrefix>
<VersionSuffix>alpha.1</VersionSuffix>
```

Bump it, merge that, then tag the commit:

```powershell
git tag v3.0.0-alpha.2
git push origin v3.0.0-alpha.2
```

**The tag must agree with `Directory.Build.props`.** If it does not, the workflow stops before
publishing rather than shipping a package whose version contradicts its tag — a mistake that is
almost impossible to undo once someone has restored it.

To see what a release would do without doing it, run the workflow manually: `dry_run` defaults to
true, so it builds, tests, packs and verifies, and pushes nothing.

## What the workflow checks before it pushes

It runs the **whole gate**, not a subset, because publishing something CI would have rejected is
the one failure mode a release must not have:

- the full test suite, with the ANAF validator oracle and a real PostgreSQL server
- an assertion that neither of those **skipped** — both go quiet when their dependency is absent,
  and a release is the worst place for that to pass unnoticed
- no vulnerable package references
- both packages carry the tagged version
- **the contracts package declares no infrastructure dependency**, checked on the packed `.nuspec`

That last one is not redundant with `AbstractionsDependencyTests`. That test reads the project file
and the compiled assembly; this reads what a consumer actually installs, and a package can declare
a dependency the assembly never uses.

It runs on a GitHub-hosted runner, deliberately. A release must not depend on one workstation being
awake, and a hosted run is the only proof the packages build on a clean machine.

## Consuming them

### The catch

**GitHub Packages requires a token to restore, even for a public package**, and accepts only
*classic* personal access tokens — not fine-grained ones. There is no anonymous access. So every
machine and pipeline that restores these needs a PAT with the `read:packages` scope.

That is a property of GitHub Packages, not of this library. It is the reason to move to nuget.org
once #10 is done.

### NuGet.config

Put this beside your solution. Never commit the token.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="efactura" value="https://nuget.pkg.github.com/lucianbumb/index.json" />
  </packageSources>

  <packageSourceCredentials>
    <efactura>
      <add key="Username" value="%GITHUB_USERNAME%" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </efactura>
  </packageSourceCredentials>

  <!--
    Without this, NuGet asks every source about every package. A transient 403 from GitHub Packages
    then fails a restore of something that only ever lived on nuget.org, and the error names the
    wrong package. Mapping sends each package to exactly one source.
  -->
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="efactura">
      <package pattern="RomaniaEFacturaLibrary*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

```powershell
$env:GITHUB_USERNAME = "lucianbumb"
$env:GITHUB_PACKAGES_TOKEN = "<classic PAT with read:packages>"
```

### Installing

These are prereleases, so they need an explicit version or `--prerelease`:

```powershell
dotnet add package RomaniaEFacturaLibrary --version 3.0.0-alpha.1
```

Reference `RomaniaEFacturaLibrary` and you get `Abstractions` with it. Reference `Abstractions`
alone from a layer that may not depend on HTTP or persistence — it brings **nothing** with it, which
the release verifies on the packed `.nuspec`.

### In someone else's CI

`GITHUB_TOKEN` publishes into *this* repository's registry, but it cannot read another
repository's. A pipeline in a different repository needs the classic PAT as a secret:

```yaml
- run: dotnet nuget add source https://nuget.pkg.github.com/lucianbumb/index.json
         --name efactura --username lucianbumb
         --password ${{ secrets.EFACTURA_PACKAGES_TOKEN }} --store-password-in-clear-text
```

## Symbols

The PDB is **embedded in the assembly** rather than shipped as a `.snupkg`, because GitHub Packages
does not serve symbol packages. Debugging works with no symbol server; the assembly is a little
larger. Source Link is on, so a debugger can fetch the matching source from GitHub.
