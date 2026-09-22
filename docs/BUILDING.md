# Building and releasing Pickets

[Back to the overview](../README.md)

Build on Windows with the .NET 10 SDK. Run commands from the repository root.

## Build from source

Clone the repository, then run:

```powershell
dotnet build Pickets.csproj --configuration Release
dotnet run --project Pickets.csproj --configuration Release
```

To create the same single-executable package used for releases:

```powershell
dotnet publish Pickets.csproj --configuration Release --property:PublishProfile=win-x64
```

The result is written to:

```text
bin\Release\net10.0-windows\win-x64\publish\Pickets.exe
```

To build the installer locally, publish into its staging folder, install
[Inno Setup 6](https://jrsoftware.org/isinfo.php), and run the included build script:

```powershell
dotnet publish Pickets.csproj --configuration Release --property:PublishProfile=win-x64 --output release\portable
.\installer\build-installer.ps1 -Version 1.0.0
```

The installer is written to `release\PicketsSetup.exe`.

Release builds enable the recommended .NET analyzers and treat warnings as errors. Pushes and pull
requests are also compiled and publish-checked on Windows through GitHub Actions.

Pushing a version tag that matches `Pickets.csproj` (for example, `v1.0.0`) runs the release
workflow. It tests and publishes the app, builds the per-user installer, creates
`SHA256SUMS.txt`, records GitHub build provenance for both executables, and attaches the installer,
portable executable, and checksums to a draft GitHub Release. Review the notes, complete the live
test sign-off, and verify the assets and provenance before publishing the draft.

## Tests and release sign-off

```powershell
dotnet test Pickets.Tests/Pickets.Tests.csproj --configuration Release
```

Complete the [hands-on tests](TEST_RELEASE.md) and [release checklist](RELEASE_CHECKLIST.md)
against the final portable executable and installer. Passing automated tests alone does not
verify Explorer recovery, monitor changes, or installation on a clean machine.
