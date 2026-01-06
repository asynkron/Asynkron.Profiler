# JitDump inlining capture: how it fits in Asynkron.Profiler

## Purpose

Capture JIT inlining structure for a specific method while the profiler launches a child process, then parse the results offline into a structured report. This aligns with the current "capture -> analyze -> print" flow already used for CPU, memory, exception, contention, and heap profiles.

## Where it fits in the current flow

Asynkron.Profiler already:

- Launches a child process for the target command.
- Uses a stable output directory (`profile-output/`).
- Runs a post-processing step to parse and render results.

JitDump fits as an additional capture mode that does **not** use ETW or `dotnet-trace`. It is a diagnostics capture step that emits JIT diagnostic text (to stdout in Debug/Checked JIT builds), then a parser step that extracts the inlining tree and optional IR for reporting.

## Capture step (parent process behavior)

The profiler should set JIT dump environment variables **only** for the child process, before it starts. This can be done on `ProcessStartInfo.Environment`, matching how other commands are launched.

Note: `JitDump` output is only available in Debug/Checked JIT builds of the runtime. Release JITs do not emit JitDump output, even if the variables are set. One way to use a Debug/Checked JIT is to point the runtime at an alternate `libclrjit` via `DOTNET_AltJitPath`/`DOTNET_AltJitName`.

Minimum useful set:

```
DOTNET_JitDump=TypedAstEvaluator.ExecutionPlanRunner:ExecuteInstructionLoop
DOTNET_JitDumpInlinePhases=1
DOTNET_JitDumpASCII=0
DOTNET_TieredCompilation=0
DOTNET_ReadyToRun=0
DOTNET_ZapDisable=1
```

Optional but often useful:

```
DOTNET_JitDumpVerboseTrees=1
```

Working directory should be set to a known location so files land in a predictable place (for example, reuse `profile-output/`):

```
psi.WorkingDirectory = outputDir;
```

JitDump writes to stdout, so the profiler should redirect the child process output to a dedicated log file to avoid interleaving with normal app output.

## Child process requirements

Nothing special is required beyond ensuring the target method is JIT compiled. If needed, a target can force JIT via `RuntimeHelpers.PrepareMethod`, but this is a child app concern, not a profiler concern.

## Output format

JitDump produces deterministic text output (stdout). The profiler should write it to a file such as:

```
jitdump_YYYYMMDD_HHMMSS.log
```

This is stable and parseable even though it is not JSON/XML.

## Parsing approach (offline analysis step)

Recommended high-level parser flow:

1. Split file content on the marker `JIT compiling`.
2. For each compilation block:
   - Detect `Inlinee:` lines and `INLINING SUCCESSFUL`/`INLINING FAILED`.
   - Track indentation to build the inline tree.
   - Optionally extract IR sections between `***************` phase headers.

Key markers:

- Method boundary:
  `*************** JIT compiling Namespace.Type:Method`
- Inline decision:
  `Inlinee: ...` and `INLINING SUCCESSFUL` / `INLINING FAILED: ...`
- Inline tree indentation conveys nesting.
- IR sections:
  `*************** After inlining` or `*************** IR Dump PHASE_INLINING`

Do not rely on line numbers. Rely on these markers.

## How this would surface in Asynkron.Profiler

A dedicated mode or flag can produce:

- A list of JIT-compiled methods captured.
- An inline tree per method.
- Optional IR and native code blocks for deeper inspection.

This mirrors the existing CLI UX: capture artifacts to `profile-output/`, parse them into a summary, and print to the console. The key difference is that the capture mechanism is environment-variable-driven rather than ETW.

## What this does not provide

- No rewritten IL or stable API.
- No callbacks or live hooks.
- It is compiler diagnostic text, not a formal telemetry format.

## Summary

JitDump capture is a clean fit for Asynkron.Profiler as an additional capture mode:

- It uses the same child-process launch pattern.
- Output is deterministic and can be redirected to a dedicated file.
- The parser can produce a structured inline tree summary for offline analysis.
