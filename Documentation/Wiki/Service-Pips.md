# Handling Background Processes in BuildXL

## Introduction

BuildXL is a build system that manages build tasks at the process (or _pip_ for short) level.  BuildXL "expects" every process to be _short-lived_, _atomic_, and _well-defined_ (SLAWD), meaning:

  - _short-lived_: the process performs a single (conceptual) operation and terminates;
  - _atomic_: once started, neither can the process receive any new commands, nor can its outputs be accessed by any of its dependants before it terminates;
  - _well-defined_: process inputs and outputs are all statically known and fully specified.

Furthermore, BuildXL enforces that the runtime behavior of each process adheres to its input/output specification.  This makes it difficult (or even impossible) to spawn and use *background* processes in a concrete build, i.e., any process that does not fit the definition above.  Both Unix daemons and Windows Services fall into this category, but any resident process may apply too.

## Motivation

Running background processes as part of a build may prove to be useful in many cases.  To support this claim, this document gives two concrete examples that are relevant for Microsoft's internal usage of the feature.

**Note**: The features described may not be publicly facing. They are here to provide an illustration for the motivation.

#### Uploading Symbols/Drops

At the end of an official/lab build, many teams at Microsoft choose to upload the produced build artifacts as a single "drop" to some online service like Azure DevOps Drops; similarly, the produced .pdb files (symbols) are uploaded to an online symbol service.

Uploading a drop involves 3 steps (for symbols it's very similar):

  1. creating a session: `drop create --name MyDrop`;
  2. uploading files (may be called multiple times): `drop publish --name MyDrop --directory Out` or `drop addfile --name MyDrop --file MyLib.dll`;
  3. finalizing the session: `drop finalize --name MyDrop`.

When these steps are not (or cannot be) integrated in a build, they are typically done in a post-processing step, after the build has finished.  This is clearly suboptimal, because there is no reason not to start uploading files as soon as they become available.

The steps to uploading a drop listed above can be performed in isolation, as separate process invocations of `drop.exe`, and, as such, can be integrated in a BuildXL build (by means of the existing SLAWD processes).  This approach, however, has major performance issues:

  - spawning a full-fledged drop.exe process for every file to be uploaded introduces a lot of overhead;
  - every invocation of drop.exe has to perform authentication, which is expensive and unnecessary;
  - uploading one file at a time is known to be suboptimal; instead,  batching files into groups performs much better in practice.

A more efficient approach is illustrated with a sequence diagram below.  In a nutshell, the idea is to first start a background process (called `dropd.exe` here), then for each of the operations listed above use some sort of RPC to communicate the operation to it, and have `dropd.exe` execute it.  In this architecture, `dropd.exe` can authenticate only once, batch files before actually uploading them, implement a retry logic, etc.

```
Command line                                                    || Sequence Diagram
--------------------------------------------------------------- || ------------------------------------------------------------
dropd                                                           ||                            dropd.exe (daemon)
                                                                ||                               |
dropc create --name MyDrop                                      ||   dropc.exe                   |
                                                                ||      |--- rpc (async) ------->|
                                                                ||      |<-----------------------| (perform auth)
                                                                ||      .                        | (execute command "create")
                                                                ||                               |
dropc addfile --name MyDrop --file MyLib.dll                    ||   dropc.exe                   |
                                                                ||      |--- rpc (async) ------->|
                                                                ||      |<-----------------------|
                                                                ||      .                        | 
                                                                ||                               |
dropc addfile --name MyDrop --file MyApp.exe                    ||   dropc.exe                   |
                                                                ||      |--- rpc (async) ------->|
                                                                ||      |<-----------------------|
                                                                ||      .                        | (read MyLib.dll)
                                                                ||                               | (read MyApp.exe)
dropc finalize                                                  ||   dropc.exe                   | (upload both together)
                                                                ||      |--- rpc (async) ------->|
                                                                ||      |<-----------------------| (execute command "finalize")
                                                                ||      .                        |
                                                                ||                               |
dropc stop                                                      ||   dropc.exe                   |
                                                                ||      |--- kill (async) ------>|
                                                                ||      .                        .
--------------------------------------------------------------- || ------------------------------------------------------------
```

#### Using the Roslyn Compiler as a Service

The same idea can be applied to compiling C# files: instead of running `csc.exe` for each project, Roslyn supports a "service mode", so it can be run as a background process, in which case individual compilation tasks are communicated to it via RPC.  Some internal reports indicate that this architecture leads to significant time savings [??].

```
Command line                                                    || Sequence Diagram
--------------------------------------------------------------- || ------------------------------------------------------------
cscd                                                            ||                            cscd.exe (daemon)
                                                                ||                               |
cscc /server /target:library /out:MyLib.dll MyLib.cs            ||   cscc.exe                    |
                                                                ||      |--- rpc (sync) -------->|
                                                                ||      |                        | (read MyLib.cs)
                                                                ||      |                        | (write MyLib.dll)
                                                                ||      |<-----------------------|
                                                                ||      .                        |
                                                                ||                               |
cscc /server /target:exe /out:MyApp.exe /r:MyLib.dll MyApp.cs   ||   cscc.exe                    |
                                                                ||      |--- rpc (sync) -------->|
                                                                ||      |                        | (read MyLib.dll, MyApp.cs)
                                                                ||      |                        | (write MyApp.exe)
                                                                ||      |<-----------------------|
                                                                ||      .                        |
                                                                ||                               |
cscc stop-service                                               ||   cscc.exe                    |
                                                                ||      |--- kill (async) ------>|
                                                                ||      .                        .
--------------------------------------------------------------- || ------------------------------------------------------------
```

(NOTE: at the moment there are no such tools named `cscc.exe` and `cscd.exe` that support this mode of operation, but again, all the necessary Roslyn infrastructure exists and can be readily used for this purpose. The example is here for illustration)

## Supporting Background Processes in BuildXL

### Features 

  1. A pip should be able to depend on a service pip being started (so that BuildXL may schedule it for execution as soon as the service has started);
  2. input/output specification of a service pip should be incremental and dynamic, meaning that after each call to a service operation, the spec for that service pip can potentially be widened (so that the service pip receives new file read/write permissions, appropriate for the operation at hand);  (**this has not been implemented**)
  3. there should be a mechanism for pips to depend on outputs that are produced by a service pip in the middle of its lifetime (so that they BuildXL may scheduled it for execution as soon as the required outputs are produced).  (**also never implemented**)


### Additional DScript API

The following are minimal additions to the existing DScript transformer API needed for the two motivating use cases:

```Typescript
// ===================================================================
// @filename: Prelude.Transformer.dsc
// ===================================================================

/** Different options for delegating permissions of a process to a service pip. */
export const enum PermissionDelegationMode { 
  /** Don't grant any permissions at all. */
  none,
    
  /** Grant permissions only throughout the lifetime of the caller pip. */
  temporary,

  /** Grant permissions permanently, i.e., until the service pip terminates. */
  permanent
} 

export interface ServiceId {}

export interface CreateServiceArguments extends ExecuteArguments {
  /** A command for BuildXL to execute at the end of /phase:Execute
    * to gracefully shut down this service. */
  serviceShutdownCmd?: ExecuteArguments;
}

export interface CreateServiceResult extends ExecuteResult {
  /** Unique service pip identifier assigned by BuildXL at creation time.  */
  serviceId?: ServiceId;
}

export interface ExecuteArguments {
  // <everything as is now>

  /** Regular process pips that make calls to one or more service 
    * pips should use this field to declare those dependencies
    * (so that they don't get scheduled for execution before all
    * the services have started). */
  servicePipDependencies?: ServiceId[];
  
  /** Whether to grant the read/write permissions of this pip to 
    * the declared service pips (permissions are granted only 
    * throughout the lifetime of this pip). */
  delegatePermissionsToServicePips?: PermissionDelegationMode;  /* NOTE: never implemented */
}

/** Schedules a new service pip. */
export declare function createService(args: CreateServiceArguments): CreateServiceResult;
```
    
### Implementation Considerations

To support the transformer API changes from the previous section, the engine would have to be updated as follows:

  - Returning a unique service id when a service pip is added to the build graph (as a result of evaluating `Transformer.createService(...)`) is trivial.
  - By default, BuildXL ensures that no service is started more than once (i.e., `Transformer.createService` is called only once per  service tool).
  - Once every process (non-service) pip has terminated, BuildXL executes the `serviceShutdownCmd` command for every running service pip.
  - The `servicePipDependencies` property introduces a new kind of pip dependency, one that is not file-based like the existing ones.  If this proves to be infeasible to implement, a reasonable simplification would be that BuildXL automatically starts all service pips before the execution phase (in any order), which ensures their availability before any regular process pips are scheduled.
  - Process detouring/monitoring must be modified so that it allows for enforcing dynamic permissions whenever requested via the `delegatePermissionsToServicePips` property.

### Error Handling

The existing BuildXL rule, stating that if any pip fails the build as a whole fails, stays unaffected by the changes proposed in this document. New kinds of pip failures that fall into this category (and thus should be treated exactly like pip failures are aready treated by BuildXL) include:

  - a service pip exited with an error code,
  - a pip failed because one of its `servicePipDependencies` has already stopped,
  - a `serviceShutdownCmd` failed, 
  - a service pip didn't terminate within the alloted pip execution timeout.
    
The proposed way of handling service pips, however, does introduce a number of new error cases that warrant special handling:

  - A service pip may die by causes of its own, before a pip that depends on it gets executed.  When that happens, BuildXL should report a new type of error, saying something like 'Pip X depends on a service Pip Y, but Y has died unexpectedly'.  This also implies that when a service pip dies, BuildXL might need to try to capture what happened (e.g., by looking at stderr) and hold that information until all the pips that depend on it are executed, just for reporting purposes.
  - If at the end of phase "Execute" there exist running service pips whose `serviceShutdownCmd` is not set, depending on configuration,
    BuildXL may chose to either:
      - terminate the service pips and treat it as a build success, or
      - terminate the service pips and treat it as a build failure, or
      - wait for the pips to terminate on their own.

### DScript Specification for the Drop Use Case

#### Uploading Drops Specification

```Typescript
import {input} from "Common.Artifact";
import {argument, option} from "Common.Cmd";

const dropcTool = { exe: f`dropc.exe` };
const dropdTool = { exe: f`dropd.exe` };

function uploadDrop() {
  // 1. start the dropd.exe service
  const dropdService = Transformer.createService({ 
    tool: dropdTool, 
    serviceShutdownCmd: {
      tool: dropcTool,
      arguments: [ argument("stop") ]
    }
  });
  
  // helper function
  const invokeDropc = (args: Argument[], ...deps: Transformer.ExecuteResult[]) => Transformer.execute({
    tool: dropcTool,
    arguments: args, 
    servicePipDependencies: [ dropdService.serviceId ],
    delegatePermissionsToServicePips: PermissionDelegationMode.permanent, // because of batching dropd might do
    dependencies: deps.mapMany(d => d.getOutputFiles())
  });

  const getFileOptions = (f: File) => [ option("--file", input(f)) ];
  
  // 2. implement the above "uploading symbols/drops" sequence diagram
  const name = "MyDrop";
  const createOp = invokeDropc([argument("create"),   option("--name ", name)]);
  const add1Op   = invokeDropc([argument("addfile"),  option("--name ", name), ...getFileOptions(f`MyApp.dll`)], createOp);
  const add2Op   = invokeDropc([argument("addfile"),  option("--name ", name), ...getFileOptions(f`MyApp.exe`)], createOp);
  const finOp    = invokeDropc([argument("finalize"), option("--name ", name)], add1Op, add2Op);
  
  return finOp;
}
```

### Caching Considerations

* Service pips should not be cached. A service starts only if some pip which requires the service needs to execute (cache miss).

* Sercice pips may choose on their own accord to skip work. In the drop example described above the `dropd.exe` service pip makes sure the external service is aware of the output file but it will avoid uploading content when the remote system already has the content.

### Sandbox Considerations

Service pips are just like any other process in that they are monitored by the file access sandbox. However, all file accesses are allowed. This is not user configurable. This essentially means service pips are trusted with respect to their input specification. But service pips are never cached and the work they perform is generally dictated by IPC pips. So this concession isn't large is practice.

### Distributed build considerations

* A service pip is started on **every** worker in a distributed build (which in contrast to regular pips, each of which gets executed on a single dynamically chosen worker).
