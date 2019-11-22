The `BuildXL.stats` file contains (string, ulong) pairs for various stats that happen throughout the build. It is a simple format that is machine parsable. The data contained within the log may change, but the schema of the file is locked.

## Units and Conventions
Convention | Description
--- | ---
*Ms* | Suffix noting that the stat is how long something took in milliseconds
*TimeTo* | Prefix indicating the wall clock time from the beginning of the build starting until some event happens.
*Count* | A count of how many times something happened within the build session.

## Phase stats
Some stats are logged in a standard way at the end of each engine phase. They are prefixed with the phase name, but the remaining items are consistent.

Statistic Name | Description
--- | ---
DurationMs | Duration the phase lasted
ProcessAverageThreadCount | Average total thread count of the bxl.exe process during that phase
ProcessMaximumPrivateMB| Maximum private MB of memory held by the bxl.exe process during the phase
MachineAverageCPUTime| Average CPU time on the machine during the phase. This is not limited to the usage of the bxl.exe process
MachineAverageDiskActiveTime.[DriveLetter]|The percent active time for each logical disk during the phase. 0 is an idle disk, 100 is a disk with no idle time during the period

# Stats
There are many more stats emitted during a build invocation than just the set described below. Search the source code to see how a statistic is being set if you don't see it listed here.
Statistic Name | Description
--- | ---
TelemetryInitialization.DurationMs| Time it took to initialize the connection to telemetry
CacheInitialization.DurationMs|Time taken to initialize the cache. Some of this time is overlapped with other operations happening concurrently.
CacheInitialization.OverlappedInitializationMs| Portion of cache initialization that happens in the background is overlaps other concurrent work.
CacheInitialization.TimeWaitedMs| Non-overlapped time waiting for the cache to initialize. A nonzero amount means the build was waiting on the cache to initialize.
FileCombiner.BeginCount|Number of files in the FileCombiner when it is initialized. The FileCombiner is used by the spec file cache and for some DScript incremental frontend operations.
FileCombiner.InitializationTimeMs| Time the FileCombiner spent initializing. 
FileCombiner.UnreferencedPercent|Percent of files in the FileCombiner that were unreferenced at shutdown time
FileCombiner.FinalSizeInMB|Final size of the FileCombiner in MB.
FileCombiner.CompactingTimeMs|Time in milliseconds the FileCombiner spent compacting itself.
FileCombiner.EndCount|Count of files the FileCombiner had when it was shut down.
FileCombiner.Hits|Number of files that were satisfied by the FileCombiner.
FileCombiner.Misses|Number of files that were not satisfied by the FileCombiner.
ApplyingFilterToPips.DurationMs|Time spent computing which pips matched the pip filter. This does not include the time spent scheduling the pips that match the filter.
ApplyFilterAndScheduleReadyNodes.DurationMs|Time spent filtering and scheduling nodes. This time includes ApplyinfFilterToPips.DurationMs.
TimeToFirstPipExecutedMs|Time from the beginning of the build until the first external process that is launched. This could also be seen as the time to the first cache miss. If the build has no cache miss, this will be the time until the end of the execute phase.
TotalCacheSizeNeeded|Total size, in bytes, of all output files and content put into the cache. This is larger than just the output files of the build because it includes additional metadata needed for cache processing.
OutputFilesChanged|Number of output files that changed from the last build.
OutputFilesUnchanged|Number of output files that did not change from the last build.
SourceFilesUnchanged|Source files that did not change from the last build.
SourceFilesChanged|Source files that changed from the last build
SourceFilesAbsent|Source files that existed in a prior build but are now absent.
OutputFilesProduced|Output files that were produced uniquely in this build. This number will not include output files from cache hits.
OutputFilesCopiedFromCache|Output files that were replayed from cache.
OutputFilesUpToDate|Output files from cache hits that were already in the correct state on disk
CriticalPath.ExeDurationMs|Length of the build's critical path in milliseconds, measured by adding the time external processes in the critical path were running. Cache hits do not have any time recorded here since they do not cause external processes to be launched. So this would be 0 for a fully cached build.
CriticalPath.PipDurationMs|Lengh of the build's critical path in milliseconds that includes both externally running process time and  BuildXL'a overhead before & after running processes.
GraphCacheReuseCheck.DurationMs|Time spent checking if there is a pip graph that can be reused.
TimeToFirstPip|Time from build invocation until the first pip starts processing.
TimeToFirstPipSynthetic|Similar to TimeToFirstPip, except it is calculated at the end of graph construction. This means it may be slightly smaller than TimeToFirstPip, but it will always be nonzero even if the execute phase is not run
