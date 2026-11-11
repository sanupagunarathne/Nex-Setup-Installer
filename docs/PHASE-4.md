# Phase 4 — installation engine (0.4.0)

## Start a software queue

Update dev and run `dotnet run --project NexInstaller.App -c Release` on Windows.
Import a licensed, complete offline installer as in Phase 3. In Software Library,
select available packages and Add selected to queue. The shared queue appears
below the library diagnostics and also on Font Library. Remove individual items
or clear it before starting. It runs in insertion order. Continue after a failed
item is off by default. Retry is explicit: review results, remove successful items,
and start remaining items again. Nothing is automatically retried or uninstalled.

Start queue triggers per-item preflight, then a confirmation of the current name,
ID, executable/source, hash, arguments, mode and elevation expectation. NEX verifies
all declared payload SHA-256 values and paths, metadata, unique package identity,
OS architecture, and administrator requirements. Missing or invalid inputs block
execution. Metadata is reread from the library rather than trusting the list on
screen. Declared files, manifests, and any profile file stay open with read-only
sharing during the item; on Windows this denies writes/deletion of those files.
The plan is checked again after confirmation and must match the confirmed plan.
Filesystem checks are snapshots, not a sandbox or substitute for trusting software.
NEX does not validate software licenses or Authenticode publishers automatically.

Manual EXEs receive zero added arguments and launch their normal UI. EXE launchers
can exit before child installers finish, so wait for all child installation windows
and answer the manual completion question truthfully. A zero launcher exit code
alone does not establish success. MSI manual mode uses the system msiexec with
`/i`, `/norestart`, and `REBOOT=ReallySuppress`; it does not add quiet mode.
Declared administrator requirements trigger normal Windows UAC through runas.
An EXE/MSI's own manifest or Windows Installer can also request elevation. Cancelling
UAC prevents that item from launching and stops the queue. No security product,
SmartScreen, licensing prompt or UAC control is disabled, suppressed or bypassed.

Stop queue cancels remaining work and preflight but waits for any already-running
process to exit; installers are never killed or abandoned to start the next item.
The normal close button is blocked while a queue is active. Forced shutdown/process
termination can still interrupt installation; inspect Windows and logs before retry.
Queues are in memory, have at most 1000 distinct items, and are not automatically
resumed after a crash. Different app instances can have separate software queues;
Windows Installer can report busy (1618). Do not start concurrent software queues.

Exit 0 means success under the configured mapping, 3010 reports restart required,
and 1641 reports that the installer initiated a restart and stops the queue. NEX
never calls restart APIs. Manual third-party installers may offer a reboot: decline
that option. Unattended profiles must be verified to suppress reboot. NEX cannot
stop a third-party program that ignores its own documented suppression options.
Nonstandard vendor exit codes must be reviewed in the profile, not guessed.

## Verified unattended profiles

No EXE silent arguments or operational profiles ship with the app. Import defaults
to Manual. Prepare local `installation-profiles.json` beside NexInstaller.exe
(or the build output executable during development), following
schemas/installation-profiles.schema.json. The file is deliberately ignored by Git.
The example in examples/installation-profiles.example.json is deliberately disabled
and non-operational; its arguments are empty and noRestartVerified is false.

An authorized technician must check vendor documentation, license unattended-use
rights, complete offline media, exact Windows version and architecture, process
completion semantics, restart suppression, and success/restart exit codes on an
isolated Windows test system. Only then supply literal vendor-verified argument
tokens, actual verification date/evidence, and noRestartVerified=true. This is local
technician attestation, not cryptographic certification by NEX or automatic discovery.
Profile ID, package ID, exact version, architecture, installer type, and entrypoint
SHA-256 must match the selected manifest. Any profile file errors fail closed.
Never put license keys, tokens, customer identities, or secrets in shared profiles.

Set that package manifest's installationMode to VerifiedUnattended and its
verifiedProfileId to the reviewed profile ID while no queue is active. There is
no profile editor yet. MSI profile arguments may only be public property assignments;
NEX owns /i, /qn, /norestart and REBOOT=ReallySuppress. A profile cannot override
REBOOT or supply other msiexec switches. EXE arguments are not interpreted by a
command shell or interpolated. Profiles must not reference unverified external
payloads or online bootstrap downloads. The entire declared package is hashed
before execution, but NEX cannot interpret every vendor-specific argument syntax.

## Install fonts for the current user

Font Library retains license acknowledgement. Select a font and queue it. Preflight
rechecks its selected content hash and safe library-relative path; changed fonts
must be refreshed and selected again. Confirmation identifies the account running
NEX: installation applies to that account, not necessarily another logged-in user
if NEX was started with different credentials.

The handler copies the verified bytes into that user's LocalAppData Microsoft
Windows Fonts folder, verifies the copy, and registers the face in HKCU's Windows
font registry. It loads the font resource and broadcasts WM_FONTCHANGE. New files
are rolled back if resource addition or registration fails. A different existing
face registration or conflicting destination is preserved and reported as failure;
NEX does not replace fonts or write HKLM/system fonts. A matching installed face
is treated as already present. A per-user mutex prevents cooperating NEX instances
from registering fonts concurrently. Some apps require reopening to see a font.
Font preview and registration use native Windows/WPF APIs and require Windows tests.

## Local logs and limits

Each queue writes structured JSONL session.start, item.start, item.result and
session.complete events to logs/installations-YYYY-MM-DD.jsonl beside the app,
falling back to LocalAppData/NexInstaller/logs. Events include UTC timestamps,
session/event IDs, package IDs, outcomes, exit codes, restart requirements and plan
fingerprints. Customer identity and actual command arguments are not logged by NEX.
NEX records its own lifecycle/results, not vendor stdout/stderr or private MSI logs.
Both logging destinations unavailable stops further launches. If logging fails
after execution, inspect the completed item before retrying; execution is not rolled
back. Full history browsing, export and presets are Phase 5 work. Log retention,
portable publication and complete Windows release acceptance are Phase 6 work.

## Validation and branches

2026-11-11, Linux x64, .NET SDK 10.0.100. Restore and Release cross-build passed
with 0 warnings/errors before each feature merge:

| Branch | Tests passed |
|---|---:|
| feat/installation-engine | 102 |
| feat/windows-installation-handlers | 107 |
| feat/installation-queue-ui | 110 |

Tests use fake process executors and fake font registration APIs. No installers,
UAC prompts, font registration, real OS font modifications, or restarts occur in
tests. Coverage includes queue ordering/failure policy, decline and preflight blocks,
hash tampering, profile bindings/evidence, confirmation-time plan changes, restart
exit codes, cancellation after current execution, font conflict/rollback, log
fallback/failure, queue deduplication, font license gating, and queue UI state.
C# and WPF XAML compile here; native process launching, UAC, SmartScreen behavior,
Windows read-sharing enforcement, shell quoting, font rendering/registration,
registry behavior, actual offline vendor installers and physical USBs are not
runtime-validated. Before operational shop use, validate on disposable Windows
10/11 x64 test systems with licensed offline media and normal security protections.

Each feature originates from dev, is tested and committed separately, and merges
with --no-ff. Branches are preserved and pushed with dev to the existing GitHub
repository. main is unchanged. No real installers, fonts, profiles, logs, build
output or customer data are committed. Next: Phase 5 presets, history and reports.
