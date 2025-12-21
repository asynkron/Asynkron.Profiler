# Exception demo

Build:

```bash
dotnet build -c Release
```

Profile:

```bash
asynkron-profiler --exception -- ./bin/Release/net10.0/ExceptionDemo
```

Filter to a specific exception type:

```bash
asynkron-profiler --exception --exception-type "InvalidOperation" -- ./bin/Release/net10.0/ExceptionDemo
```
