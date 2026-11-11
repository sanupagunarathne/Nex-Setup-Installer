# NEX Installer

Portable, offline Windows software and font installation workspace.

Phase 1 implements a .NET 10 WPF shell with MVVM, dependency injection, seven
page layouts, working navigation, read-only filesystem counts, local JSON
configuration, and structured local logging. Import, package discovery and
validation, installation, presets, and history management are future phases.
Their controls are deliberately disabled and labeled in the application.

## Solution

```text
NexInstaller/
  NexInstaller.App/             WPF, view models, navigation, shared theme
  NexInstaller.Core/            Navigation state, library contracts, path settings
  NexInstaller.Infrastructure/  Filesystem inventory, JSON configuration, JSONL logs
  NexInstaller.Tests/           Cross-platform unit tests using fake services
  package-library/
    packages/
    fonts/
    presets/
    manifests/
    history/                   Reserved for future session reports
  docs/
```

## Build and test

Use a .NET 10 SDK. The preferred desktop development environment is Windows
10/11 x64 with Visual Studio's .NET desktop tools or the `dotnet` CLI.

```powershell
dotnet restore NexInstaller.sln
dotnet build NexInstaller.sln --configuration Release --no-restore
dotnet test NexInstaller.Tests/NexInstaller.Tests.csproj --configuration Release --no-build --no-restore
dotnet run --project NexInstaller.App --configuration Release --no-build
```

The application targets `net10.0-windows` and `win-x64`. `EnableWindowsTargeting`
permits cross-compilation on Linux; **WPF execution and visual validation require
Windows**. Tests target `net10.0` and link the framework-neutral view model source
so navigation and refresh behavior can be tested without loading WPF.
No test launches any installer or writes fonts to the operating system.

SDK and NuGet dependency downloads are development-time requirements. The app
has no network client, online package manager, remote API, telemetry, or cloud
database. Development without a connection requires pre-cached SDK targeting
packs and NuGet dependencies. No installer packages are bundled with this source.

## Run with a portable library

Place the external `package-library` directory beside `NexInstaller.exe`, or set
`libraryRoot` in the adjacent `nexinstaller.json` to a relative or absolute path.
Relative roots resolve against the executable location, regardless of the
terminal's working directory. Restart after editing configuration. There are
no hardcoded drive letters. Configuration is read-only from the UI in Phase 1.

Dashboard counts reflect top-level JSON files in `manifests`, `presets`, and
`history`, and recursive TTF/OTF files in `fonts`. They are file inventory, not
validated packages, saved preset models, or verified session records. No sample
statistics are shipped. Missing folders and inaccessible folders produce notices;
inventory never creates folders or modifies library content. Linked font folders
and files are skipped. A failed count is accompanied by a warning rather than
being presented as a healthy empty library.

Structured JSONL logs use `logs` beside the executable, falling back to
`%LOCALAPPDATA%\NexInstaller\logs` if necessary. Logging failure does not block
startup; Settings reports its availability. Logs contain application events and
exception types, not customer identities or exception payloads. Inventory notices
and Settings show local paths. Logs remain local; rotation and retention are
future work. The shell runs `asInvoker` and does not request administrator access.

See [portable deployment](docs/PORTABLE.md) and
[development scope and validation](docs/DEVELOPMENT.md).

## Local Git policy

All source control is local. `main` holds the initial configuration only until
an explicitly requested stable milestone. Feature branches originate from `dev`
and merge back with `--no-ff` only after restore, build, and relevant tests pass.
Keep feature and investigation branches. Do not push, add remotes, publish code,
rewrite history, or merge `dev` into `main` without explicit instruction.

Installer binaries, fonts, logs, local caches, and customer data are ignored.
Populate the real offline library separately; commit only safe examples,
schemas, configuration, and empty directory markers. Real manifests, import
validation, and library editing are intentionally deferred to Phases 2 and 3.
