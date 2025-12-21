# Asynkron.Profiler

Install globally:

```bash
dotnet tool install -g asynkron-profiler --prerelease
```

A lightweight CLI for CPU, memory allocation, and heap profiling of any .NET command using `dotnet-trace` and `dotnet-gcdump`.

This is a profiler frontend/CLI UI built for humans and automation (including AI agents). It outputs plain‑text, structured summaries so the same insights you’d inspect in GUI tools like dotMemory or dotTrace are available to scripts and agent workflows.

## Prerequisites

- .NET SDK 10.x
- `dotnet-trace` and `dotnet-gcdump` installed:

```bash
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-gcdump
```

## Profile your own .NET app

Build your app in Release, then point the profiler at the compiled output (fastest and avoids profiling the build itself):

```bash
dotnet build -c Release
asynkron-profiler --cpu -- ./bin/Release/<tfm>/MyApp
```

Framework-dependent apps can be profiled via `dotnet`:

```bash
asynkron-profiler --cpu -- dotnet ./bin/Release/<tfm>/MyApp.dll
```

You can also pass a project or solution file directly. The profiler will build Release and run the resulting executable:

```bash
asynkron-profiler --cpu -- ./MyApp.csproj
asynkron-profiler --cpu -- ./MySolution.sln
```

You can also profile `dotnet run`, but it will mostly capture the dotnet host (and build/restore). Use this only if you can't run the built output directly:

```bash
asynkron-profiler --cpu -- dotnet run -c Release ./MyApp.csproj
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
dotnet tool install -g --add-source ./nupkg asynkron-profiler
```

## Usage

### Screenshots

CPU call tree:

![CPU call tree](assets/images/cpu-calltree.png)

Memory allocation call tree:

![Memory allocation call tree](assets/images/memory-calltree.png)

### Profile a command

CPU profile a command:

```bash
asynkron-profiler --cpu -- ./bin/Release/<tfm>/MyApp
```

Memory allocation profile (GC allocation ticks + call tree):

```bash
asynkron-profiler --memory -- ./bin/Release/<tfm>/MyApp
```

Heap snapshot:

```bash
asynkron-profiler --heap -- ./bin/Release/<tfm>/MyApp
```

### Render existing traces

You can render existing files without re-running the app. Supported inputs:

- CPU: `.speedscope.json` or `.nettrace` (will convert to Speedscope)
- Memory: `.nettrace` or `.etlx`
- Heap: `.gcdump` (or a `dotnet-gcdump report` text file)

Examples:

```bash
# Auto-selects CPU/memory/heap based on file extension
asynkron-profiler --input /path/to/trace.nettrace

# Force CPU rendering for a speedscope file
asynkron-profiler --input /path/to/trace.speedscope.json --cpu

# Render memory allocations from an .etlx
asynkron-profiler --input /path/to/trace.etlx --memory

# Render heap dump
asynkron-profiler --input /path/to/heap.gcdump --heap

# Manual flow: collect a CPU trace, then render it
dotnet-trace collect --output ./profile-output/app.nettrace -- dotnet run MyProject.sln
asynkron-profiler --input ./profile-output/app.nettrace --cpu
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
- `--input <path>` render existing `nettrace`, `speedscope.json`, `etlx`, or `gcdump` files
- `--tfm <tfm>` target framework when profiling a `.csproj` or `.sln`

## Troubleshooting

- `dotnet-trace` not found: install with `dotnet tool install -g dotnet-trace` and ensure your global tool path is on `PATH`.
- `dotnet-gcdump` not found: install with `dotnet tool install -g dotnet-gcdump`.
- Empty allocation tables: ensure you ran with `--memory` (GC allocation ticks) or provided a `.nettrace`/`.etlx` that includes GC allocation events.
- Slow or huge traces: reduce the workload/iterations or add filters on your app side, then re-run.
- The CLI checks for required tools on first use and prints install hints when missing.

## Examples

```bash
CPU profiling:
asynkron-profiler --cpu -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --cpu --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --cpu --input ./profile-output/app.speedscope.json

Memory profiling:
asynkron-profiler --memory -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --memory --root "MyNamespace" -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --memory --input ./profile-output/app.nettrace

Heap snapshot:
asynkron-profiler --heap -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --heap --input ./profile-output/app.gcdump

Render existing traces:
asynkron-profiler --input ./profile-output/app.nettrace
asynkron-profiler --input ./profile-output/app.speedscope.json --cpu
asynkron-profiler --input ./profile-output/app.etlx --memory

General:
asynkron-profiler --help

asynkron-profiler --cpu --calltree-depth 20 -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --memory --root "MyNamespace" -- ./bin/Release/<tfm>/MyApp
asynkron-profiler --input ./profile-output/app.nettrace --cpu
```
