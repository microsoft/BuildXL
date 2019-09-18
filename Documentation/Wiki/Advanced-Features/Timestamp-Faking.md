On the Window splatform BuildXL attempts to normalize file timestamps that spawned pips see. This is done to improve determinism and cache hit rates. Otherwise BuildXL would have to know which pips consider timestamps as inputs and which ones don't. Additionally, it would be expensive to preserve and coordinate timestamps of output files when they are produced multiple times in the same build graph or need to be transited across machines in distributed builds.

The mechanism by which timestamps are normalized is to change the timestamps in detoured file accesses.

In practice, process pips can encounter 3 types of timestamps:

1.  "New" - This is an arbitrary static point in time (sometime in the year 2002) that the vast majority of accesses fall into.
1.  "Real" - Actual file timestamps are allowed for files that are declared outputs and rewrites. BuildXL doesn't modify the timestamps for these files regardless of whether they are accessed for read, write, or both. **BUT** by default, all declared output files are deleted before a process pip runs. So in practice this rarely gets exercised unless specific overrides are set.
1.  "Old" - This is an arbitrary static time in 2001. The important part is that it is older than 'new.' This is the timestamp that rewritten files are set to before a process is launched. Rewritten files are not deleted before running a process pip. So technically it is just a special variant of "Real" above.

The behavior described above may be modified with a few command line arguments. Note that using either of the options below can change the behavior of tools in the build and lead to flaky or less deterministic builds.

bxl.exe accepts `/normalizeReadTimestamps-` as an argument to disable timestamp normalization. But remember that timestamps of output files may not necessarily be preserved after a pip runs since they get hardlinked into the cache to preserve space. If the file with the same content has earlier been produced and is in the cache, it will be deduped and will get the timestamp from the first time the file was introduced into the cache.
