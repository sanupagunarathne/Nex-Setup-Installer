> Page labels/layout were subsequently simplified: [approved blue workspace](BLUE-WORKSPACE.md). Existing functionality is retained.

# Phase 5 — presets, history and reports (0.5.0)

## Create and edit presets

On Windows, update dev and run `dotnet run --project NexInstaller.App -c Release`.
Installation Presets now lists saved JSON presets. New preset creates an in-memory
draft; enter a name, select software checkboxes in Software Library or a font row
in Font Library, then return and add those references. Remove individual entries,
edit the name to rename, and Save preset JSON. Changing selection or creating a new
draft asks before discarding actual unsaved edits. Refresh preserves unsaved drafts.
There is no preset-file deletion button; Remove edits items within the preset.

Files are saved under the active library's presets/<id>.json. IDs are lowercase
GUIDs without dashes; names can change without changing identity. Software uses
stable package IDs, not drive paths. Fonts use library-relative TTF/OTF paths and
an expected SHA-256. This supports drive-letter changes when the external library
moves intact. See schemas/installation-preset.schema.json and the safe example
outside the live library. To transfer/load a JSON preset from another USB, copy
its valid <id>.json file into the active presets directory and Refresh library.
Referenced packages/fonts must also be available in that library. No app rebuild
is needed. Filename/ID mismatches, malformed JSON, unknown/duplicate properties,
invalid paths and duplicate request IDs are rejected with diagnostics.

Save writes a temporary file in the same directory, flushes it, and atomically
replaces the destination under the library mutation lock. A malformed existing
file is not overwritten. Read-only, missing-media and disk errors are reported;
preset saving does not silently fall back to local app data because presets belong
to the portable library. Concurrent instances serialize writes; explicitly saving
an edited valid preset replaces its latest contents (there is no version merging).
An empty preset can be saved while preparing it, but cannot be started.

## Start a saved preset

Save pending edits first. Start saved preset on the preset page or select a saved
preset in the Dashboard quick action. Every reference is resolved before the
pending queue is changed: missing/duplicate/unavailable software and missing or
hash-changed fonts block the whole preset. Font license acknowledgement is required
for a preset containing fonts. If a pending queue exists, replacing it requires a
separate confirmation; declining preserves it. An active queue cannot be replaced.

Starting uses the same Phase 4 installation engine. Each item is still freshly
verified and individually confirmed, with normal UAC, profile requirements, exit
handling and no automatic restart. Presets contain no silent arguments or security
approvals. Early preset resolution is not a transaction for actual installs: a
file changing afterward can still cause a later item to fail preflight, and already
completed installations are not automatically rolled back. Failures and partial
queues remain visible. Queue completion (including failure) refreshes inventory
and history. Loading or saving a preset alone does not start any installer.

## Review installation history

Installation History reads Phase 4-compatible installations-*.jsonl journals from
logs beside the app and LocalAppData/NexInstaller/logs. A session can span both
locations when portable logging fails; identical event IDs are merged once, while
conflicting copies are flagged. Current-user fallback logs stay on that Windows
machine and are not copied onto USB automatically. The view is scoped to those
application/fallback locations, not exclusively the currently configured library.

Search by session ID, package ID/name, outcome, exit code or message. Selecting a
session shows its timestamps, requested count, recorded package IDs/kinds/names,
exit codes, outcomes, failures, restart requirements, errors and source log names.
It does not collect customer names, emails, serial numbers or contact information.
Some original installer error messages can contain local paths. An item rejected
before item.start may have an ID/result but no recorded name/kind; these are shown
as not recorded, not guessed from the current library.

Finished means a session.complete event matches its recorded result counts, not
that every item succeeded. Incomplete can mean an active or interrupted queue.
An announced execution without a result remains unknown; never assume success or
automatically retry it. Missing result fields, duplicate/conflicting events, bad
counts and timestamps yield warnings/inconsistent state. Malformed or truncated
lines do not hide valid events. This is a local log viewer, not a signed audit log
or recovery agent; deleting/editing journals can remove or invalidate history.

Dashboard Saved presets counts validated documents, and Recent sessions counts
recorded sessions whose start time is in the last rolling 30 days (including
incomplete/failed sessions). Future-dated entries are excluded from that recent
count. Preset/history diagnostics also appear in library status. No sample counts
are shipped. Fonts still use a file inventory count; software availability does
not mean hashes were verified at that inventory snapshot.

## Export local reports

Select a session, choose JSON or CSV, and choose a destination with the native Save
File dialog. Existing destinations require its normal overwrite confirmation.
Exports contain the selected session snapshot, recorded outcomes and read warnings;
they do not execute code or alter history. JSON uses schemaVersion 1, exportedUtc,
warnings and a session object. CSV includes session state, timestamps, requested
count, item IDs, names/kinds, outcomes, codes, restart flags, messages and warnings.
Unknown results remain explicitly unknown. Times in details/exports are ISO 8601
with offsets; session list summaries use the Windows user's local display format.

CSV fields are quoted/escaped and formula-leading text is prefixed with an
apostrophe to prevent spreadsheet execution. Such strings remain exact in JSON.
CSV is UTF-8 with a BOM for spreadsheet compatibility. Writes use flushed temporary
files and atomic replacement; cancellation or failure does not replace an existing
report. Backend overwrite defaults to false; the UI enables it only after the Save
File dialog accepts the destination. Report extensions must match the chosen format.
Save private reports outside the source repository, or in ignored reports/. Default
nex-installation-report*.json/csv exports are also ignored. Safe schemas/examples
remain tracked. No report is sent to a remote service.

Read bounds are 2 MiB/1000 items per preset, 64 MiB per journal, 256 Ki characters
per event line, and 100000 unique events per history read. Diagnostics are capped
at 200 plus an omission notice. Limits or inaccessible sources can produce partial
history; warnings accompany the UI and exports. Nothing is automatically deleted
or rotated. Retention, complete Windows acceptance and portable publishing are
Phase 6 work.

## Validation and version control

2026-11-11, Linux x64, .NET SDK 10.0.100. Every branch passed restore and Release
solution build with zero warnings/errors before its merge into dev:

| Feature branch | Tests passed |
|---|---:|
| feat/installation-presets | 124 |
| feat/installation-history-reports | 131 |
| feat/presets-history-ui | 138 |

Coverage includes preset serialization/rename/item removal, portable paths,
malformed JSON, duplicate items, cancellation/write failures, missing packages and
changed fonts, history reconstruction, duplicate fallback events, conflicts,
truncated lines, absent outcome fields, incomplete sessions, JSON/CSV exports and
formula escaping, preset start through a fake engine, license gating, queue-replace
cancellation, dirty-draft preservation and actual dashboard counts.

The initial preset test build needed explicit types for params arguments. An export
test exposed an empty JSON wrapper caused by a serialization setting; it was
corrected and guarded by the report test. All final tests pass. No real installer,
font registration, UAC prompt or Windows registry write is executed by tests. WPF
C#/XAML is cross-compiled; native dialog behavior, keyboard/layout/display scaling,
read-only physical USBs and real Windows integration require Windows validation.
The existing GitHub dev progress-bar OneWay binding fix (a2ffe57) was preserved.

Each feature starts from clean dev, is committed separately using Conventional
Commits, and merges with a merge commit. Branches are preserved and pushed with dev.
main remains unchanged. No customer data, real installers, fonts, logs or report
outputs are committed. Next: Phase 6 testing, portable publishing, USB deployment
and Windows release validation. A stable main merge still needs explicit approval.
