# Asynkron.Profiler

A lightweight CLI for CPU, memory allocation, and heap profiling of any .NET command using `dotnet-trace` and `dotnet-gcdump`.

## Prerequisites

- .NET SDK 10.x (preview)
- `dotnet-trace` and `dotnet-gcdump` installed:

```bash
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-gcdump
```

## Build

```bash
dotnet build -c Release
```

## Pack as a dotnet tool

```bash
dotnet pack -c Release -o ./nupkg src/ProfileTool/ProfileTool.csproj
```

Install from the local package:

```bash
dotnet tool install -g --add-source ./nupkg Asynkron.Profiler
```

## Usage

CPU profile:

```bash
asynkron-profiler --cpu -- dotnet run MyProject.sln
```

Memory allocation profile (GC allocation ticks + call tree):

```bash
asynkron-profiler --memory -- dotnet test
```

Heap snapshot:

```bash
asynkron-profiler --heap -- dotnet run path/to/app.csproj
```

Outputs are written to `profile-output/` in the current working directory.

## Options

- `--cpu` CPU profile only
- `--memory` memory allocation profile only
- `--heap` heap snapshot only
- `--root <text>` root the call tree at the first matching method
- `--calltree-depth <n>` max call tree depth (default: 30)
- `--calltree-width <n>` max children per node (default: 4)
- `--calltree-self` include self-time tree
- `--calltree-sibling-cutoff <n>` hide siblings below X% of the top sibling (default: 5)
- `--filter <text>` filter function tables by substring
- `--include-runtime` include runtime/process frames

## Examples

```bash
asynkron-profiler --cpu --calltree-depth 20 -- dotnet run ./samples/MyApp/MyApp.csproj
asynkron-profiler --memory --root "MyNamespace" -- dotnet test ./tests/MyApp.Tests/MyApp.Tests.csproj
```
