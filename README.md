# Contoso students

This is an incomplete service slice, not a deployable replacement. It provides
authenticated student record lookup, list/search, create and update with a
versioned OpenAPI contract. Delete/reference integrity, cross-service
composition, deterministic seed and SQL-backed validation are not yet
implemented.

The API requires `AzureAd:TenantId`, `AzureAd:Audience`, and
`ConnectionStrings:Students`. Configure SQL managed identity externally and
use a distinct migration identity; the runtime does not create or seed its
database. The API registration must emit the `idtyp` optional access-token
claim for app-only lookup tokens, and only intended managed identities may
receive the `Students.Lookup` app role, which permits only GET by ID.
Delegated users require both the
`students.read` scope and a `Reader` or `Registrar` role.
Outside Development and Testing, `APPLICATIONINSIGHTS_CONNECTION_STRING`
is required. Azure Monitor uses `DefaultAzureCredential` and the
`contoso-students` service name; the runtime identity needs appropriate
telemetry ingestion access. No cloud exporter is enabled in local test runs.

The checked-in EF migration is an explicit schema artifact; applying it is a
separate privileged operation, not startup behavior. It has not yet been
validated against SQL Server. No Entra app, database, consent or role
assignment is created by this repository.

Run the API unit and in-memory HTTP tests with `dotnet test Contoso.Students.sln`.
These tests replace authentication and SQL with test-only implementations and
do not establish real Entra or SQL integration.

The MSTest characterization tests compile the original student controller and
models from the verified, unchanged reference checkout; no source is copied
into this repository.

Run with .NET 10 SDK and PowerShell 7:

```powershell
.\scripts\test-legacy.ps1 -LegacySourceRoot C:\path\to\app-dotnet-azsql-appsvc
```

The script requires reference commit
`0716ee7262eff2c268ebb72097ceb93e0fc83536` with no tracked modifications.
Tests use EF's in-memory provider and invoke controller actions directly. They
are not SQL integration tests, HTTP/authentication tests, or proof of behavior
parity for the future service. SQL deletion, foreign-key and concurrency
behavior must still be characterized separately.

The reference source is MIT licensed; see [LICENSE](LICENSE).
