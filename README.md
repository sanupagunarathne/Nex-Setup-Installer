# NEX Installer

Portable, offline Windows software and font installation workspace.

The approved blue workspace now includes reference-aligned Apps and Add files
layouts (0.5.2). See [layout changes and when to add software/fonts](docs/REFERENCE-LAYOUTS.md)
and the [original design update](docs/BLUE-WORKSPACE.md). Existing installation,
verification, preset, history and reporting capabilities remain available.

Phase 5 adds editable JSON installation presets, installation history reconstructed
from local journal events, and JSON/CSV report export. Presets store package IDs
and relative font paths/hashes; missing, unavailable or changed references block
starting a preset. All execution still uses the Phase 4 queue with fresh preflight
and per-item technician confirmation. No sample packages or installation statistics
are bundled.

The dashboard counts validated saved presets and recorded sessions in the last
30 days. History distinguishes finished queues from incomplete/inconsistent records;
finished does not imply every installation succeeded. Exported CSV text is protected
against spreadsheet formula interpretation. No customer identity fields are added.

See the [Phase 5 presets/history guide](docs/PHASE-5.md),
[Phase 4 installation guide](docs/PHASE-4.md), and [Phase 3 imports](docs/PHASE-3.md).
Native Windows execution, UAC and visual acceptance testing remain outstanding;
the Linux validation uses fake installers and Windows cross-compilation.

## Custom library folders

To remove saved files, select an app in **Apps** and use **Delete selected**.
Checked apps are deleted together; when none are checked, the highlighted app
is deleted. In **Fonts**, highlight a font and use **Delete selected font**.
Both actions ask for confirmation, remove affected pending queue entries, and
move library copies into `.trash/<operation-id>/`, preserving relative paths for
manual recovery. Shared installer files are retained while another app manifest
references them. Source files and installed software/fonts are unchanged.
Saved setups keep their references and report missing items until updated or
the corresponding library files are restored. Deletion is blocked during active
installations and imports. **Remove chosen file** clears an unsaved single-app
selection; each installer in a collection draft also has a **Remove** button.

For a batch of installers, open **Add files → Import several apps as a collection /
preset**. Name the collection, choose multiple EXE/MSI files, and review their app
names and versions. Use **Add complete installer folder** for setups that need
supporting files. **Save collection** copies new packages into the named folder;
the checked **Also save as a reusable installation preset** option creates a
setup under **Saved setups** for repeated installations on client laptops.
Existing identical installers are reused without copying or moving them. If an
import fails, completed imports are retained and no new preset is saved; fix the
source and retry. Uncheck the preset option to save only the collection folder.

When adding an app, use **Save in library folder** on the app details step.
Type a name such as **Adobe**, or select a folder used by an earlier import.
Repeat for each installer you want to keep there. Complete installation folders
still retain their supporting files and their chosen EXE/MSI entry point.
Use the folder filter in Apps, select the apps you need, and add them to the
install list for each client.

Fonts have the same optional destination folder field. Choose it before adding
font files or importing a source folder, then use the font folder filter to
browse the saved collection. **Add all fonts shown to install list** queues the
visible collection after license confirmation. Imports only save files;
installation starts separately through the install list.
Both font picker buttons open even when the license checkbox is unchecked. After
source selection, license confirmation is requested before any copying; cancelling
the picker or declining confirmation leaves the library unchanged.

Named destinations are stored beneath `packages/folders/<name>/` and
`fonts/folders/<name>/` in the portable library. Each import retains its own
package ID or font hash directory to prevent filename collisions. Folder names
are single names of up to 80 characters, with Windows filename rules. Leave the
field blank to keep using unfiled storage. Existing libraries remain readable,
and duplicate content is still rejected across the library. Folders become
available in the picker after a successful import; existing files are not moved.

The dark theme uses `#011526` for its background, `#025373` for surfaces,
`#F2F0F2` for text, `#79C4F2` / `#048ABF` for blue accents, and `#FFCA29` for actions.
Empty text-field hints disappear on focus and return when an empty field loses
focus.

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
no hardcoded drive letters. Configuration is read-only from the UI.

Dashboard software counts include only successfully parsed, unique-ID,
architecture-compatible packages whose declared files passed path and readability
checks. Missing or incompatible packages remain visible as unavailable. Bad
manifests appear in diagnostics. This does not verify SHA-256 contents or establish
that an installer is safe or licensed; installation requires a separate queue action and confirmation. Use Package Manager to verify hashes explicitly;
software availability counts still reflect metadata and file checks, not a cached
verification result.
The font tile counts files; the preset tile counts validated preset JSON; the
session tile counts recorded sessions in a rolling 30-day window. Warnings identify
partial inventory/history reads.
No sample statistics or real installers are shipped.

During `dotnet run`, the default active library is beside the executable under
`NexInstaller.App/bin/Release/net10.0-windows/win-x64/package-library`, rather than
the repository-root library. Check Settings for the exact active path. Copy your
external library there or configure `libraryRoot` appropriately; do not commit
machine-specific paths. An empty library correctly shows zero software packages.
Reading and discovery never create missing folders or modify library contents.

Structured JSONL logs use `logs` beside the executable, falling back to
`%LOCALAPPDATA%\NexInstaller\logs` if necessary. Logging failure does not block
startup; Settings reports its availability. Logs contain application events and
exception types, not customer identities or exception payloads. Inventory notices
and Settings show local paths. Logs remain local; rotation and retention are
future work. The shell runs `asInvoker`; an installer requiring elevation uses a standard Windows UAC request. Fonts are installed for the account running NEX.

See [portable deployment](docs/PORTABLE.md) and
[development scope and validation](docs/DEVELOPMENT.md).

## Git workflow

The linked repository is `sanupagunarathne/Nex-Setup-Installer`. Completed feature
branches are validated locally, committed separately, merged into `dev` with
`--no-ff`, and published to GitHub when authorized. Keep feature and investigation
branches. Stable milestones are merged into the default `main` branch without
rewriting either branch's history.

Installer binaries, fonts, logs, local caches, and customer data are ignored.
Populate the real offline library separately; commit only safe examples,
schemas, configuration, and empty directory markers. Manifest parsing and discovery are implemented in Phase 2. Phase 3 includes imports, SHA-256 checks, font import, and category metadata editing.
Package replacement and additional library management remain future work; app and
font removal are available from their library pages.
