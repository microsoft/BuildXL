Incremental scheduling in BuildXL is a feature that allows BuildXL to avoid processing pips based on the filesystem's change (or USN) journal records tracked from the previous builds. Incremental scheduling can also be viewed as a technique to prune the pip graph on up-to-date pips. Incremental scheduling can be enabled by passing `/incrementalScheduling+`.

## Pip processing
When a pip is ready to be processed, i.e., all its dependencies have been processed, BuildXL performs the following tasks on the pip:
1. Check if the pip can be run from cache. This check amounts to computing the pip weak and strong fingerprints,  and looking-up in the cache.
2. If the cache lookup results in a cache hit, then BuildXL deploys the pip outputs from the cache when necessary.
3. If the cache lookup results in a cache miss, then BuildXL executes the pip, which amounts to launching the executable specified by the pip.
4. If BuildXL executes the pip, then, after the pip finishes its execution, BuildXL process the pip's outputs to record dynamic outputs/inputs, and pushes the outputs to the cache.

Step 1 shows that BuildXL depends on the cache look-up to check the up-to-dateness of a pip.

Suppose that we have the following pip dependency graph:
```
    fileA <-- Process1 <-- fileB <-- Process2 <-- fileC <-- Process4
                                                               |
                                                               V
                                     fileD <-- Process3 <--  fileE
                                                     
```
Assume that our cache is empty at the beginning. The first build of the above graph, which is a clean build, performs steps 1, 3, 4 for every process pip in the graph.
Now, before we do the next build, we modify `fileD`. Without incremental scheduling, in the next build, BuildXL performs steps 1, 3, 4 for `Process3` and `Process4` (assuming `fileE` changes after `Process4` execution), and performs step 1 and 2 for `Process1` and `Process2`. That is, without incremental scheduling, BuildXL computes pip fingerprints and performs cache look-up's, even though the outputs of `Process1` and `Process2` exist and do not change from the previous build.

## Incremental scheduling
With incremental scheduling, during the build, BuildXL tracks the filesystem journal USN records of input and output files. When a file is modified, BuildXL is able to compare the recorded USN with the current one, and if the USNs are different, then BuildXL marks the consuming/producing pips dirty, i.e., the pips need to be processed. (Invariant: If a pip is marked dirty, all its transitive dependents are marked dirty.)

In the above example, BuildXL is able to mark that `Process1` and `Process2` are clean, and thus it can avoid scheduling those process pips. In essence, BuildXL will only see the following pip graph:
````
                                                  fileC <-- Process4
                                                               |
                                                               V
                                     fileD <-- Process3 <--  fileE
````

Incremental scheduling works in the presence of pip filtering. Pip filtering is a mechanism that allows users to explicitly select pips to process based on the user-supplied filter expression. Pip filtering and incremental scheduling are orthogonal features, and using both together can speed up builds. Pip filtering is applied to the pip graph before incremental scheduling is applied to the graph. That is, the incremental scheduling is only applied to the smaller filtered pip graph.

Due to the lazy output materialization feature, BuildXL also tracks the facts whether pips materialize their outputs. If a pip has not materialized its outputs, but then a user request that specific pip to be built, then that pip is marked dirty.

## Feature incompatibility
Incremental scheduling is a cross-cutting feature. In this section we describe features that are incompatible with incremental scheduling or that make incremental scheduling sub-optimal. Some features can be incompatible with incremental scheduling, for example, building in Cloud.

| Incompatible        | Sub-optimal                         | 
| ------------------ | -----------------------------|
| Distributed build  | Uncacheable whitelist           |
|                             | Anti-dependency                 |
|                             | Lazy output materialization  |
|                             | Shared opaque directory      |                       
|                             | Unflushed page cache          |
|                             | Drops                                  |


### Distributed build
Incremental scheduling is incompatible with distributed builds because pips built on datacenter machines rely on a peer-to-peer cache to get inputs that are produced by other pips built on different machines. In the above example, suppose that `Process3` is built on machine `W3`, and `Process4` is built on machine `W4`. When `Process3` finishes, the machine puts the output `fileE` to the cache. `Process4` simply gets `fileE` from the cache, but it cannot get `fileC` because it is not guaranteed to be in the cache because of possible eviction, as no one builds it. `fileC` may exist on the master build machine.

BuildXL disables incremental scheduling completely when distributed build is requested.

## Uncachable whitelist
BuildXL allows users to specify files in the configuration file that are whitelisted on checking access violation. Some pips may produce or consume files in that whitelist. If those files are in so-called uncacheable whitelist, then those pips will not be cached, and so they are expected to be executed in every build. For example, suppose that `fileX` below is in the uncacheable
whitelist, e.g., `fileX` may contain date/time when `Process42` is executed: 
```
    Process42 <-- fileX <-- Process43
        ^                       |
        |                       |
        +-------- fileY <-------+
```

Incremental scheduling marks such a pip perpetually dirty, and so BuildXL will keep processing the pip. This essentially makes incremental scheduling feature sub-optimal.

## Anti-dependency
Anti-dependencies are caused by probing non-existent files, such as a search for a C++ header file in an ordered list of search directories. BuildXL observes anti-dependencies during pip executions. However, incremental scheduling does not use the result of such observations. Currently, when a user introduces a file that was probed absent in the prior builds, then incremental scheduling will simply assume that all nodes in the graph are dirty.

## Lazy output materialization
BuildXL has a feature that lazily materializes outputs when a pip can be run from the cache (`/enableLazyOutputs`). Let's consider the following example:
```
    Process1 <-- fileX <-- Process2 <-- fileY <-- Process3
                               |
                     fileZ <---+
```
Suppose that the user performs the initial clean build by explicitly requesting only `Process3`, perhaps by using a filter. In processing `Process1`, BuildXL can run the pip from the remote cache, i.e., someone has already built `Process1`. Because of lazy output materialization, `fileX` is not materialized on the disk, i.e., BuildXL simply records the content hash of `fileX` that it obtains during the cache look-up. Similarly, suppose that `Process2` can be run from the cache as well, and so `fileY` is not materialized on disk. Now, on processing `Process3`, it turns out that BuildXL needs to execute `Process3` because it cannot run from the cache. To this end, BuildXL tries to materialize `Process3`'s dependency, `fileY`, which in turn makes `Process2` fully materialize its output. 

In the above scenario, incremental scheduling will mark `Process2` and `Process3` clean and materialized, but only mark `Process1` clean. If `Process2` had another output, say `fileW`, that is not consumed by anyone, then at the end `Process2` will only be marked clean because it has not fully materialized its outputs.

If we do another build without changing anything, then at the beginning of the build, before processing the pips, incremental scheduling will change the marker of `Process1` to dirty, which in turn makes `Process2` and `Process3` dirty as well. Thus, in this case incremental scheduling cannot prune the graph. Suppose that incremental scheduling did not change the marker of `Process1` to dirty. If now the user modifies `fileZ` and requests to build `Process2`, then `Process2` needs to execute, but since `Process1` is clean, `Process2` simply assumes that `fileX` is on disk, but it is not. This is the rationale of having clean and materialized markers for pips.

## Shared opaque directory
Process pips can produce output directories. The content of those directories are unknown until the pips produce them. The output directories can be consumed directly by pips, without the consuming pips know the contents of the directories. Such output directories can also be shared by more than one process pip. Such directories are called shared opaque directories. 

For correctness, the contents of shared opaque directories are deleted before BuildXL begins pip processing. Thus, in principle, pips that produce shared opaque directories need to always be processed. Incremental scheduling marks such pips perpetually dirty.

## Unflushed page cache
Incremental scheduling tracks input and output files by recording their USN records. To get a stable record for an output file, BuildXL flushes the page cache to the file-system.
Unfortunately, on spinning disks, page cache flushes can be expensive. If we turn off page cache flush, then in the next build BuildXL detects that the USN records of some output files may have changed because the OS flushed the page cache after the output file was tracked. Thus, BuildXL marks the pips that produced those output files dirty.

To keep flushing page cache on tracking or storing outputs to cache, one can pass `/flushPageCacheToFileSystemOnStoringOutputsToCache+`.

## Drops
BuildXL has a distinct kind of pip, called an IPC pip, that is used to drop files to some artifact store. If a user modifies some input files, and re-runs the build that drops the produced output files, then we have the following issues:

1. If the drop name used by the current build is the same as that of the previous builds, then the file cannot be dropped because the previous build has already finalized the drop name.
2. If the drop name is different, then each IPC pip that drops the file needs to be executed in order to create the same set of drops as in the previous builds.

To this end, incremental scheduling simply marks IPC pips perpetually dirty, and so they are always processed.