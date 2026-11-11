# Phase 2 — software package library

## Delivered behavior

Discovery runs on application launch and through Refresh library. All top-level
`.json` manifests in the active root's `manifests` directory are parsed separately.
One malformed manifest does not suppress other packages. A missing or unreadable
library is reported without creating folders or writing library contents.

Valid manifests appear in Software Library. Missing/unreadable files, reparse
points or symbolic links in package paths, incompatible architectures, and
duplicate package IDs make a package unavailable. Duplicate IDs make every
conflicting manifest unavailable; discovery never silently chooses a winner.
X64 Windows supports Any, X64, and X86 metadata; Arm64 is unavailable on X64.
Compatibility is metadata-based, not executable binary analysis.

Search matches name, version, ID, and description without case sensitivity.
Category and architecture filters compose with search. Categories come from
manifest labels, are deduplicated without case sensitivity, and sorted. Category
creation/editing remains Phase 3 work. Unavailable rows remain inspectable but
cannot be selected. Select available acts on visible rows; Clear selection clears
all rows, including those hidden by filters. Selection survives filtering and
refresh by package ID only while the package remains available.

The preparation list is held in memory and does not persist or execute. Details
show metadata, relative paths, declared hashes, readable size, and unavailability
reasons. Readable size may be partial for unavailable packages. Hashes are not
verified, and no installer is executed. A declared profile ID is not a verified
unattended installation profile. Imports and queue execution remain disabled.

Software counts now show available packages. Fonts, presets, and history still
show filename counts until their respective phases. On unexpected discovery
failure, stale software results and selections are cleared, the software count
becomes unavailable, and Refresh permits recovery.

## Try on Windows

```powershell
git switch dev
git pull --ff-only
dotnet run --project NexInstaller.App --configuration Release
```

Find the active library root in Settings. For source builds, the default root is
under the executable's build output directory, not the repository root. Supply
your own licensed installer under `packages/<folder>/`, and a manifest under
`manifests/` following `examples/package-manifest.example.json`. Compute the real
hash in PowerShell using `Get-FileHash <installer-path> -Algorithm SHA256`.
Refresh and browse Software Library. No binaries or working sample packages are
bundled; the example lives outside the scanned library and uses a placeholder
hash. Do not mistake file availability for content verification.

Recommended manual Windows checks: mixed-case search, combined filters, keyboard
navigation, checkbox selection, hidden selections, missing-file diagnostics,
refresh after file removal, resizing and display scaling. Change a manifest ID to
create a duplicate and confirm both packages become unavailable. Test on offline
and read-only media. No Windows UI, UAC, USB device, or visual acceptance testing
was possible in this Linux environment.

## Validation and Git

On 2026-11-11, .NET SDK 10.0.100 on Linux x64:

| Feature branch | Restore / Release build | Unit tests |
|---|---|---|
| feat/package-manifests | Passed, 0 warnings / errors | 58 passed |
| feat/package-discovery | Passed, 0 warnings / errors | 69 passed |
| feat/software-library | Passed, 0 warnings / errors | 74 passed |

Tests use temporary dummy payloads, fake services, and actual filesystem scans;
none invoke an installer. They cover parsing, invalid metadata, duplicate JSON
properties, path traversal, reserved names, missing payloads, duplicate IDs,
architecture checks, cancellation, link rejection on Linux, composable filters,
selection rules, and dashboard refresh after file deletion. The link creation
test does not create a Windows link because that requires developer mode or
permissions; Windows link behavior needs manual validation. WPF C# and XAML were
cross-compiled, not run. JSON Schema is a documentation/editor aid; the runtime
parser also applies semantic rules described in PACKAGE-MANIFESTS.md.

Each feature starts from a clean dev branch, is validated, committed with a
Conventional Commit, and merged back using a merge commit. Feature branches are
preserved and published with dev. GitHub main retains its existing initial commit;
it is not merged or rewritten. Source history is retained. No binaries or
customer data are committed.

Next: Phase 3 imports, SHA-256 calculation/verification, duplicate content detection,
font imports, and category/library editing, each on its own feature branch.
