# Phase 1 foundation

## Implemented

- Four-project .NET 10 solution; WPF targets Windows x64.
- Exact theme colors: `#12110F`, `#1B191A`, `#373739`, `#8E8A80`, `#1BFA01`;
  near-white primary text.
- Shared rounded cards and button templates, hover and keyboard focus states,
  responsive grids/wrapping, and scrollable desktop content.
- All seven page layouts selected through view model data templates.
- `Microsoft.Extensions.DependencyInjection` composition root with lifetime disposal.
- Framework-neutral navigation state and commands; observable active menu state.
- Background, cancellable, read-only library scan; progress state and retry.
- Application-relative JSON configuration with readable fallback notices.
- Local structured logging with local-app-data fallback for read-only media.
- Directory markers for the external library; no installer or font payloads.
- Unit tests with temporary isolated directories and fake inventory/log services.

## Deliberately deferred

Phase 2 introduces package models, manifest schemas and parsing, validation,
discovery, filtering, and missing-package behavior. Phase 3 adds import, SHA-256,
duplicate detection, category management, and font import. Phase 4 adds the
installation engine and queue with technician confirmation. Phase 5 adds preset
serialization, session history, and reporting. Phase 6 covers publishing and
full Windows release acceptance.

The disabled controls show the intended workspace; they do not simulate a
completed importer or installer. Package lists and fonts are not rendered from
unvalidated manifests in Phase 1. Counts indicate file inventory only. There are
no real installation services yet, and no automated test invokes an installer.

## Validation boundaries

Cross-building WPF validates C# and XAML compilation; it does not validate
runtime resource loading, data binding, keyboard interaction, display scaling,
or visual layout on Windows. The test project compiles the same framework-neutral
view model source used by the WPF app, exercising its behavior with fake services.
Windows smoke testing remains mandatory before claiming desktop runtime support.

Log fallback tests block directory creation using an ordinary file at the log
path. This reliably exercises write failure on both Linux and Windows, but it
is not a physical read-only USB test. Installer path containment and execution
security will require distinct tests when those capabilities are introduced;
`LibraryPaths` only resolves an administrator-configured root, not package paths.

## Git workflow

The initial configuration commit is on `main`. `dev` starts from it, and Phase 1
is developed on `feat/project-foundation`. All branches remain local and are
preserved. A successful restore, Release build, and applicable test run are the
merge gate; desktop runtime acceptance remains separately documented. Use a
merge commit to integrate completed Phase 1 work into `dev`. Leave `main`
untouched until a stable milestone is explicitly requested.

Next task: create `feat/package-discovery` from a clean `dev`, implement Phase 2
models and strict JSON manifest handling, then add path containment, invalid
manifest, missing-file, and filtering tests before merging.
