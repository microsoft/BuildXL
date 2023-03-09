# File Materialization
File materialization in BuildXL refers to the state of output files on disk after the build has completed. If they exist in a verified state, they are deemed to be "materialized".

## Background - Caching
In the presence of caching, not every build task (i.e., process invocation that is a part of the build) needs be executed every time. The cache stores a mapping from a process invocation (including the process executable and all of its input parameters) to a set of output files the process produced.  Assuming that build tasks are deterministic (which BuildXL does), if a process invocation is found in the cache, its outputs can be fetched straight from the cache, without re-running the process.

## Lazy Materialization
It is most efficient to materialize the minimal amount of content from the cache. This is especially true if using a remote cache. BuildXL utilizes an algorithm called **Lazy Materialization** where it does not eagerly bring process' output files from the cache to disk. Instead, each output file is materialized only if:

  1. It was explicitly requested by the user, or
  1. A build task that depends on it (i.e., explicitly specifies that file as an input dependency) is scheduled for execution.

Lazy materialization is enabled by default in BuildXL and is controllable via the `/enableLazyOutputs` flag.

### Explicitly requesting outputs
Outputs are explicitly requested by use of [pip filtering](How-To-Run-BuildXL/Filtering.md) with the `/filter:` command line argument. Outputs of pips matching the pip filter are considered to be explicitly requested. Pip that are scheduled to satisfy dependencies of filter matching pips are not considered to be explicitly requested and their outputs will not be materialized when possible.

When no pip filter is specified, all outputs are considered to be explicitly requested and therefore lazy materialization will not be utilized.

### Materialization options
`/enableLazyOutputs` has the following modes of operation:
* `/enableLazyOutputs`  Enables lazy output materialization in its standard mode. It is the default if not specified
* `/enableLazyOutputs-` Explicitly disables lazy materialization. All outputs will be materialized as cache hits happen
* `/enableLazyOutputs:minimal` Outputs will only be materialized when they are dependencies of a cache miss. This is the minimal amount of materialization possible and thus the most performant mode. Upon a 100% cache hit, no output files will be materialized on disk. No materialization will be performed even for pips matching the filter specified on the command line.

## Applications
Builds are often times used mainly to validate the correctness of a build, i.e., ensure that all projects successfully compile and all tests (and any other validations) pass. In such a setting, producing every build file artifact and ensuring that it is materialized to disk and placed in its designated output folder becomes unnecessary. Pull Request validation builds frequently fall into this category and benefit from `/enableLazyOutputs:minimal`

Developer machine builds are typically performed both to validate the build succeeds and to make the results of the build available for testing or other uses. Here, the default lazy output materialization is the best option. A developer may filter the build to the part of the build graph they are working on, say a specific executable. If a remote cache is being used, transitive dependencies of the executable included in the filter will not be downloaded from the remote cache. Only what is necessary to build their unique changes will be materialized.