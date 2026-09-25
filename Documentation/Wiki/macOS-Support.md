# macOS Support

BuildXL support on macOS is experimental. BuildXL can build and run process pips with `SandboxKind.None`, but it does not provide file-access monitoring or enforcement. Builds in this mode must not be treated as having BuildXL's normal hermeticity or cache-correctness guarantees.

See the [Developer Guide](DeveloperGuide.md#macos) for setup and bootstrap instructions.

## Roadmap

- [ ] Implement a macOS file-access sandbox, likely using Apple's Endpoint Security framework. It must provide sufficiently complete and reliable process-tree and file-access reporting before sandbox-dependent caching can be supported.

## Unsupported features

| Feature | Current limitation |
|---|---|
| File-access sandboxing | BuildXL runs process pips with `SandboxKind.None`. It cannot monitor or enforce file accesses or reliably track the complete process tree. Requesting a sandbox reports `DX0953`. |
| Caching based on observed accesses | Without file-access monitoring, BuildXL cannot incorporate dynamically observed reads, absent-path probes, or directory enumerations into fingerprints. Builds do not have BuildXL's normal hermeticity or cache-correctness guarantees. |
| Incremental scheduling | Incremental scheduling depends on observed file accesses and is unavailable without sandbox monitoring. |
| File-access policies | Unexpected-access validation, access allowlists, observation reclassification, and file-access tracing depend on sandbox reports and are unavailable. |
| Shared opaque directories | Shared opaque directories require monitoring to discover their contents. If any pip declares one, graph validation fails with `DX0954`, identifying the directory, pip, and defining spec. |
| Preserve outputs | Safe reuse and validation of preserved process outputs depends on sandbox enforcement and is not supported. |
| JavaScript graph builders | The Lage, Nx, Rush, and Yarn graph builders use shared opaque directories for installation, compilation, and deployment, so those resolvers are unavailable. |
| MSBuild frontend | MSBuild graph construction relies on sandboxed process execution and observed file accesses for correct caching. It is currently disabled on macOS. |
| Ninja frontend | Ninja integration requires sandboxed process execution and a macOS-specific Ninja tool package that is not currently available. |

## Finding macOS exclusions

Search the repository for these patterns when reviewing or updating macOS support:

- `BuildXLSdk.AllSupportedQualifiersWithoutMacOS`, `BuildXLSdk.Net8PlusQualifierWithoutMacOS`, and `BuildXLSdk.Net10QualifierWithoutMacOS` exclude macOS during DScript evaluation. `BuildXLSdk.DefaultQualifierWithoutMacOSArm64`, `BuildXLSdk.DefaultQualifierWithNet472WithoutMacOSArm64`, and `BuildXLSdk.AllSupportedQualifiersWithoutMacOSArm64` exclude only Apple Silicon.
- `Context.getCurrentHost().os === "macOS"` and `OperatingSystemHelper.IsMacOS` select host-specific build or runtime behavior.
- `TestClassIfSupported`, `FactIfSupported`, and `TheoryIfSupported` skip tests whose declared requirements are unavailable on the current platform.
- `macos-missing` comments mark localized workarounds for functionality that is not currently implemented. Include a short reason after the marker so repository searches remain actionable.
