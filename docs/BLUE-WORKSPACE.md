# Approved blue workspace redesign — 0.5.1

This applies the user-approved design after Phase 5. Release preparation and
further functional changes remain separate tasks.

## Visual direction

| Role | Color |
|---|---|
| Background | #092626 |
| Sidebar/cards | #025373 |
| Accent | #048ABF |
| Supporting text/highlights | #79C4F2 |
| Primary text | #F2F0F2 |

Borders and selected surfaces use opacity variants of these colors. Shared input,
button, dropdown, checkbox and expander styles retain visible focus indicators.
Page names now read Home, Apps, Fonts, Saved setups, Add files, Past installs and
Settings. Internal page IDs and saved JSON formats are unchanged.

Home leaves upper space, centers “Your software, ready to go.” lower in the page,
and anchors three actual library summaries near the bottom. Summary cards navigate
to Apps, Fonts and Saved setups. Detailed library status, library path, last refresh
and the recent-install count are retained in Library status. Refresh remains in the
footer and on other pages. Setup launching is on Saved setups; it has not been
removed. No fabricated counts, promotional action tiles or sample packages are used.

The shared footer places the small Sanupa Gunarathne credit on the right above
GitHub, LinkedIn and Instagram logos. Explicit clicks open only these supplied URLs:

- https://github.com/sanupagunarathne
- https://www.linkedin.com/in/sanupagunarathne/
- https://www.instagram.com/sanuoa.p/

Icons are bundled vectors, not remote images. Normal operation remains offline;
only opening a social website uses the user's browser/internet. Browser failures
are reported without blocking offline work. Font Awesome attribution is included
in THIRD_PARTY_NOTICES.md and copied beside build/publish outputs.

## Guided Add files

Three visible stages: Choose file, App details, Review & save. Going Back retains
entered metadata. A changed source returns to the first step for review. Every
field has a short visible hint; the review includes advanced values and full source
information. Source and metadata checks in the UI help staff navigate, but all
existing authoritative importer validation still runs when saving.

Common groups are offered without writing any library files at startup: Browsers,
Office, Media, Security, Design & editing, Tools and Development. Existing saved
and manifest-derived groups are combined without case-insensitive duplicates;
legacy groups such as Utilities are preserved. New imports default to Tools;
existing package manifests are not reassigned. The last dropdown item always opens
“Create new group…”. This item is a UI action, never saved as category metadata.
Create & select saves the named group through the existing category service;
cancelling keeps the prior actual group. An unfinished creation step blocks review.

More options retains installer architecture, mode, administrator requirement and
optional notes. No architecture, silent arguments or installation behavior is guessed.
Manage saved files and groups retains SHA-256 verification and all prior create,
remove-unused and assign-group operations. Import progress, cancellation and reports
remain accessible. Saving files never runs an installer.

Apps keeps search, group/computer-type filtering, checkboxes, selections and the
shared install list. Technical metadata and unavailable-file reasons are available
under App details and file checks. Labels such as Ready to select refer to inventory,
not preflight verification. The engine rechecks hashes before any real installation.
Fonts retains imports, license acknowledgement, previews and current-user installs.
Saved setups retains editing and protected launch. Past installs retains search,
JSON/CSV export and incomplete-session reporting. The queue keeps continuation
policy, removal, clear, confirmation, stop-after-current and all result handling.
The existing OneWay progress-bar binding is preserved. No installation engine,
font registration, manifest parser, preset persistence, history reader or report
export implementation is changed by this redesign.

## Validation

2026-11-11, .NET SDK 10.0.100, Linux x64:

| Branch | Restore / Release build | Tests |
|---|---|---:|
| feat/blue-workspace-ui | Passed, zero warnings/errors | 138 passed |
| feat/guided-add-files | Passed, zero warnings/errors | 145 passed |

New tests exercise default/custom groups, duplicate handling, creation/cancellation,
retaining advanced metadata through review/back, required-field guidance, in-folder
entry-point checks and preventing the create-action label becoming saved metadata.
Existing business-logic tests remain passing. No real installers or font registration
are executed. One generated XAML label initially contained an unescaped ampersand;
it was corrected before successful build validation.

WPF C#/XAML cross-compilation does not verify runtime layout or resource/binding
behavior. On Windows, check Home anchoring at multiple window sizes/scales, keyboard
navigation, expander toggles, text selection/copy, group dropdown create/cancel,
folder imports, footer link destinations, read-only reports and the shared install
list. Native window chrome follows the Windows theme. Actual rendered appearance
has not been tested in this Linux environment.

Both feature branches originate from clean dev, are preserved, validated, committed
separately and merged with merge commits. They are pushed with dev to the linked
GitHub repository; main remains unchanged. Next: review the Windows appearance,
then make the user's separately requested functional changes before any release.
