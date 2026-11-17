# Contoso students

The service has not yet been implemented. These MSTest characterization
tests compile the original student controller and models from the verified,
unchanged reference checkout; no source is copied into this repository.

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
