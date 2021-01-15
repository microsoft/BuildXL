# Trusted tools: Case of shared compilation

## Shared compilation 

BuildXL supports shared compilation. Shared compilation allows the compiler (e.g., `csc.exe`) to create a background process (e.g., `VBCSCompiler.exe`) that breaks away from its process and continues to run in the background even after `csc.exe` exits. The idea is that the subsequent invocations of csc can discover the running VBCSCompiler process and delegate some work to it. By being a long-running process, VBCSCompiler can implement various in-memory caches that can be reused across multiple csc invocations, and thus, potentially, improve overall build time across all those csc invocations. 

## Process breakaway

A pip in BuildXL, by default, consists of the entire process tree of the main pip process. In other words, for a pip to complete successfully, all the processes in its process tree must complete successfully (and, typically, the root process must complete last). To support the shared compilation model, an unsafe process execution argument was introduced, namely 'childProcessesToBreakawayFromSandbox'. For a child process that is allowed to break away the following rules apply:
  - such a process is executed outside the sandbox and is thus untracked
  - consequently, BuildXL is completely unaware of its behavior, the files it reads, the files it writes etc.; for that reason, this is an unsafe option and should only be used for processes that are known to be well behaved and whose behavior is well understood with respect to BuildXL's caching semantics.

More details on process breakaway can be found [here](./Process-breakaway.md).

## Trust statically declared inputs/outputs

To enable shared compilation, BuildXL uses VBCSCompiler tool, configured as a breakaway process, and relies on dependencies and outputs that are declared statically by the pip. Since VBCSCompiler tool is a breakaway process, BuildXL cannot observe file accesses by the tool when the tool executes. After the compilation is done, BuildXL augments whatever reported file accesses by the pip with dependencies and outputs that the pip statically declares. (See `AugmentWithTrustedAccessesFromDeclaredArtifacts` method in [SandboxedProcessPipExecutor](/Public/Src/Engine/Processes/SandboxedProcessPipExecutor.cs) class).

In DScripts this kind of trust is done by specifying the unsafe field of `Transformer.ExecuteArguments` as follows:
```ts
unsafe: {
    childProcessesToBreakawayFromSandbox: [ a`VBCSCompiler.exe` ],                          
    trustStaticallyDeclaredAccesses: true,
}
```
DScript's Managed SDK provides an option to enable shared compilation via the 'shared' property of the CSC 'Arguments' interface.

To enable shared compilation for the selfhost build, pass `/p:[Sdk.BuildXL]useManagedSharedCompilation=1` to your BuildXL build.

## Report file accesses through BuildXL file reporting handle (Windows-only)

To compensate for the unobserved accesses from the breakaway child processes, a separate mechanism was introduced to allow such breakaway processes to explicitly report accesses by talking directly to BuildXL once they've broken out of the sandbox.  Concretely, before the process breaks away from the sandbox, the sandbox sets the `BUILDXL_AUGMENTED_MANIFEST_HANDLE` environment variable that the child process can read to obtain a handle, via which the process can report file accesses directly to BuildXL (just like the Detours-based sandbox would).  Clearly, this works only for processes purposely developed with this in mind; third party processes, like VBCSCompiler, do not benefit from this feature.

This feature is currently used by VBCSCompilerLogger (that is shipped as part of toolset shipped by BuildXL). This logger catches csc and vbc MSBuild tasks and uses the command line argument passed to the compiler to mimic the file accesses that the compiler would have produced. This logger is used by the MSBuild frontend of BuildXL when scheduling pips: when process breakaway is supported, `VBSCompiler.exe` is allowed to escape the sandbox and outlive the originating pip. This logger is used as a way to compensate for the missing accesses and the MSBuild frontend attaches it to every MSBuild invocation.