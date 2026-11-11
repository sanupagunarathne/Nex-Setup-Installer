# Offline package manifests

Put one JSON manifest per software package in `package-library/manifests`.
See `schemas/package-manifest.schema.json` and the documentation-only manifest
in `examples/package-manifest.example.json`. Examples are not loaded automatically.
Supply your own licensed installer files under `packages/<package-folder>/` and
replace the example SHA-256 with the actual value. No installer binaries are
committed. Multiple files can describe a complete installation folder; entryPoint
must identify a listed EXE/MSI file. Every listed file is required.

Schema version 1 uses camelCase properties and named architecture/installer/mode
values. IDs are stable lowercase identifiers, independent of display names and
versions. Category is a non-empty trimmed label; new categories are derived from
manifests in Phase 2. Editing category metadata is Phase 3 work.

The parser enforces additional semantic rules beyond JSON Schema: no duplicate
properties or file paths (case insensitive), no whitespace-only labels, entrypoint
extension and file-list consistency, portable Windows path rules, and a 1 MiB
manifest limit. Paths are library-relative and must be beneath
`packages/<package-folder>/`. Absolute paths, traversal, alternate data streams,
reserved Windows names, and linked/reparse-point payload paths are rejected.

EXE imports will default to Manual in Phase 3; omitted installationMode already
defaults to Manual in the manifest model. A VerifiedUnattended declaration requires
a profile ID but does not establish that profile's trust or verification. No
silent argument fields are accepted, guessed, or executed in Phase 2.

SHA-256 metadata must be well formed. Phase 2 checks file presence and portable
paths, not content integrity, signatures, permissions for execution, or license
validity. Hash verification is Phase 3/4 work and will be mandatory before any
future execution. Manifests must be treated as untrusted local input.
