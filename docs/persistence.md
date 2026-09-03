# Where the library keeps its data

Four tables, in whatever database the host configures:

| Table | What it holds |
|---|---|
| `EFacturaTokens` | One ANAF authorization per company, both tokens encrypted |
| `EFacturaSubmissions` | Documents sent, tracked until ANAF decides, with the signed archive |
| `EFacturaInboxMessages` | Messages seen in the SPV inbox, so each is downloaded once |
| `EFacturaInboxCursors` | How far each company's inbox has been read, and when to read it again |

```csharp
builder.AddRomaniaEFactura(
    configureDatabase: db => db.UseNpgsql(connectionString));
```

## Providers

Exercised in CI against **SQLite** and **PostgreSQL**. The two differ in ways this schema touches
directly, which is why both are tested rather than one being assumed to imply the other:

- **SQLite cannot order by `DateTimeOffset`**, and the library's queries are ordered by time — "what
  is due, oldest first". So the context converts every `DateTimeOffset` to a UTC `DateTime`.
- **Npgsql maps `timestamp with time zone` and refuses a `DateTime` whose kind is not UTC.** The
  workaround for one provider is the thing the other is strict about, so the converter specifies
  `DateTimeKind.Utc` on the way back — and a test asserts an instant survives a round trip through
  a non-UTC offset rather than merely a clock reading.

Another provider will probably work; nothing in the model is exotic. It has not been proven, and
`ExecuteDeleteAsync` — used to remove an authorization — is translated by each provider itself.

## Migrations

**The library ships none, deliberately.** A migration is provider-specific: one generated for
SQLite emits SQL that will not run on PostgreSQL, so committing a set for any single provider would
work for some consumers and quietly mislead the rest, while the presence of a `Migrations` folder
implies it works for everyone.

Two ways to get the schema:

**Let the library create it.** `EnsureEFacturaSchemaAsync` creates the tables directly when no
migrations exist, and applies them when they do. Safe to call on every start.

```csharp
await app.Services.EnsureEFacturaSchemaAsync();
```

**Own it yourself**, which is what an application with its own migration history will want.
Generate migrations against `EFacturaDbContext` from your own project, where the provider is known:

```bash
dotnet ef migrations add EFacturaInitial --context EFacturaDbContext --output-dir Migrations/EFactura
```

`EnsureEFacturaSchemaAsync` then applies yours rather than creating tables behind your back — it
checks for a migration history first and defers to it.

## What is encrypted, and what is not

The two token columns hold ciphertext, protected with `IDataProtector` under a versioned purpose
string, so a database backup or read access to the table does not hand over the ability to file
invoices as the company.

Invoice content is **not** encrypted: the application needs to read it. A row that cannot be
decrypted — keys rotated away, a backup restored onto a host without them — is treated as no
authorization rather than as an error, because that is what it means in practice.

A multi-instance deployment must give data protection **shared, persisted key storage**, or each
instance will be unable to read what the others stored.
