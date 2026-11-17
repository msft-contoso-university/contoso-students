# Contoso students

This is an incomplete service slice, not a deployable replacement. It provides
authenticated student record lookup, list/search, create and update with a
versioned OpenAPI contract. Delete/reference integrity, cross-service
composition and live Azure SQL validation are not yet implemented.

The API requires `AzureAd:TenantId`, `AzureAd:Audience`, and
`ConnectionStrings:Students`. Configure SQL managed identity externally and
use a distinct migration identity; the runtime does not create or seed its
database. The API registration must emit the `idtyp` optional access-token
claim for app-only lookup tokens, and only intended managed identities may
receive the `Students.Lookup` app role, which permits only GET by ID.
Writes require a non-default enrollment date; its supplied clock value is
stored unchanged and emitted as a UTC-designated date-time. The migration
rejects existing default dates rather than silently changing them; inspect and
correct such data explicitly before migration.
Delegated users require both the
`students.read` scope and a `Reader` or `Registrar` role.
Outside Development and Testing, `APPLICATIONINSIGHTS_CONNECTION_STRING`
is required. Azure Monitor uses `DefaultAzureCredential` and the
`contoso-students` service name; the runtime identity needs appropriate
telemetry ingestion access. No cloud exporter is enabled in local test runs.

The checked-in EF migration is an explicit schema artifact; applying it is a
separate privileged operation, not startup behavior. From the published
package, a separately privileged in-network bootstrap can run
`dotnet Contoso.Students.Api.dll --migrate` or append `--seed-demo` to apply
the migration and `scripts/seed-students.sql`. The connection string must name
the migration identity, not the runtime identity. The seed inserts three fixed synthetic records only on an empty
database, records a transactional marker, and is a no-op on later runs; it
refuses to seed pre-existing unmarked records. Its DDL is never granted to the
runtime identity. It has not yet been validated against Azure SQL.
No Entra app, database, consent or role
assignment is created by this repository.

Run the API unit and in-memory HTTP tests with `dotnet test Contoso.Students.sln`.
These tests replace authentication and SQL with test-only implementations and
do not establish real Entra or SQL integration.

When `CONTOSO_PRODUCER_SAMPLES` names an output directory, the HTTP tests
capture synthetic successful lookup, create, populated-page and empty-page
responses as JSON envelopes with `schema` and `body` fields. CI publishes
them as `producer-response-samples-<commit>-<run-attempt>` for consumer
compatibility checks. These are test-host responses, not Azure evidence.
Candidate CI also runs read-only CodeQL; pull requests require the pinned
engineering story gate. The workflow on this feature branch is not itself
proof that trusted-base branch protection has been configured.

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
