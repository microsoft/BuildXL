# Runtime hints (`##bxl`)

## Concept
Some process pips are thin wrappers that offload the real work to an external system (for example, a pip that only submits a job to CloudTest and returns almost immediately). Their measured wall-clock time is close to zero, so the scheduler has no signal that the underlying work is expensive and may arbitrarily delay them.

Runtime hints let a process communicate the "true" cost of its work back to BuildXL by writing a specially formatted line to its standard output (or standard error):

```
##bxl[runtimeSecs]=42
```

When hint scanning is enabled on a pip, BuildXL scans each output line starting with `##bxl` and, for the `runtimeSecs` hint (a whole number of seconds), injects that value as the pip's runtime. The injected value:

* Overrides the measured runtime as soon as the pip completes, so all downstream consumers observe the injected value.
* Is persisted to BuildXL's historic performance data, so subsequent builds prioritize the pip according to its real cost.
* Is tracked as injected for diagnostics. Logs that print the runtime (e.g. critical path) surface it as `[injected] 42000`.

The `##bxl` prefix is a generic channel; today only the `runtimeSecs` hint is recognized. Any other `##bxl[...]` line or a duplicate `runtimeSecs` hint produces a warning and is ignored.

## Language support & API
Set `scanForBuildXLHints: true` in the argument of `Transformer.execute`:

```ts
Transformer.execute({
    tool: <your tool name>,
    workingDirectory: d`.`,
    arguments: [ /* other args */ ],
    scanForBuildXLHints: true,
    dependencies: [ /* dependencies list */ ],
});
```

Hint scanning is off by default; enable it only on pips that emit `##bxl` lines.
