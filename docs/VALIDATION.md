# Phase 1 validation record

Validated on 2026-11-11 in Linux x64 (Debian 13) using .NET SDK 10.0.100.

| Check | Observed result |
|---|---|
| Solution restore | All four projects restored successfully |
| Release solution build | Passed, 0 warnings, 0 errors, including Windows WPF cross-build |
| Unit tests | 28 passed, 0 failed, 0 skipped |
| Whitespace review | `git diff --cached --check` passed |
| Ignore rules | Installer/font payloads, build output, logs, and published artifacts excluded |
| Git remotes | None configured |
| WPF Windows runtime and visual checks | Not performed; this environment is Linux |
| Portable publish and USB acceptance | Deferred to Phase 6 |

Commands, with the locally installed SDK and its caches redirected to writable
workspace directories:

```text
dotnet restore NexInstaller.sln
dotnet build NexInstaller.sln --configuration Release --no-restore
dotnet test NexInstaller.Tests/NexInstaller.Tests.csproj --configuration Release --no-build --no-restore
```

The first installed SDK (10.0.404) could print `--info` but failed during template
creation with an inaccessible `NuGet-Migrations` named mutex and a read-only home
directory. SDK 10.0.100 completed setup and validation with workspace-local CLI
state and caches. No claim is made that 10.0.404 commands succeeded.

The first solution build found xUnit analyzer error `xUnit2031` in a test
assertion. The assertion was corrected to use the predicate overload of
`Assert.Single`; the subsequent build and all tests passed. No tests invoke
installers or install fonts. Test font and JSON fixtures are temporary dummy
files, never product library content or sample dashboard statistics.
