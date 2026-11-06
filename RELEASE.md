# Release preparation

## Versioning

The application major version is always **1**. Each successful application build increments the minor version in `version.txt` and embeds `1.N` into the application. The first versioned build is `1.1`. Windows file and assembly versions use `1.N.0.0`.

`dotnet build`, `dotnet run` without `--no-build`, test builds referencing the application, and `dotnet publish` each count as an application build. Failed builds, IDE design-time builds, WPF temporary projects and `--no-build` operations do not advance the counter. Build this project sequentially; simultaneous independent builds of the same checkout are not supported. Preserve `version.txt` between releases and commit its final value with the release source.

## Create an archive

On Windows with .NET 10 SDK, close FilterCopy before building:

```powershell
dotnet run --project Tests\FilterCopy.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Prepare-Release.ps1
```

The packaging script publishes the application once, reads the version from the resulting binary, and creates a versioned ZIP, a SHA-256 checksum and release notes under `artifacts\releases`. It uses an explicit file list and does not package local settings, filter history, tests, symbols or build logs. Self-contained win-x64 is the default and may require downloading runtime packs. Use `-FrameworkDependent` to package a build requiring .NET 10 Desktop Runtime instead.

The release ZIP contains registration scripts that use the executable next to the scripts; end users do not need an SDK or a source checkout. The ZIP's `README.md` is copied from `docs/USER_GUIDE.md`, while the repository's root README is developer documentation. Maintain the user instructions in the guide only. Extract to a permanent location before registering. Do not register from a temporary extraction folder.

The application is licensed under MIT; the root `LICENSE` is included in every archive. Self-contained archives also include the bundled .NET runtime licenses and third-party notices under `licenses`, retaining their upstream terms. To repackage the same already-published binary without building or advancing the version, use `-PublishedDirectory path-to-publication`. The publication must match the requested runtime type and the restored runtime packs must still be available. ZIP filenames must not already exist.

## Before publishing to GitHub

- Retain the root MIT `LICENSE` and bundled runtime notices in the release archive.
- Review `git diff --cached` and `git status --short`. Build output and machine-specific configuration should be ignored.
- Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-PublicFiles.ps1` for the staged source file list, and pass `-Archive path-to-release.zip` to inspect the ZIP. This flags local profile paths, local identities and common secret formats. Email checks apply to project-owned files; vendor .NET DLLs may contain example addresses. This is a heuristic check, so review the source changes as well.
- Configure the intended Git author identity (for example your GitHub no-reply address) before the first commit. No author identity is written automatically.
- Commit the reviewed sources, including the final `version.txt`, then tag the matching binary version, for example `v1.N`.
- Upload the ZIP and its checksum to the matching GitHub release. Do not rebuild after tagging; another build advances the version.

No remote URL, credentials, Git author identity, commit, tag, push or GitHub release is created by the preparation scripts.
