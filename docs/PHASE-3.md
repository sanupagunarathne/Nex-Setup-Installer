> Historical Phase 3 record. Installation is now implemented: [Phase 4](PHASE-4.md).

# Phase 3 — offline import system (0.3.0)

## Using the new UI

Update `dev`, then run `dotnet run --project NexInstaller.App -c Release` on
Windows. Check Settings for the active library root. During development this
is beside the build output unless configured otherwise, not the source folder.

Package Manager provides three guided steps: choose an EXE/MSI or complete
folder, enter package metadata, then review and import. For folders, choose or
type a relative EXE/MSI entry point. All supporting files and subfolders are
included. Supply name, version, architecture, category, optional description,
and administrator requirement. Enter a custom category or choose an existing
one. Installation mode defaults to Manual and is the only available import mode;
verified unattended profiles will arrive with Phase 4. Nothing is executed.

The source is checked and hashed, existing manifests are checked for duplicate
content, files are copied into a unique staging directory, and copied SHA-256
values must match the source snapshot. A source changing during copying fails
without publishing the package. Payloads move into a generated package-ID folder;
the JSON manifest is published last. The UI refreshes discovery and counts.

Duplicate package detection compares the sorted multiset of recorded file hashes
for the complete package; renaming a source does not avoid duplicate detection.
It does not prove an existing package is intact: Verify package hashes reads the
current payloads and reports missing/unreadable files or SHA-256 mismatches.
Invalid existing manifests stop mutations/verification with an error so they
can be repaired rather than silently ignored. Verification does not execute or
license an installer and is not cached as a promise of future integrity.
Phase 4 must recheck paths/hashes and permissions immediately before execution.

Category management creates saved categories in library-root/categories.json,
combines these with categories used by manifests, removes only unused categories,
and assigns a category to the package row currently inspected in Software Library.
Choose the row, return to Package Manager, choose a managed category, and assign.
Assignments replace a single manifest atomically. In-use removal is rejected.
Category names are trimmed-input labels of 1–80 characters; duplicates are compared
without case sensitivity. Bad existing category JSON is never overwritten.

Font Library requires explicit font-license acknowledgement. Import multiple
TTF/OTF files or recursively import a folder. Unsupported extensions are skipped
in folders; empty folders are reported. Each font is structurally checked for a
supported sfnt signature and in-bounds table directory, hashed, copied through a
temporary file, rehashed, and published under a content-hash directory retaining
its filename. Duplicates are detected against current font contents and within
the batch. Imported, duplicate, and failed files each receive a result. Structural
validation is not a full font sanitizer or proof of font licensing. Select a font
for a WPF preview; unreadable/unsupported previews fall back to the default font.
No font is registered in Windows. Font installation and its permission/result
handling belong to Phase 4.

## Error recovery and limitations

Imports reject linked/reparse-point source paths, unsafe portable file names,
source/library nesting, traversal, unsupported entry points, and case-insensitive
payload path collisions. Libraries are locked exclusively for mutations to
prevent cooperating NEX instances writing concurrently; lock files contain no
customer details and can remain on disk. Read-only locations or busy locks produce
UI errors. Payload files and real local category data stay outside Git.

Cancel is available during hashing/copying. Package cancellation rolls back the
new staged payload; font batches commit one file at a time, so earlier successful
fonts remain after cancellation. Refresh to see those completed imports. Existing
library files are never overwritten by imports. Empty newly created folders and
lock files may remain. Power loss/process termination can leave staging files or
an unreferenced payload folder; no unfinished JSON manifest is discovered. Inspect
these only after closing all NEX instances. Do not remove active staging data.
Filesystem checks are snapshots and do not replace execution-time security checks.

Import progress shows the current stage/file with an indeterminate progress bar.
Single imports and recursive scans are limited to 10000 files. Source paths must
be local/readable; unsupported font collections such as TTC are not imported.
No automatic UAC, restart, licensing, security bypass, or online service is used.
The library must have free space for staged copies; disk errors are reported.
Full package editing/replacement/removal, persisted queues, and Windows font
installation remain future work. Categories are the metadata editing in this phase.

## Validation and version control

2026-11-11: Linux x64, .NET SDK 10.0.100. Each branch passed restore and Release
solution build with 0 warnings/errors before its merge into dev:

| Feature branch | Tests passed |
|---|---:|
| feat/package-importer | 78 |
| feat/font-importer | 80 |
| feat/category-management | 84 |
| feat/import-workspaces | 91 |

Tests use dummy installer bytes and structural font fixtures; no real installers
are launched, fonts registered, or fixture fonts rendered. Tests cover copies,
folder preservation (including a source .manifest.json), renamed duplicates,
source tampering, pre-start and mid-copy cancellation, rollback on blocked paths,
unsafe paths, source/library nesting, link rejection on Linux, font validation
and duplicates, license gating, category assignment/removal, invalid category
configuration, import confirmation cancellation, dashboard refresh, and import completion during an existing discovery scan.

The initial package test expected a different exception type for an unsafe path;
its assertion was corrected. Font batch tests exposed an uncaught InvalidDataException;
the importer now reports that failure per file. The UI build initially lacked two
System.IO imports; these were corrected. Final checks passed. Windows dialogs,
font rendering, display scaling, read-only physical USBs, and WPF runtime behavior
have not been tested in this Linux environment and need Windows acceptance tests.

All feature branches are preserved, merged with merge commits, and pushed with
dev to the linked GitHub repository. main is unchanged. No build output, installers,
font files, local configuration, logs, credentials, or customer data are committed.
Next: Phase 4 sequential installation engine, technician confirmation, preflight
hash/path/admin checks, verified profiles, exit codes, restart reporting, and font
installation. No installer execution is part of Phase 3.
