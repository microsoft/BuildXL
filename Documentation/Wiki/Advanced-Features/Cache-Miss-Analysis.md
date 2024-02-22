# Cache miss analysis

## Cache lookup
A simplified explanation of BuildXL's cache lookup algorithm is:
1. Collect inputs of a process pips. This can be file content, whether files exist, directory enumeration listings, process command lines, environment variables, the identity of the process itself (e.g. the hash of the executable), and other BuildXL settings that influence how a process is run (e.g. which file accesses are untracked).
1. Create a "fingerprint" which is a hash of all of those inputs and query the cache to see if it has a record from a previous invocation.

When a build has a cache miss, the difference in the fingerprint isn't particularly helpful since that is just a content hash. The useful thing is to compare the data used to create the fingerprint. Unfortunately this poses some challenges:
1. The raw data that goes into fingerprints can grow to be very large, even excluding file content.
1. One must know what input data to compare against. Diffing fingerprint input data for a specific pip that was a cache miss across all entries in the cache is not feasible. Even filtered down to a stable pip identifier, every pip across all invocations from that pip across past build sessions does not give a cohesive answer. It would be best to compare all pips against a specific past build that is expected to be a good baseline.
1. Not every build executes every pip. Comparing against the last build might not yield any data if a pip were filtered out in the immediately prior build.

### Fingerprint Store
Due to these reasons, BuildXL has a separate database for cache miss analysis data called the *FingerprintStore*. This is a store which holds one complete build's worth of fingerprint input data. Data is carried forward across builds and overwritten on a pip by pip basis. This ensures that if a pip is filtered out from the current build, subsequent builds can still get cache miss analysis data for future misses of that pip.

Cache miss analysis is not currently enabled by default. It can be enabled by the `/cachemiss` flag on the command line.

## Runtime Cache Miss Analysis
Doing cache miss analysis at runtime is more convenient than doing it postmortem - there are no logs to download or tools to run - one just needs to check the `BuildXL.CacheMiss.log` file (note: the file might have a different name if `/logPrefix` argument was used to modify the default prefix). Runtime cache miss analysis can be enabled with the `/cachemiss` command line flag. This will perform cache miss analysis comparing processes that are misses in the current build session with the last time they ran on the same machine.

### Selecting a Baseline and utilizing a remote cache
The FingerprintStore is separate from the cache actually delivering cache hits. So comparing against the wrong FingerprintStore baseline could yield incorrect results.

The FingerprintStore database itself is stored in the content cache for use in future builds. This is useful when the build is configured to use a remote cache. The `/cachemiss` option can take a key to control which FingerprintStore database to use. The newest matching store will be used. For example a rolling build pipeline may want to use `/cachemiss:pipelineName` so it performs analysis on the last completed invocation of the pipeline. When the build is complete, it will add the FingerprintStore to the cache utilizing the same key (if the cache is configured to be writeable). When no key is specified, BuildXL will only ever utilize the local FingerprintStore even if BuildXL is configured to use a remote cache.

### Automatic baseline selection for builds running in Azure DevOps
In Azure DevOps builds, the default `/cacheMiss+` runs with a heuristic in which multiple keys are selected automatically: this this find a baseline store in precedence order. For example, consider a rolling build that publishes to the cache and developer builds that pull from the same cache. When a developer publishes a pull request, they want the cache miss baseline to be the newest rolling build that is closest to their commit. We can use a series of recent commit hashes as candidate keys for the store, trying to pick candidate keys that might have been used to publish a fingerprint store for a build as "close" as the one running.

For this, candidate keys are considered in the following order:
1. The current branch the build is running from (e.g: `/refs/heads/dev/foo/MyFeature`, or `/refs/heads/pull/129192`)
2. If running as part of a pull request, the source branch for the PR (e.g., `/refs/heads/dev/pr/myPrBranch`)
3. If running a part of a pull request, the target branch for the PR (e.g., `/refs/heads/main`)

The rationale being:
1. If a baseline build is ran regularly on the target branch, the first build in the PR branch will have a fingerprint store to compare
1. Subsequent builds within a same branch (PR or otherwise) will find the fingerprint store produced by the last build in the branch

At the end of the build, the first key in the series is what is used to store the FingerprintStore in the cache (namely, using the current branch as a key). Subsequent builds in the same branch (PR or otherwise) will find the fingerprint store produced by the last build. 

### Telemetry (Microsoft internal)
When available, cache miss analysis data is pumped to telemetry. See the Telemetry section in the internal documentation at https://aka.ms/BuildXL

## Postmortem Cache Miss Analysis
Cache miss analysis can be performed after a build finishes. This gives more flexibility on exactly what to compare.

### Cache Miss Analyzer
A version of cache miss analysis that can handle incremental scheduling and graph filtering. The analysis relies on a persistent database that stores pip fingerprint data build-over-build. The analyzer uses snapshots of the database that are stored in the BuildXL logs folder. 

To use this analyzer, set the mode to **/m:CacheMiss** and the **/xl:** parameters to the **BuildXL log folders** rather than the BuildXL.xlg file. For example:
`bxlAnalayzer.exe /m:CacheMiss /xl:F:\src\buildxl\Out\Logs\20171023-125957 /xl:F:\src\buildxl\Out\Logs\20171023-130308 /o:f:\src\buildxl\cachemiss`

The `analysis.txt` file in the output directory shows the first pip in each dependency chain that was a cache miss as well as the reasons for the miss. Full fingerprint computation inputs for each analyzed pip are kept in the "old" and "new" subdirectories; there will be a file for each pip's `SemiStableHash`.

**Note:** This analyzer will only work with logs from BuildXL builds with a `/cachemiss` mode enabled or `/SaveFingerprintStoreToLogs+` which creates analysis data without performing the analysis at runtime


### Legacy Cache Miss Analyzer
This method of analysis remains for builds without `/cachemiss` or /storeFingerprints` enabled. See Cache Miss Analyzer above for cache miss analysis with incremental scheduling and graph filtering. Use this analyzer to compare two distinct builds, to see which pips were cache misses in the second build, and why.

To use this analyzer, set the mode to `/m:CacheMissLegacy`, set the `/xl:` parameters to the full path to the execution logs of both builds (first build first, then the build you want to analyze against), set `/o:` to the full path of an output directory. For example:
`bxlAnalayzer.exe /m:CacheMissLegacy /xl:F:\src\buildxl\Out\Logs\20171023-125957\BuildXL.xlg /xl:F:\src\buildxl\Out\Logs\20171023-130308\BuildXL.xlg /o:f:\src\buildxl\cachemiss`

The "analysis.txt" file in the output directory shows the first pip in each dependency chain that was a cache miss as well as the reasons for the miss. Full fingerprint computation inputs for each analyzed pip are kept in the "old" and "new" subdirectories. There will be a file for each Pip's `SemiStableHash`.

### Diff Format

The new cache miss analyzer produces diff outputs in the form of Json. The new cache miss analyzer offers two different diff formats. The first format, called *CustomJsonDiff*, is a custom diff format resulting from our own diff algorithm that understands the semantics of weak and strong fingerprints. 

The second diff format is *JsonDiffPatch*. This format shows the diff as a delta between two Json representations. To output this format, BuildXL relies on an external diff algorithm. References about the algorithm and the diff syntax can be found in the following links: 

[General diff syntax reference](https://github.com/benjamine/jsondiffpatch/blob/master/docs/deltas.md)

[Array diff syntax reference](https://github.com/benjamine/jsondiffpatch/blob/master/docs/arrays.md)

The default diff format is CustomJsonDiff. One can specifying explicitly the diff format to use by using `/cacheMissDiffFormat:<CustomJsonDiff|JsonDiffPatch>`

#### Known Limitations
The cache miss analyzer works correctly under the assumption that the two builds being compared shared the same graph scope and processed all of the same pips through the full scheduling algorithm. When this assumption is false, the analyzer may produce the following messages:

- `Pip is missing from old graph`
The pip is a new node that was added to the graph since the previous build.

- `No fingerprint computation data found to compare to since pip was skipped or filtered out in previous build.`
Changing the filter of a build or using incremental scheduling can cause pips to be (correctly) skipped. Cache miss analysis is limited when pips are skipped because not all of the data required for the comparison is generated.

#### Additional Information from Incremental Scheduling
When incremental scheduling causes the source data to not exist, information about what changed may exist in the normal BuildXL.log file. Look for messages similar to:

`[0:03.519] verbose DX8054: >>> PipA06BC4F97BCE2784 is dirty => Reason: Dynamically observed file (or possibly path probe) 's:\git\os2\os\obj\x86fre\objfre\i386\__bldlanguage__.props' changed | Path change reason: DataOrMetadataChanged`

The message shows that a particular pip was marked as dirty because `__bldlanguage__.props` changed from the prior build.

Messages like this, are potential differences that were then verified to be the same. Therefore they are not causes for cache misses
`[0:03.549] verbose DX8011: Path 's:\git\os2\os\obj\x86fre\analog\Apex\enterprise\assignedaccess\lib\ucrt\objfre\i386\eventtoken.h' was potentially added, but verified actually absent (re-tracked)`
