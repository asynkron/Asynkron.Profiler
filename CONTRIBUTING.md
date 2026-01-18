# Contributing

Thanks for helping improve Asynkron.Profiler!

## Prerequisites

- .NET SDK 10 preview (see `global.json` for the pinned version).

## Build

From the repo root:

```sh
dotnet restore
dotnet build -c Release --nologo
```

## Test

```sh
dotnet test -c Release --nologo
```

## Release

Releases are tag-based and run via GitHub Actions (`.github/workflows/pack.yml`).

1. Create a version tag (example):

```sh
git tag v1.2.3
git push origin v1.2.3
```

2. The workflow packs and publishes:
   - `src/ProfileTool/ProfileTool.csproj`
   - `src/ProfilerCore/Asynkron.Profiler.Core.csproj`

If you need to run the pack step locally:

```sh
VERSION=1.2.3
dotnet pack -c Release -o ./nupkg src/ProfileTool/ProfileTool.csproj -p:Version="$VERSION"
dotnet pack -c Release -o ./nupkg src/ProfilerCore/Asynkron.Profiler.Core.csproj -p:Version="$VERSION"
```
