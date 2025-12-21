# CPU demo

Build:

```bash
dotnet build -c Release
```

Profile:

```bash
asynkron-profiler --cpu -- ./bin/Release/net10.0/CpuDemo
```

Or (framework-dependent):

```bash
asynkron-profiler --cpu -- dotnet ./bin/Release/net10.0/CpuDemo.dll
```
