# External On-Demand File Materialization API

This document describes an IPC-based API that BuildXL provides so that external processes can use to it explicitly request that a file be materialized on disk.

## Background

### Caching

In the presence of caching, not every build task (i.e., process invocation that is a part of the build) needs be executed every time. The cache stores a mapping from a process invocation (including the process executable and all of its input parameters) to a set of output files the process produced.  Assuming that build tasks are deterministic (which BuildXL does), if a process invocation is found in the cache, its outputs can be fetched straight from the cache, without re-running the process.

### Lab Builds and Lazy Materialization

Builds are often times used mainly to validate the correctness of a build, i.e., ensure that all projects successfully compile and all tests (and any other validations) pass.  In such a setting, producing every build file artifact and ensuring that it is materialized to disk and placed in its designated output folder becomes unnecessary.  For that reason, BuildXL implements a feature called *lazy materialization*, which allows the user to precisely specify which files must be materialized by the end of the build.  With lazy materialization turned on, upon a cache hit BuildXL does not eagerly bring process' output files from cache to disk; instead, each output file is materialized only if

  1. it was explicitly requested by the user, or
  1. a build task that depends on it (i.e., explicitly specifies that file as an input dependency) is scheduled for execution.
     
## Problem

Some build tasks cannot statically decide exactly which input files they may need during execution.  In such cases, their build specification must be conservative and declare them all as input dependencies.  From BuildXL's perspective, all those declared files must unconditionally be materialized before the task is executed. 

In practice, if a build specifies a large number of such build tasks (which end up not accessing some their explicitly declared input files), the cost of unnecessarily materializing files ahead of time may become significant.

A remedy described in this document consists of an external API that BuildXL provides for on-demand file materialization, so that BuildXL can delay file materialization in such cases until explicitly requested by the build task.

## Practical Motivation: Uploading Build Artifacts to a Drop Endpoint

At Microsoft, a common goal of lab builds is to upload build artifacts to a Drop service endpoint.  The Drop service, however, is not a quintessential property of a general-purpose build engine.  For that reason, there is no support for Drop that is baked into the BuildXL engine.  Instead, it is provided by an external process (which may be configured by the user like any other build task).  In the rest of this document, this process will be referred to as *DropDaemon*.

For a file to be added to drop, DropDaemon first calls “Associate” for that file against a configured drop service endpoint.  This operation requires only the VSO hash of the file and returns whether the file already exists in the remote drop endpoint; if it does, it just associates that file with the drop name, which completes adding the file to the drop; otherwise, DropDaemon needs to read the file from disk and upload it to the drop.

This is a typical journey of a file before it becomes a part of a drop
```
                                                         __________
    +---------+                  +----------------+     /          \
    | csc.exe |---> file.dll --->| dropd.exe |--->| drop cloud |
    +---------+                  +----------------+     \__________/
```
If executing “csc.exe” was a cache hit, then `file.dll` didn't have to be brought from cache to disk (due to lazy materialization).  Next, running “Associate” on it (by dropd.exe) only requires the VSO hash of the file; BuildXL can provide that VSO hash to dropd, so `file.dll` still doesn't have to be materialized.  Finally, if “Associate” returned true (i.e., the file already exists in the drop cloud), `file.dll` has been added without ever having to be materialized.

Large builds will typically have a lot of cache hits, so this optimization is deemed essential.

Obviously, there will be cases when dropd will have to physically access and upload files to drop cloud.  In those cases, dropd will use the on-demand materialization API provided by BuildXL to explicitly request file materialization for those files.

## The API

Command line arguments for issuing an "addfile" request to dropd look like:

```
    dropd addfile                              
      --file <absolute-file-path-on-local-disk>
      --dropPath <relative-drop-path>
```

This makes BuildXL eagerly materialize the input file before every invocation of "dropd addfile".

Leveraging the on-demand file materialization API, the arguments will look like:

```
    dropd addfile                          
      --file <absolute-file-path-on-local-disk>  
      --fileId <file-identifier>
      --hash <vso-hash>                          
      --dropPath <relative-drop-path>            
      --server <ipc-moniker>
```

Corresponding DScript specification is

```ts
    [
        Cmd.option("--fileId ", Artifact.fileId(file)),
        Cmd.option("--hash ", Artifact.vsoHash(file)),
        Cmd.option("--dropPath ", dropPath),
        Cmd.option("--ipcServerMoniker ", Transformer.getIpcServerMoniker())
    ]
```

The 2 new DScript ambient functions in the specification above are:

  - `Artifact.fileId: File => string` 
    For a given file it returns a (BuildXL-internal) unique file identifier (which will have to consist of the path id, and rewrite count).  This identifier need not be stable across BuildXL invocations. 
  
  - `Transformer.getIpcServerMoniker: () => Artifact.IpcMoniker`
     Returns an IPC moniker identifying the IPC server BuildXL will run     to provide a back end for the external API.
  
Conceptually, the API will provide the following operation:

```ts
    /// <summary>
    ///     Materializes the file identified by the given <paramref name="fileId"/>
    ///     to disk.  When successful, returns the full path where the file was 
    ///     materialized.
    /// </summary>
    string MaterializeFile(string fileId)
```
    
In practice, dropd will wrap this operation in an object of the `IpcOperation` class, it will issue it as an IPC call via the received IPC moniker, and it will receive a result as an object of the `IpcResult` class.
