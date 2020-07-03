# Escaping the sandbox

BuildXL runs all build tools in a sandbox to observe their actions and, in some cases, prevent processes from taking certain actions. However, running tools in a sandbox has some limitations: the process (and child processes) lifespan is confined to the lifespan of the corresponding pip. This means  that any child process that tries to survive the main process will be terminated. This behavior doesn't allow processes like telemetry, compiler, code generation services, etc. to be correctly modeled with BuildXL.

In order to accomodate this type of scenarios, BuildXL enables  **trusted** tools a way to configure child processes to escape the sandbox. 

## Configuring a breakaway process

An *unsafe* configuration option

`childProcessesToBreakawayFromSandbox?: PathAtom[];`

 is available as part of the arguments of `Transformer.execute`. This option is configured on a per-pip basis with the names of the child processes that will breakaway from the sandbox when spawned by the main process. E.g.:

 ```javascript
 unsafe {
     childProcessesToBreakawayFromSandbox: [a`vctip.exe`]
 }
 ```

Whenever a child process is spawned whose name matches one of the names specified in `childProcessesToBreakawayFromSandbox`, that process immediately escapes the sandbox. Its behavior remains completely unknown to BuildXL.

 Observe this configuration option only affects *child* processes and not the main process associated with the pip, which will always run in the sandbox. 
 
## What can go wrong when enabling processes to breakaway from the sandbox?

Observe that configuring processes to breakaway from the sandbox is an inherently *unsafe* configuration. Reasons are:
* Any file access performed by a child process that escapes the sandbox won't be observed by BuildXL. This means these accesses won't be part of the associated pip fingerprint nor part of the pip outputs. The consequences of allowing an arbitrary process to breakaway are underbuilds (BuildXL computes an incomplete set of inputs for a pip, so it may miss changes on particular input files) or missing outputs (when an output is not explicitly declared and is part of an output directory)
* A breakaway process may survive its corresponding pip and interact with other pips (or even across builds) introducing build non-determinism.

## General considerations

Here are some rules of thumb to consider when configuring processes to breakaway from the sandbox:

* A typical scenario where configuring a breakaway process makes sense is when a process is designed to act as a service and its responsibilities go beyond the associated pip that originally spawned it. E.g. a telemetry service, shared across projects, that sends message over the wire.
* In order to trust a breakaway process and consider it safe, the process should not produce any files (or produce files that are known to be inconsequential to the build). Additionally, the breakaway process should not hold any state that may affect subsequent pips (or subsequent builds) in an observable way.

Additionally, whenever nested job objects are supported by the underlying hardware, BuildXL will make sure any processes created during a build are terminated when the main BuildXL process is terminated. So this may terminate any breakaway process as well. *Note*: if BuildXL is launched using server mode (the default) this consideration only applies when BuildXL server process is terminated, which can effectively result in breakaway processes to survive across builds.

This configuration option is not to be confused with `allowedSurvivingChildProcessNames` (another unsafe option under `Transformer.execute`).  The option `allowedSurvivingChildProcessNames` allows to specify which processes are safe to terminate when trying to survive the sandbox. This means the associated pip will not fail when this situation happens (otherwise, any surviving process that is not part of `allowedSurvivingChildProcessNames` that runs in the sandbox will also be terminated, but the corresponding pip will be flagged as a failing one).