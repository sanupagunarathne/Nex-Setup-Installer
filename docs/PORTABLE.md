# Portable deployment notes

The Phase 1 project is configured for a self-contained, single-file Windows x64
application. A separate .NET installation will not be required by the eventual
published application. This is a foundation, not an installation-ready release.

When release work is explicitly authorized, the intended publish command is:

```powershell
dotnet publish NexInstaller.App/NexInstaller.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/portable
Copy-Item package-library artifacts/portable/package-library -Recurse
```

Keep `nexinstaller.json` and `package-library` external to the EXE. The project
copies configuration to build and publish outputs. The library must be copied
separately so packages can change without rebuilding the app. Standard build
output is not the final portable release. Publication and release validation
are Phase 6 work and have not been performed in Phase 1.

```text
portable/
  NexInstaller.exe
  nexinstaller.json
  package-library/
    packages/
    fonts/
    presets/
    manifests/
    history/
```

Single-file WPF publishing may extract native runtime files into the user's
temporary directory. A read-only USB can still be used when local application
data and temporary storage are writable. If both log destinations are blocked,
the app continues with a diagnostics notice. Future imports and installation
services must report write restrictions before modifying the library.

Populate installer binaries and licensed fonts outside Git. Phase 1 scans font
filenames and metadata filenames only. Do not infer package availability from
the manifest file count. Do not use arbitrary manifests or silent arguments to
execute software: validated manifests and verified installation profiles will
be introduced in later phases.

Windows acceptance checks before a release:

- Run on clean Windows 10 and Windows 11 x64 systems without .NET installed.
- Disconnect networking before launch and repeat the checks.
- Navigate all seven pages with mouse and keyboard; check focus and accent states.
- Check 100%, 150%, and 200% display scaling, resizing, and scroll behavior.
- Copy to a USB drive and change its drive letter; verify relative path resolution.
- Launch from a working directory different from the EXE directory.
- Test missing, malformed, and custom-root configuration.
- Test missing folders, read-only media, disconnected media, and log fallback.
- Confirm that imports and installs remain disabled in the foundation build.
- Check antivirus and SmartScreen normally; never disable or bypass protection.

No Windows runtime, USB device, UAC, or visual acceptance checks are claimed by
the Linux cross-build and unit tests.
