By default, BuildXL does not log the reasons why it decided to run a pip instead of bringing it from cache. While some cache misses are expected or easily explained, e.g. changes to the code base, some other cache misses might tricky to classify. To help customers understand why they experience cache misses in their builds, BuildXL ships with tooling for doing cache miss analysis. Such analysis can potentially identify problems in a build that cause extra cache misses. Customers can elect to do the analysis at runtime, which will add some overhead to the overall build time, or after a build has finished.

## Runtime Cache Miss Analysis
Doing cache miss analysis at runtime is more convenient than doing it postmortem - there are no logs to download or tools to run - one just needs to check the `BuildXL.CacheMiss.log` file (note: the file might have a different name if `/logPrefix` argument was used to modify the default prefix). Runtime cache miss analysis can be enabled with the `/cachemiss` command line flag. This will perform cache miss analysis comparing processes that are misses in the current build session with the last time they ran on the same machine.

Enabling cache miss analysis on distributed builds requires or on build sessions where the machine performing the build may not have been the machine that previously added the pip into the cache requires additional configuration.

## Postmortem Cache Miss Analysis
Currently there are two [analyzers](./Execution-Analyzer.md) that can generate a report describing the reasons for cache misses between two builds. The main difference between the analyzers is the fingerprints they compare --- the legacy analyzer compares cache lookup time fingerprints while the new analyzer compares execution time fingerprints. Because of this, in some scenarios, the analyzers might report different hashes for the same pair of builds; this does not affect the classification of cache misses.

### Cache Miss Analyzer
A version of cache miss analysis that can handle incremental scheduling and graph filtering. The analysis relies on a persistent database that stores pip fingerprint data build-over-build. The analyzer uses snapshots of the database that are stored in the BuildXL logs folder. 

To use this analyzer, set the mode to **/m:CacheMiss** and the **/xl:** parameters to the **BuildXL log folders** rather than the BuildXL.xlg file. For example:
`bxlAnalayzer.exe /m:CacheMiss /xl:F:\src\buildxl\Out\Logs\20171023-125957 /xl:F:\src\buildxl\Out\Logs\20171023-130308 /o:f:\src\buildxl\cachemiss`

The `analysis.txt` file in the output directory shows the first pip in each dependency chain that was a cache miss as well as the reasons for the miss. Full fingerprint computation inputs for each analyzed pip are kept in the "old" and "new" subdirectories; there will be a file for each pip's `SemiStableHash`.

**Note:** This analyzer will only work with logs from BuildXL builds with **/storeFingerprints** enabled. This is **enabled** by default on desktop builds, and can be disabled by passing /storeFingerprints-.

### Legacy Cache Miss Analyzer
This method of analysis remains for builds without **/storeFingerprints** enabled. See Cache Miss Analyzer above for cache miss analysis with incremental scheduling and graph filtering. Use this analyzer to compare two distinct builds, to see which pips were cache misses in the second build, and why.

To use this analyzer, set the mode to **/m:CacheMissLegacy**, set the **/xl:** parameters to the full path to the execution logs of both builds (first build first, then the build you want to analyze against), set **/o:** to the full path of an output directory. For example:
`bxlAnalayzer.exe /m:CacheMissLegacy /xl:F:\src\buildxl\Out\Logs\20171023-125957\BuildXL.xlg /xl:F:\src\buildxl\Out\Logs\20171023-130308\BuildXL.xlg /o:f:\src\buildxl\cachemiss`

The "analysis.txt" file in the output directory shows the first pip in each dependency chain that was a cache miss as well as the reasons for the miss. Full fingerprint computation inputs for each analyzed pip are kept in the "old" and "new" subdirectories. There will be a file for each Pip's `SemiStableHash`.

### Diff Format

Both cache miss analyzers use *JsonDiffPatch* to diff *WeakFingerprint* and *StrongFingerprint* json files. If you are not familiar with json diff syntax, you can find the reference in the following links: 

[General diff syntax reference](https://github.com/benjamine/jsondiffpatch/blob/master/docs/deltas.md)

[Array diff syntax reference](https://github.com/benjamine/jsondiffpatch/blob/master/docs/arrays.md)


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
