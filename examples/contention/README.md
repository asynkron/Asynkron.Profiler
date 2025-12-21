# Contention Demo

Small app that intentionally creates lock contention so you can validate the profiler's contention mode.

## Build

```bash
dotnet build -c Release
```

## Run directly

```bash
./bin/Release/net10.0/ContentionDemo
```

## Profile contention

```bash
asynkron-profiler --contention -- ./bin/Release/net10.0/ContentionDemo
```

If you only have the framework-dependent DLL:

```bash
asynkron-profiler --contention -- dotnet ./bin/Release/net10.0/ContentionDemo.dll
```
