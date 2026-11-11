# Apps and Add files reference alignment — 0.5.2

2026-11-12. This follows the approved blue visual references while preserving the
existing installation capabilities and the Home/footer design.

Apps now uses a viewport-height workbench: search and group selection above the
app-name/version/group table, with the actual live installation list on its right.
The library-empty illustration has a working Add your first app action. A filtered
empty result is labelled differently and does not claim the entire library is empty.
Selection, select-all-available, clear choices and add-to-list remain explicit.
Hidden selections remain available while filtering. Computer-type filtering,
metadata, unavailable-file reasons and diagnostics remain under the lower details
expander. The right list shares the same queue used by Fonts and Saved setups;
there is no duplicate queue below Apps. It retains removal, clearing, failure
continuation, preflight/confirmation, stopping, progress, restart summaries and
results. Results remain accessible after pending items are cleared. The side panel
can scroll when the window or details area leaves less room for controls.

Add files uses a circular connected stepper, a larger guided form with bottom
navigation, and a compact Your file / Need help column. Placeholder text supports
existing labels/hints rather than replacing them. Operations have a slim status
area, with detailed reports, verification and group management still available in
an expander. All three source/details/review stages, folder imports, custom groups,
advanced settings, import cancellation and checks remain present. No installer is
executed by adding files. Home, the approved palette and personal footer are kept.

## When to add real software and fonts

Importing was implemented in Phase 3 and can be used now, before release:

1. Run NEX on Windows and check Settings for the active library location.
2. Use Add files to choose an offline EXE/MSI, or a complete installation folder and
   its setup entry point. Fill the app details, review, then Save to library.
3. Use Fonts, acknowledge the applicable licenses, then Add font files or Add a font
   folder for TTF/OTF imports.
4. Imported files appear in the library after refresh. Installation is a separate
   action through the install list and still asks for confirmation.

For source builds, the default library is beside the executable under the build
output directory, not automatically the repository-root directory. Importing into
a test build does not bundle that data into Git or a future published release.
For the final shop USB, publish the portable app in Phase 6, launch that EXE and
populate its external package-library, or copy the already-populated library into
that release directory. Keep configuration and the whole package-library together:
packages, fonts, manifests and presets. Check the active root after copying.

Technicians can continue importing after release without rebuilding the EXE.
Supply licensed, complete offline installers and permitted font files separately;
no real installers or fonts are supplied by the source repository. Keep these files
out of GitHub. Read-only media cannot receive imports; the app reports write errors.

## Validation and Git

All changes are cross-compiled with .NET SDK 10.0.100 on Linux x64. Restore and
Release solution build passed with zero warnings/errors. Tests use fake execution
and temporary fixture files, never real installers or OS font registration.

| Branch | Tests passed |
|---|---:|
| fix/late-installation-progress | 146 |
| feat/reference-page-layouts | 148 |

An existing test intermittently failed when a late progress callback replaced the
final restart summary. A deterministic regression reproduced this before the fix.
Per-run synchronized progress acceptance is now closed before any final result or
error summary is published, so late callbacks cannot overwrite completion state.
The fix was developed/validated separately from dev; layout work was temporarily
stashed, restored, and tested with that fix. New layout checks cover navigation from
the empty Apps page and retaining completed results after clearing pending items.
The existing OneWay progress-bar bindings remain intact.

WPF C#/XAML compiles here, but native Windows rendering, scrolling at every display
scale, dialogs and browser launching are not runtime-tested on Linux. The layout
structure follows the references; pixel-level Windows acceptance remains necessary.
Review on Windows at default and minimum window sizes, including long app names,
empty/filtered libraries, long install lists, results, expanded details and the
three Add files stages. No screenshot is presented as an actual Windows runtime
capture. Both branches are preserved, merged to dev with merge commits and pushed;
main remains unchanged. Further functional requests and release work stay separate.
