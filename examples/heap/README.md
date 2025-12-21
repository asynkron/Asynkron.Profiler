# Heap snapshot demo

Build:

```bash
dotnet build -c Release
```

Profile:

```bash
asynkron-profiler --heap -- ./bin/Release/net8.0/HeapDemo
```

Or (framework-dependent):

```bash
asynkron-profiler --heap -- dotnet ./bin/Release/net8.0/HeapDemo.dll
```
