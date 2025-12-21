# Memory allocation demo

Build:

```bash
dotnet build -c Release
```

Profile:

```bash
asynkron-profiler --memory -- ./bin/Release/net10.0/MemoryDemo
```

Or (framework-dependent):

```bash
asynkron-profiler --memory -- dotnet ./bin/Release/net10.0/MemoryDemo.dll
```
