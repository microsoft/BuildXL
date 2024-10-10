This page is a curated list of the release notes for releases after 0.20170619.4.0 and a manual copy of notable changes from each build before that. See the repo's commit history full details for what is included in each build.

---
---
# 0.1.0-20241004.1 (Release [29152776](https://dev.azure.com/mseng/Domino/_build/results?buildId=29152776&view=results))
-	Breakaway processes for Linux interpose sandbox.
- Change default destination for DScript profiler log to build log directory.
- Fixes for codex property assignment references.

# 0.1.0-20240920.2 (Release [29106031](https://dev.azure.com/mseng/Domino/_build/results?buildId=29106031&view=results))
-	Various tweaks for Codex analyzer
-	Fix flushing the xlg data on workers
-	Send cache logs to Kusto
-	Dependency Analyzer includes the file probes in observed inputs

# 0.1.0-20240906.8.1 (Release [29053112](https://dev.azure.com/mseng/Domino/_build/results?buildId=29053112&view=results))
-	Fix ContractException when return failure result if historic meta data is called after cancellation has been requested
-	Disable support for console hyperlinks
-	Conditionalize Windows specific perf stats based on OS
-	Demote Create Historic Metadata Cache Failure to Verbose from Warning.
-	Don't increment UpstreamCacheMissLongestChain length for pip that depends on a cache disabled pip

# 0.1.0-20240830.2 (Release [29011861](https://dev.azure.com/mseng/Domino/_build/results?buildId=29011861&view=results))
-	Improve hyperlinks handling on the console
-	Bug fixes

# 0.1.0-20240825.1 (Release [28989861](https://dev.azure.com/mseng/Domino/_build/results?buildId=28989861&view=results))
- Check if scheduler has been cancelled when ensure historic metadata cache loaded
- Add cache factory to construct a local cache with a remote blob cache
- Correct HOMEDRIVE env name in AmbienContext
- Let users specify the total build timeout using a CLI arg
- Add additional perf data to the result of PipExecutionPerformanceAnalyzer

# 0.1.0-20240816.4 (Release [403518](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=403518))
-	Tighter integration with 1ES Hosted Pool integrated cache resource
-	Correct dependency consistency of external packages
-	User configurable observed input type reclassifications
-	Error classification improvements for service pips
-	Address file locking issue when hashing inputs

# 0.1.0-20240809.1 (Release [401607](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=401607))
- Remove net7 qualifier
- Minor tweaks and improvements

# 0.1.0-20240802.2 (Release [400113](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=400113))
- Disable caching graph when InputTracker is not enabled.
- \[AdoBuildRunner\] Don't launch worker if the orchestrator build is over.
- Allow loading hosted pool cache configuration file from env variables.
- Add support for macOS arm64 to interop library.
- Kill tracees if tracer dies under ptrace in Linux.
- Add support for clone3 in the Linux sandbox.
- Update rocksdbsharp to include macos arm64 support.
- Avoid crashing build when attempt to load HistoricMetaDataCache after cancellation is requested.
- Assume blob cache credentials are DPAPI encrypted by default.

# 0.1.0-20240719.2.1 (Release [397894](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=397894))
- Fix theory detection in XUnit SDK
- Plugin initialization timeout fails the build
- Various improvements for AdoBuildRunner

# 0.1.0-20240712.10 (Release [395776](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=395776))
- Fix msbuild frontend when building solution
- Don't warn OS compatibility for Ubuntu 22.04
- Speed up ObjectPool
- Convert detours message count mismatch into an error on Linux Builds
- Log more information when bxl failed to open a file for hashing
- [Cache] Support per-container SAS tokens

# 0.1.0-20240705.2.2 (Release [394871](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=394871))
- Gracefully exit workers when released before attachment.
- Enable encryption for ephemeral cache.
- Added retry mechanism for handling transient errors in ADO build runner.
- Bump CB.QTest to 24.6.26.153636
- Added special handling for openat calls in ptrace sandbox.
- Track output directories created by the engine for the pip in the Output filesystem view.
- Filter out directory writes for observations passed to observed input processor.
- Docs are spelled checked.

# 0.1.0-20240622.1 (Release [391575](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=391575))
- Clean up IOHandler/AccessHandler code from MacOS Sandbox
- Refactor HistoricMetadataCache to handle BlobL3 topology
- Add BuildXLInfo and BuildXLPerfInfo
- Linux sandbox bugs fix
- Fix capture build properties for org and codebase
- Update PublishSymbols pool to BuildXL-DevOpsAgents-Selfhost
- Log error when there is no exit message from orchestrator

# 0.1.0-20240614.2 (Release [389957](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=389957))
-	Perf improvement for BlobL3 cache
-	Improvement for serialization of BuildXL config object
-	Debugging improvements for Observed Input Processor
-	Reclassify some DScript errors
-	Support for encrypting RPC channel used by cache

# 0.1.0-20240606.3 (Release [388286](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=388286))
- Don't save fingerprint salt as part of cached graph
- Fix CacheDump Analyzer Crashes when Trying to Dump a Failed Strong Fingerprint
- Log internal warning on open file descriptor spike
- Minor tweaks and fixes

# 0.1.0-20240531.3.1 (Release [387989](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=387989))
- Small tweaks for Ubuntu 22.04 support
- Update .NET runtime versions
- Update SBOM dependencies

# 0.1.0-20240525.1.1 (Release [386412](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=386412))
- Preserve errno on internal system calls from the Linux Sandbox.
- Fix checks for non-files on Linux Sandbox.
- Fix for probe on unset mount causing a graph (engine) cache miss.
- Fixes for Ubuntu 22.04 support with ptrace sandbox.
- Update the threshold for LimitProblematicWorkerCount to 0.9.
- Temporarily re-add dotnet7 BuildXL binaries.

# 0.1.0-20240517.7.3 (Release [384894](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=384894))
- Linux sandbox improvements
- Update the threshold for LimitProblematicWorkerCount
- Allowlist rules should not be affected by '\\?\' path prefix
- Lookup the grpc cert in the multiple stores
- Allow VCTIP to survive when it is launched by lib.exe
- Use the ADO invocation key as part of the fingerprint store key for cache miss analysis

# 0.1.0-20240510.14.1 (Release [382804](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=382804))
- Fail the build when the problematic workers exceed the half remote workers
- Enable QTest for .net core projects
- Move selfhost builds to a common pool
- Allow users to pass generated symbol meta data for sending symbol request
- Refactor linux sandbox
- Clean macOS sandbox code and logic
- Linux sandboxing - remove root jail support
- Some bug fixes

# 0.1.0-20240502.2.1 (Release [381485](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=381485))
- Handle non-existent paths with getcap
- Refine logging ReceivedReportFromUnknownPid to avoid synthetic accesses
- Do not check accesses when the provided paths can't be normalized
- Disallow the use of phase:Evaluate for the users.
- Reverted the change related to Catch consolenotconnected exception and exit buildxl.

# 0.1.0-20240426.5.1 (Release [379849](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=379849))
-	Improved debuggability for “Problematic Workers” with multi-build synchronization issues in CloudBuild
-	Update msvc version used for compiling RocksDB
-	Fixes for reparse point handling on linux
-	Security update for QTest package
-	Improved clone3 API support for linux sandbox
-	Crash fixes for linux
-	Make toolpath non-mandatory in file access allowlists

# 0.1.0-20240419.10 (Release [377534](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=377534))
- Fix issue in file tracking on Ubuntu 22.04
- Update Ninja integration for Ninja resolver
- Correct requesting user in telemetry for AzureDevOps builds
- Update MSVC version from 14.37 to 14.39 and fix binskim issues
- Deprecate /cacheLogToKusto command line arg
- Fixes for various build hang scenarios in CloudBuild
- Misc crash fixes

# 0.1.0-20240412.16 (Release [375925](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=375925))
- Retry by default processes that exited with Azure Watson 0xDEAD code on ADO.
- Linux sandbox refactoring work.
- Produce a valid statsperf.json JSON file
- Bug fixes

# 0.1.0-20240405.5 (Release [374323](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=374323))
- Fix passthrough env var treatment under JS custom scheduler
- Set shorter expiration for non-finalized symbol publishing requests
- Better IPC error handling
- Various fixes and improvements

# 0.1.0-20240329.3 (Release [372820](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=372820))
- Update Unix hard link error codes.
- Correct DeleteFIleW error code returned by detours.
- Refactoring Linux Sandbox.
- Add helptext for result filter on the pip execution performance analyzer.
- Pass operation timeout and max operation retries to drop daemon start arguments.
- Enable sandbox logging if log observed file accesses flag is set.
- Use OS specific node executable name in tool based JavaScript resolver.

# 0.1.0-20230317.0 (Release [284911](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=284911))
- [Linux sandbox] Propagate __BUILDXL_PTRACE_MQ_NAME through the process tree
- Spanify parsing of the Linux sandbox reports
- Decouple SandboxProcessPipExecutor out of Processes
- Track the time it takes to push outputs to the cache as part of perf info
- [Linux sandbox] Process pending reports from the FIFO even after the pip process tree has exited
- Capture observed inputs for failed & retried pips in the XLG
- Fix a crash caused by a race during build cancellation
- [Linux sandbox] Generate write accesses for destination on rewrite interposing
- Run ptrace sandbox as a daemon
- Add error handling to CreateFileStream() GraphAgnosticIncrementalSchedulingState.cs in to avoid crashing on failure during incremental scheduling.
- Handle pips that are too large to serialize to fingerprint store

# 0.1.0-20240314.11 (Release [369658](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=369658))
- Relax config calls so arbitrary expressions are accepted
- Add read-only mode to the blob cache client
- Add internalWarnings to DominoCompletion event
- Update BuildXL.Tools.Ninjson version
- Update APIScan yml to avoid using secret
- Change 1ESPT BuildXL parameters after schema changes
- Always report symbol publishing in CB
- Publish new external cache packages
- Hook up command line fingerprint salt that accumulates
- Some bug fixes

# 0.1.0-20240307.8.1 (Release [369197](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=369197))
- Perf improvements in cache stack for file copies in local builds
- Improvements for cache hit rate on serviceless BlobL3 cache (1ES HP cache scenarios)
- Crash fixes
- Improvements for distributed worker connectivity rate for Office builds
- Update to net8

# 0.1.0-20240301.2  (Release [366837](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=366837))
- Add a lage location to the lage resolver
- Fix crash when we receive the ack for Attach call after AttachCompleted
- Stop using legacy .net symbols
- Add AAD support to blob-based cache clients
- Logging improvements on ADO
- Add open file descriptors counter to status.csv file
- Improve logging for ProcessAddFiles in DropDaemon
- Add option to specify timeout and retry intervals in drop config
- Reduce cache telemetry in BuildXL

# 0.1.0-20240223.2 (Release [365241](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=365241))
- Modified the build status line for IPC pips progress
- Add net8 support for NugetSpecGenerator
- Increase regex timeout in service pips
- Remove GitHashes cache miss analysis mode

# 0.1.0-20240216.4 (Release [363779](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=363779))
- Drop daemon is deployed as .NET6.
- PipOutputUpToDate message is demoted to diagnostic level.
- Improved retry logic for pips that failed in remote worker.
- Changed minimum wait for remote worker from 1 min to 3 mins.
- Removed stats.prf.json from Kusto telemetry.
- Removed gRPC telemetry from cache.
- Changed the error logging message displayed on ADO console for DX64 errors and DX65 warnings.
- Added scripts for provisioning kusto log for onboarding purposes.
- Changed job object logic for collecting surviving processes to accommodate undocumented Windows API behavior.
- Avoid creating process dumps for allowed surviving child processes.
- Improvement on symbol daemon indexer:
  - ignore absent files,
  - treat request for existing symbol as user error.
- Avoid logging full (big) message payload received by ApiServer.

# 0.1.0-20240202.1.1 (Release [361581](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=361581))
- BXL bits are now using Net 7
- Improved worker attachment logic
- Improved handling of dynamic workers

# 0.1.0-20240126.13.1 (Release [359923](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=359923))
- Add active TCP connections statistics to status.csv.
- Enable uploading cache logs to Kusto.
- Interpose __xmknod/__xmknodat in Linux Sandbox.
- Environment variable values are case sensitive when computing cache fingerprints.
- Include current fingerprint salt when calculating static fingerprint during graph construction from graph fragments.
- Fixes to VS solution generation in Linux.
- Fixes path canonicalization for near-root paths.
- Add ETW trace logging to Windows Detours sandbox.
- Use ADO environment variables to infer cache miss analysis keys based on branches related to the build.
- Log files accesses that happen before Linux sandbox initialization is complete.

# 0.1.0-20240121.1.1 (Release [358162](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=358162))
- Add args to override csc debugType option
- Address Detours message mismatch failures
- Use path remapper for nuget package exclusions
- Improve fire & forget MaterializeOutput feature
- Early-released dynamic workers don’t wait for orchestrator attachment
- Exit gracefully when a Hello call is refused from a dynamically-attaching worker

# 0.1.0-20240113.1 (Release [356195](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=356195))
- Improve the early termination with an internal error.
- Add Retry for download pips.
- Simplify GRPC options
- Launch ptrace sandbox when a binary has capabilities set.
- Update Rush and Nuget tests to use internal package feeds on internal builds. 
- Add additional profiling files to support API scan.

# 0.1.0-20240105.3 (Release [354406](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=354406))
- [Linux] Report full program path on sandbox init

# 0.1.0-20231229.2 (Release [352891](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=352891))
- [Linux] Report opendir​ in sandbox interpose.
- Add help text for PipProperties, and rename pip-specific PipFingerprintingSalt property to PipFingerprintSalt.
- Clean up default arguments from ADOBuildRunner.
- Fixed memory usage accounting of job object in detoured process.

# 0.1.0-20231208.1 (Release [348223](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=348223))
- Add a cache miss analysis mode that uses latest git commit hashes as fingerprint store key candidates.
- Make sure Linux sandbox tear down works properly even when we have missing accesses.
- Unify handling of default configurations.
- Bug fixes.

# 0.1.0-20231202.0 (Release [346718](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=346718))
-	Do not produce a pip dump if a DFA is allow listed.
-	Add a message counting semaphore for the Linux sandbox as a sanity check.
-	L3 cache provisioning scripts available.
-	Add a concurrent pip analyzer.
-	Bug fixes.

# 0.1.0-20231122.3 (Release [344123](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=344123))
- Opt-in to new msbuild logger behaviour.
- Clean up reference to BuildXL.Tracing in BuildXL.Native.
- Log the final configuration object used by bxl during a build.
- Add PublicAPI Analyzers to BXL and enforce the public API for hashing.
- Fix cancellation crash when HistoricMetadataCache is not initialized.

# 0.1.0-20231110.1 (Release [341167](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=341167))
- Update various dependencies
- Use internal feed for npm packages.
- Remove BuildXL.Tracing assembly reference from BuildXL.Processes.
- Stop scrubbing source sealed directories.
- Cache client reliability fix.
- Include pip outputs in cachedump analyzer.
- Improve incremental scheduling hit rate for BuildXL.Internal repo.
- Add ability to generate cache config for 1ES Hosted Pool distributed builds.
- Improve reliability for internal error build termination.
- Fix retry logic when worker runs out of disk space (already hotfixed to CloudBuild).
- Enable early worker release for 1ES Hosted Pool builds by default.

# 0.1.0-20231027.2 (Release [337912](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=337912))
- Allow BuildXL to use multiple Blob L3 Shards.
- Use a worker stage in the Linux PR distributed validation.
- Enabling journaling and publish build logs for dependency update pipeline.
- Deprecate CloudBuildV1 Build Manifest.
- Add Result Filter to PipExecutionPerformance Analyzer.
- Release read lock in ObjectCache only if it was acquired by the thread.

# 0.1.0-20231021.0.1 (Release [337230](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=337230))
-	Fix the dynamic worker's timeout issue when it is released early
-	Do not reduce the orchestrator's slot when there are a few remote workers
-	Quad align USN size to get the correct max size
-	Propagate PID to normalize path in Linux Sandbox
-	Make report processing more robust in Linux Sandbox
-	Ensure execute permissions bit is set for destination file for copy file pip scenarios

# 0.1.0-20231013.2 (Release [334693](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=334693))
-	Use Linux stateless pool
-	Do not declare yarn.lock as a static input in yarn SDK
-	Wait for finalize to finish when it's called during shutdown 
-	Deprecate /minAvailableRamMb flag
-	Render namespaces and identifiers taking into consideration escaping rules 
-	[Linux Sandbox] Avoid interference between primary and secondary access report consuming threads
-	Some bug fixes

# 0.1.0-20231006.2.1 (Release [333923](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=333923))
-	Migrated Windows detours to C++ 2.0
-	Enforce the same engine version when doing distributed builds
-	Expand frontiers of runtime cache miss analysis
-	Display executing processes from all workers in the build status
-	Improve early worker release logic by not releasing workers when there is a scheduler limiting resource

# 0.1.0-20230929.3.1 (Release [331981](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=331981))

- Add a new FileSystemMode that ignores dir enumerations during pip fingerprint computation 
- Expose unsafe arguments to the yarn install sdk
- Add EverConnectedWorkerCount metric
- Add BuildXLCancelLargestRamUseFirst option that makes engine cancel processes with largest mem usage instead of the most recently launched ones
- Add wasted duration due to the retries for the critical path
- Various fixes and improvements

# 0.1.0-20230921.3.1 (Release [330519](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=330519&_a=release-pipeline-progress))

- Keep traversing candidates when hitting an evicted content hash list
- Support dotnet msbuild in VBCSCompilerLogger
- Collect Git repo information for dev builds
- Add strong fingerprint info in cache hit log messages
- Log plugin process stderr if plugin start fails

# 0.1.0-20230915.6 (Release [327814](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=327814&_a=release-pipeline-progress))

- Make LocalDiskContentStore automatically creates the path casing cache when the honoring flag is enabled
- Update dotnet versions to latest
- Change Input Filter to consider sealed source directory with patterns
- Add argument to enable verbose process and sandbox logging for specific pips
- Include distributed build role in DominoInvocation telemetry
- Remove various low value telemetry events
- PipExecutionPerformance should record the PIDs as part of the processTree
- Remove various obsolete todos
- Removing old AD-based auth packages
- Use Microsoft.Artifacts.Authentication for authentication 

# 0.1.0-20230901.1 (Release [324492](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=324492&_a=release-pipeline-progress))
- Forward console messages from workers to orchestrator.
- Add option to honor directory path casing for dynamic outputs.
- Classify write-on-absent-path-probe as missing dependency in DFA summary.
- [Detours] Detect cycles when resolving chain of symlinks.
- Ensure tracee process has ptrace pemission before tracer is launched.

# 0.1.0-20230825.2.1 (Release [323902](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=323902))
- Not untrack user profile on Linux on the MsBuild pip constructor
- [Blob L3 GC] Periodically stop consuming new changes and create a new checkpoint
- Simplify Kusto logging authentication with managed identities.
- Build new interop binaries with xcode when running macos PR pipeline.
- Allow the blob-based cache to authenticate to a storage account using a managed identity.
- Various changes related to MacOS.

# 0.1.0-20230818.3 (Release [321099](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=321099))
- Ignore anonymous files on Linux Sandbox
- Fix Linux sandbox crash bugs
- Allow configuring additional verbose events for workers to forward to orchestrator
- Improvements to how BuildXL repo packages are created

# 0.1.0-20230810.2.1 (Release [320235](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=320235))
- Add self-recovery capabilities for the blob L3 cache
- Fix processor usage percentage for pips in linux
- Only publish release binaries for macos interop library
- Remove IcmClient from cache monitor
- Report back enumerations when running front end related process under a sandbox
- Kill active ptracerunners on SandboxedProcess.KillAsync

# 0.1.0-20230804.6  (Release [317902](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=317902))
- Cache related components are now shipped with multiple NuGet packages
- Added extra data to PXL related to cached pips 
- Report DFAs from past attempts on retry
- Perf improvements for generating binary logs

# 0.1.0-20230727.4.2 (Release [316902](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=316902))
- Improvements to address a potential pathset explosion
- Allow users to limit the number of unique path sets to check during cache lookup
- Reduce memory allocations in detours
- Various fixes and improvements

# 0.1.0-20230721.4 (Release [314745](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=314745))
- More robust execution log serialization.
- Kill pip if ptrace exits with an error code.
- Avoid unnecessary memory allocation during detours process injector initialization.
- Various other bug fixes

# 0.1.0-20230713.3 (Release [312875](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=312875&_a=release-pipeline-progress))
- Enable scoped build for the lage resolver
- Upgrade ADO and Artifact packages to 19.224
- BlobLifetimeManager in charge of L3 garbage collection
- Be platform agnostic when replacing newlines for Kusto logs
- Update critical path summary
- MSBuild frontend documentation
- Use ConcurrentStack implementation of ObjectPool on all platforms

# 0.1.0-20230706.3 (Release [311327](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=311327&_a=release-pipeline-progress))
- Make AdoBuildRunner work on job retries
- Limit the amount of open connections from the blob storage L3 cache

# 0.1.0-20230630.6.1 (Release [311085](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=311085&_a=release-pipeline-progress)), released on 7/6/2023
- Remove observed path set from execution result to reduce the overhead of data sent from workers to orchestrator
- Add customization capabilities for displaying pips to the end user
- Update QTest SDK to include BlameCollectorMode
- Ensure last access time is updated when doing pin on a blob-based content session
- Upgrade blob libraries to latest and use official DowndloadAsync for implementing TouchAsync
- Add the ability to selectively send logs to the console
- Introduce DScript Workflow SDK for easily writing build workflow in DScript

# 0.1.0-20230622.2 (Release [308283](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=308283&_a=release-pipeline-progress)), released on 6/28/2023
- Properly propagate failure when updating metadata.
- Add documentation for configuring a blob-based L3 cache
- Add more logging to cache eviction logic
- Interpose realpath and report all intermediate symlink resolutions
- Abstract away initialization of grpc core server.

# 0.1.0-20230617.0 (Release [307141](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=307141&_a=release-pipeline-progress)), released on 6/21/2023
- Add pin elision capabilities for the blob-based L3 cache
- Optionally add symbol indexing data to the BSI
- Include standard output in DX0016 timeout log messages
- Run DumpPipLite for DFA pips
- Implement ReportProcessArgs in the Linux sandbox
- Fail workers if pips have pending messages for particular pips on successful exits
- Change some configuration defaults on AdoBuildRunner

# 0.1.0-20230608.2 (Release [305070](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=305070&_a=release-pipeline-progress)), released on 6/14/2023
- Perf improvements for file content table loading
- Revert Microsoft.Artifacts.Authenticate package to address auth issues with interactive login
- Perf improvements in FileContentManager
- Address occasional IPC errors about failures binding to port for service pips
- Linux support for VsCode DScript extension
- Expose Symbol drop events to data stream to be rendered in CloudBuild UI

# 0.1.0-20230601.0 (Release [303270](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=303270&_a=release-pipeline-progress)), released on 6/7/2023
- Correctly report process creation ID for the PTrace sandbox
- Correctly retrieve the Linux filesystem type
- Fix EventHub connection with managed identity

# 0.1.0-20230526.0 (Release [301868](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=301868)), released on 6/1/2023
- Flip default for the includeMonikersInNuspecDependencies setting in NugetResolver 
- Intermediate symlinks are now resolved when deleting symlinks under directory symlinks
- Add the output file of component detection to produced drops
- Fix the deletion of rewritten sources on pip retry
- Fix calculation for graph shape limiting resource

# 0.1.0-20230518.3 (Release [299901](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=299901)), released on 5/24/2023
- Paginate results in GenerateBuildManifestFileList API
- Various fixes to the Guardian SDK
- Enable the ptrace sandbox by default on Linux

# 0.1.0-20230512.2 (Release [298428](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=298428)), released on 5/18/2023
- Allow safe source rewrites for QTest
- Log full output for pips in compliance builds
- Copy output files from policheck for compliance build
- Consider monikers in nuspec dependency target frameworks
- Various bug fixes

# 0.1.0-20230505.4 (Release [296804](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=296804)), released on 5/10/2023
- Complete missing USN change reasons
- Added missing parenthesis in journal debug info
- Make frontend less aggressive for expected use cases
- Fix selection of OS-specific FileSystemExtensions 
- Translate the result of DeviceIoControl (FSCTL_GET_REPARSE_POINT case) under an unsafe flag
- Track AppData in Linux

# 0.1.0-20230428.1 (Release [294986](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=294986)), released on 5/3/2023
- Update .net 7 to 7.0.5
- Debug info for USN journalling feature.
- Untrack Microsoft monitoring agent.
- Add an option to send logs to Kusto.
- Fix for SaveFileChangeTrackerAsync: NullRef bug.
- Use env when launching root process with ptrace.
- Set size on DebugEntry on Symbol API request.
- Untrack locallow folder in Guardian SDK.
- Address OverflowException in JumpConsistent Hashing.
- Report PID from ptracesandbox to report_access calls.
- Fix ProcessCachedWithAllowlistedFileMonitoringViolations test on Linux

# 0.1.0-20230424.4 (Release [293854](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=293854)), released on 4/26/2023
- Documentation update for pip environment variables
- Fix for crash when CloudBuild timeout is hit
- Documentation for weak fingerprint augmentation
- Fixes for nuget resolver
- Allow forcing PTrace linux sandbox for specific processes
- Reliability fixes for PTrace sandbox

# 0.1.0-20230414.2.1 (Release [292538](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=292538)), released on 4/19/2023
- Track source changes in ProgramData
- Update nugetcache fingerprint version
- Only avoid querying the remote cache (under /remoteCacheCutoff) if the remote is read-only
- Detect and warn when CacheDump fingerprint computation is not accurate
- Change the warning logging message displayed on ADO console for DX65 warnings
- Update net6, net7 and packageurl-dotnet version
- Fixed the missing dependency used by ComponentDetectionToSBOMPackageAdapter
- Various bug fixes

# 0.1.0-20230407.6 (Release [289924](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=289924)), released on 4/12/2023
- Track source changes in UserProfile and LocalAppData by default
- Embed sources into PDBs for internal builds
- Migrate Bond to Protobuf for cache serialization
- Bug fixes and perf improvements

# 0.1.0-20230331.2 (Release [288278](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=288278)), released on 4/5/2023
- Fix grouping commands for Lage resolver
- Change the log message on ADO console for DX64 errors
- Temporarily skip signing nuget packages
- Add an option to disable sending XLG events from workers
- Correct negative ResourcePaused count

# 0.1.0-20230324.3.2 (Release [287737](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=287737)), released on 3/30/2023
- Optimize allow list check for undeclared accesses
- Linux sandbox considers O_RDWR when marking access modes as writes
- Add push to outputs duration on the critical path
- Fix for Lage graph being too big for stdio
- Various other bug fixes.

# 0.1.0-20230317.0 (Release [284911](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=284911)), released on 3/22/2023
- [Linux sandbox] Propagate __BUILDXL_PTRACE_MQ_NAME through the process tree
- Spanify parsing of the Linux sandbox reports
- Decouple SandboxProcessPipExecutor out of Processes
- Track the time it takes to push outputs to the cache as part of perf info
- [Linux sandbox] Process pending reports from the FIFO even after the pip process tree has exited
- Capture observed inputs for failed & retried pips in the XLG
- Fix a crash caused by a race during build cancellation
- [Linux sandbox] Generate write accesses for destination on rewrite interposing
- Run ptrace sandbox as a daemon
- Add error handling to CreateFileStream() GraphAgnosticIncrementalSchedulingState.cs in to avoid crashing on failure during incremental scheduling.
- Handle pips that are too large to serialize to fingerprint store

# 0.1.0-20230310.5 (Release [283306](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=283306)), released on 3/15/2023
- Fix directory enumeration with legacy Win32 pattern *.*
- Merge ConcurrencyLimit and UnavailableSlots in build summary reporting
- Log specific user level error on VSTS cache startup failure.
- Remove grpc.core support for client
- Promoting credential scanner warnings to errors fix
- Enable dev logs across the board
- Pretty print result of Fingerprint store analyzer
- Allow machine total ram to dynamically increase for ram forecasting

# 0.1.0-20230303.6 (Release [281691](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=281691)), released on 3/8/2023
- Remove non-merge logic from RocksDb Databases.
- Fix PipsExecuting counter in Batmon.
- Clean up linux sandbox logs in release mode.
- Infer default output file for DumpProcess Analyzer.
- Add /ado argument to single machine builds.
- Ensure latest file path in consisten in Azureblobstoragecheckpointregistry.
- Break Instrumentation.Common dependency from BuildXl.Utilties.Core.
- Enable remote injection from 32-bit process by default.

# 0.1.0-20230227.0 (Release [280708](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=280708)), released on 3/1/2023
- Fix mistargeted directory symlinks in opaque directories when using preserved outputs
- Linux file monitoring sandbox reliability improvements
- Handle newlines in process command lines in windows sandbox
- NetCore security update
- Fix for some long DX0064 errors missing on CloudBuild UI
- Make environment variable CredScan violations verbose level until promoted to errors in a future release
- Address crash when failing to create Windows Error Reporting event

# 0.1.0-20230209.1 (Release [276369](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=276369)), released on 2/15/2023
- Allow BuildXL to run without enabling the change journal
- Added some missing interposed methods in the Linux sandbox
- Enable heartbeats by default
- Improvements on distributed builds handshaking

# 0.1.0-20221209.1.11 (Release [256361](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=252305)), released on 12/15/2022
- Reenable Ninja tests
- Deploy OS-specific RocksDB binaries
- Move from ActionBlock to ActionBlockSlim across the codebase
- Scan environment variables using the CredScan library 
- Properly invalidate file descriptor table invalidations on Linux clone/forks
- Fix capturing ADO requester info
- Increase ProcessDumper default depth to 20

# 0.1.0-20221202.8 (Release [252305](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=252305)), released on 12/7/2022
- Fixes to null reference exception on dynamic worker release.
- Fix dynamic workers' names in statsperf file.
- Fix null reference exception when computing sha256 during pip graph construction from MSBuild graph.
- Update Linux sandbox deployment to no longer use runtime package.
- Report whether a file access was a directory on the Linux sandbox.
- Various miscellaneous bug fixes.

# 0.1.0-20221125.2 (Release [249470](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=249470)), released on 11/30/2022
- Report statically linked processes on the Linux sandbox
- Enable generation of file access trace files on Linux
- Separate BuildManifest events from ExecutionLog events when sending from workers
- Improve error handling and cancellation for ActionBlockSlim
- Allow workers to actively join the distributed build
- Subst the BuildXL executable path when running under /runInSubst
- Add ChooseWorkerIpc to the list of ChooseWorker dispatchers
- Add timeout for output logging if Windows Terminal is the default terminal
- Use MSBuild locator to locate/load MSBuild assemblies when using MSBuild frontend
- Add bxl-adobuildrunner verb to npm deployment
- Various other improvements for Linux sandbox
- Various improvements and bug fixesArtifacts

# 0.1.0-20221104.7 (Release [241075](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=2410751&_a=release-pipeline-progress)), released on 11/9/2022
- Allow cache lookup pips to be reschedule on other machines when machine processing task is lost
- Various dependency security patches
- Reliability improvements for per-pip process dumps
- Don’t reuse server process when VSTS L3 cache client fails to authenticate
- Improve Linux developer documentation
- Various perf optimizations for highly cached distributed builds
- Ability to control injected FILE_SHARE_DELETE in detours based sandbox
- Disable LD_AUDIT events in linux sandbox to improve performance
- Symbol upload compatible with non-VS0 hash types
- Split npm package into Windows & Linux
- BuildXL uses Ubuntu built file access observation sandbox
- Do not set timestamps or scrub shared opaque directories in CloudBuild

# 0.1.0-20221020.0.1 (Release [236579](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=236579&_a=release-pipeline-progress)), released on 10/26/2022
- Improved documentation and examples for the Ninja resolver
- Fix CMake resolver invocation command line
- Add extra provenance when logging environment variables impacting the build
- Choose worker logic redesign

# 0.1.0-20221013.0 (Release [232249](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=232249&_a=release-pipeline-progress)), released on 10/19/2022
- Allow gRPC IPC server to receive arbitrarily large messages
- Introduce /assumeCleanOutputDirs to avoid scrubber for shared opaque dirs
- Expose running unsandboxed in yarn SDK

# 0.1.0-20221007.3 (Release [229981](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=229981&_a=release-pipeline-progress)), released on 10/12/2022
- Reuse weak identity for source files.
- Remove MaterializeInput step for IPC pips.
- Add yarn fast to Yarn SDK.
- Add support for Linux sandbox logging for JS resolvers.
- Various bug fixes.

# 0.1.0-20221003.19.1 (Release [228699](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=228699)), released on 10/6/2022
- Update Google.Protobuf to 3.19.5
- Remove directory deletion lock
- Do not emit stale location traces on master
- Various performance optimizations
- Add Linux support for capturing process dumps

# 0.1.0-20220923.4.1 (Release [226434](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=226434)), released on 9/28/2022
- Add support for trace files (sandbox observations) on Windows
- Avoid dynamic memory allocation in ReportProcessData
- Fix DFA due incremental tool enumeration
- Promote SBOM package parsing failures to errors
- Clean some object pools for server process
- Fix to handle crash during the failure of deserialization in FingerprintStore

# 0.1.0-20220916.6 (Release [221995](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=221995)), released on 9/21/2022
- Remove the OpenBond distribution layer.
- Add grpc option to enable gzip compression.
- Added ability to config chooseworkerlight and default to 100.
- Enabling various unit tests on Linux.
- Developer Guide for Linux.
- Introducing multiple container support and migrating off of azure pipeline.
- Add nuget resolver signing.
- Perf bash changes related to using pooled memory stream in PipTwoPhaseCache.
- Various bug fixes.

# 0.1.0-20220908.0 (Release [218898](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=218898)), released on 9/14/2022
- Various optimizations for bxl.exe memory footprint and performance
- Improvement for domioninvocation telemetry retention
- Fix for race condition in HierarchialNameTable that caused unnecessary cache misses
- SourceSealDirectory patterns added to DumpPip Analyzer
- Default IPC pip protocol to gRPC
- Expose nested process termination timeout to Javascript resolvers

# 0.1.0-20220831.2 (Release [215995](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=215995)), released on 9/7/2022
- Reduce storage operations and bandwidth utilization when uploading logs
- QTest on BuildXL: Allow additional QTest arguments to be passed in a rsp file
- Avoid warning for low worker count if we performed early releases
- Log a specific error when a file is unavailable to be stored to cache
- [Linux] BuildXL npm package updates
- Some bug fixes and memory optimizations

# 0.1.0-20220818.0.1 (Release [212415](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=212415)), released on 8/26/22
- Fix crash caused by pip cancellation
- Resolve reparse points when creating a process

# 0.1.0-20220811.1 (Release [208433](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=208433)), released on 8/18/2022
- Resolve reparse points when creating a process
- Change Ninja resolver settings to intervene CXX command lines into an environment setting
- Change BuildXL.Summary.md to make it easier to differentiate between different builds in ADO
- Various bug fixes

# 0.1.0-20220804.0 (Release [205184](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=205184)), released on 8/10/2022
- Add StageId property to captured build info.
- Redesign of IpcMoniker abstraction.
- Add support for PAT authentication with DropDaemon.
- Add performance optimized extension methods for IReadOnlyList.
- Various bug fixes.

# 0.1.0-20220728.5 (Release [202601](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=202601)), released on 8/3/2022
- Track source rewrites on Linux
- Expose allowed surviving processes in JS/MSBuild resolvers
- Some bug fixes 

# 0.1.0-20220721.4 (Release [199845](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=199845)), released on 7/27/2022
- Fix windows language pack file access violation for Javascript builds
- Enable additional unit & integration tests on Linux
- Misc bug fixes

# 0.1.0-20220711.4.2 (Release [196363](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=196363)), released on 7/13/2022
- Log unexpected exceptions in grpc server interceptor 
- Capture more info related to the organization triggering the build.
- Introduce temp folder shared by pips executed in VM
- Blob L3 - Download strategies and pin elision optimizations
- Fix empty drops crashing GenerateBuildManifestFileList
- IPC pips through gRPC
- Log service pip mem usage in stats.csv
- Eat connection reset unobserved task exceptions
- Update grpc.net to 2.47.0
- Decrease grpc communication overhead between worker and orchestrator

# 0.1.0-20220626.0 (Release [190089](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=190089)), released on 6/29/2022
- \[Linux Sandbox\] Stop wrapping processes in a bash script.
- Fail pips faster when a connection is lost with a worker.
- \[Linux Sandbox\] Implement a tiny bxl-env program to use instead of /usr/bin/env.
- Remove spansort extension.
- \[libDetours\] Don't crash if no FileAccessManifest is specified.
- Capture Infra property for Telemetry.
- Disable suspending service pips.
- \[BXL Remoting\]\[AnyBuild\] Enable VFS pre-rendering using hardlinks
- Add more diagnostic info to FindAllOpenHandlesInDirectory.

# 0.1.0-20220617.5.2 (Release [187820](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=187820)), released on 6/22/2022
- Fail pips faster when a connection is lost with a worker
- Fix underbuild due to dirty check
- Expand Peformance Summary Logging

# 0.1.0-20220603.4 (Release [181226](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=181226&_a=release-pipeline-progress)), released on 6/8/2022
- Reduce the number of events going to the XLG to optimize space
- Allow for interactive authentication in linux/mac local builds
- Use path based sorting to improve cache stability when generating dependency files
- Stop using dev cache when the chances of getting a remote cache hit are low
- Incremental scheduling improvements
- Bug fixes

# 0.1.0-20220527.6 (Release [178572](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=178572&_a=release-pipeline-progress)), released on 6/1/2022
- Stop materializing IPC pip outputs due to value pip dependencies
- NuGet resolver changes reapplied after some fixes
- Add support for AzureAuth
- Scrub stale packages by default on ADO builds
- Removed netcoreapp3.1 and net5.0 qualifiers
- Change maxProcMultiplier to 0.9 by default
- Some bug fixes and optimizations

# 0.1.0-202200523.0 (Release [176841](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=176841&_a=release-pipeline-progress)), released on 5/25/2022
- Register enumerated files with the output file system so that directory enumerations are properly detected
- Rehash dropped files when necessary
- Remove net462 support
- Sort telemetry tags by time taken
- Allow configuring servergc heap count and set it to 3 for bxlanalyzer
- Don't fail finalization call when no drops were created
- [Guardian] Fix policheck timeout issue and infrastructure error
- [Guardian] Add Policheck gdnsuppress

# 0.1.0-202200517.1 (Release [174531](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=174531&_a=release-pipeline-progress)), released on 5/19/2022
- Make ADO message limit configurable
- Udate grpc.core packages
- Performance improvements for RocksDB
- Ctrl-c related crash fix
- Various bug fixes

# 0.1.0-20220509.1.1 (Release [171893](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=171893&_a=release-pipeline-progress)), released on 5/11/2022
- Context.GetBuildEngineDirectory() returns a normalized engine path
- ExecutionLogSDK is now dotnet core only
- CredScan updated to 2.2.7.8
- Update Grpc.Net client to fix unobserved Task exceptions
- Various bug fixes

# 0.1.0-20220429.3 (Release [167793](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=167793&_a=release-pipeline-progress)), released on 5/6/2022
- Eagerly ensure minimum workers
- Early worker release for COSINE builds
- Various fixes and improvements

# 0.1.0-20220413.1 (Release [161474](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=161474&_a=release-pipeline-progress)), released on 4/20/2022
- Spanify StringTable GetExtension and RemoveExtension.
- Add Support for FlawFinder with Guardian.
- Add unsafe execute option to disable sandboxing at the pip level
- Make status update frequency more dynamic for Azure DevOps Listener.
- Various bug fixes.

# 0.1.0-20220331.4.2 (Release [159571](https://mseng.visualstudio.com/Domino/_releaseProgress?releaseId=159571)), released on 4/8/2022
- Avoid calling globR on ComplianceBuild
- Handle incorrect file name length for SetFileInfoByHandle
- Various fixes 

# 0.1.0-20220327.0 (Release [154760](https://mseng.visualstudio.com/Domino/_releaseProgress?releaseId=154760)), released on 3/30/2022
- Added support for using Grpc.NET with .net6
- Fix for cache client logger crash
- Emit target failed event for eventual display on CloudBuild UI
- Configuration cleanup
- Preserve state of statsprf.json file on build timeout
- Incremental scheduling performance improvements
- Improvements for process remoting server initialization

# 0.1.0-20220318.6.1 (Release [152886](https://mseng.visualstudio.com/Domino/_releaseProgress?releaseId=152886)), released on 3/23/2022
- Add some tolerance for late-joining workers 
- Enable module affinity
- Enable net6.0 by default
- Handle new line characters in detours
- Improve cache stability for directories created by pips
- Several bug fixes

# 0.1.0-20220311.6 (Release [148943](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=148934)), released on 3/16/2022
- Better descriptions for ninja pips
- Various fixes and improvements

# 0.1.0-20220306.0.2 (Release [148113](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=148113)), released on 3/9/2022
- Allow increasing batch limit for orchestrator->worker RPC when sending MaterializeOutput requests
- Support custom SBOM overrides through DropDaemon arguments
- Entries in RuntimeConfigFiles are sorted to avoid artificial cache misses
- Environment variable inputs to the graph are now compared in a case-sensitive way
- Various fixes and improvements

# 0.1.0-20220225.2 (Release [144135](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=144135)), released on 3/2/2022
- Updated BuildXL trace documentation
- Enable remoting cache tests
- BsiMetadataExtractor to take custom PackageVersion

# 0.1.0-20220218.5 (Release [141436](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=141436)), released on 2/24/2022
- Add milliseconds to CriticalPath table logging
- Globally skip logging specific warnings when CtrlC cancellation token is signaled
- Add option in cache dump analyzer to process multiple pips in single invocation
- Sort outputs to mitigate same-content concurrent pushes
- Add experimental CPU resource awareness option to scheduler
- Enable high pipe read retry count for Net6
- Disable embedded webview for interactive authentication
- Update Lage graph builder tool to accept a lage location parameter

# 0.1.0-20220211.6 (Release [138769](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=138769)), released on 2/16/2022
- Improve performance of storing outputs in cache
- Add remotely run processes to the console.
- Detours improvements
- Increase service pip handshake timeout Increase service pip handshake timeout
- Add WMI counters to monitor CPU congestion
- Log MaterializeOutputOverhang duration for MetaBuild.
- Remove HashFile pip from source nodes in CriticalPathAnalyzer
- Explicitly report directory probes in detours
- Update critical path calculation and priority assignment

# 0.1.0-20220204.4 (Release [136160](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=136160)), released on 2/10/2022
- Switch BuildXL.Tools.CredentialProvider to .NET6
- Limit rush tests concurrency
- Enable limiting resource stats for distributed builds
- Add configurable waiting time for remote worker attachment
- Include the drop name in the drop log filename
- Various bug and dependency fixes

# 0.1.0-20220128.1 (Release [133425](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=133425)), released on 2/2/2022
- Move sarif files under a single location
- Add counter for time spent replaying outputs on remote cache hit
- Handle cancellation for cache pin operation
- Migrate cache, drop, and symbols authentication to MSAL
- Publish .net6 packages for all platforms

# 0.1.0-20220114.1 (Release [128430](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=128430)), released on 1/19/2022
- Reduce high-volume logging for distributed pip requests
- Allow specifying search paths for JS coordinators in resolver configuration
- Handle Crtl+C cancellation when interacting with the cache
- Simplify subst usage for dev cache with new /runInSubst flag
- Various bug fixes

# 0.1.0-20220107.4.1 (Release [128274](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=128274)), released on 1/14/2022
- SPDX Improvements
- Various bug fixes

 # 0.1.0-20211203.5 (Release [123327](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=123327)), released on 12/8/2021
- Add SBOM packages to SBOM generation step in DropDaemon
- Light process pips are fully supported by scheduler
- Add .net6.0 support 
- Disambiguate AbsentPathProbe DFA logging 
- Allow RuntimeCacheMissAnalyzer until the build ends to potentially load
- Generate SBOMs by default and add the options to the Drop SDK
- Various bug fixes

 # 0.1.0-20211112.2 (Release [121971](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=121971)), released on 11/18/2021
- Address SBOM API scaling issues
- Mount table is properly populated for DScript VSCode extension
- Fix collisions during build manifest generation in multi-drop build
- Various bug fixes

# 0.1.0-20211105.0 (Release [121386](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=121386)), released on 11/10/2021
-  Telemetry is enabled by default for Microsoft internal developer builds
-  Update DSCript VSCode plugin to the latest VSCode infrastructure
-  Various bug fixes and perf improvements

# 0.1.0-20211029.3 (Release [120929](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=120929)), released on 11/3/2021
-  Improved cache miss analysis in dev cache builds
-  Allow fingerprint augmentation for QTest pips
-  Documentation updates
-  Various bug fixes

# 0.1.0-20211022.4 (Release [120385](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=120385&_a=release-pipeline-progress)), released on 10/27/2021
- New packed execution logging framework.
- Object cache to fingerprint store for optimized access of the store.
- Update ASPNetCore version to address security issue.
- Upgrade DotNet 3 runtime to 3.1.19 for selfhost.
- Differentiate between an individual DropConfig and global DropServiceConfig
- Use native method for setting ACL instead of executing takeown/icacls.

# 0.1.0-20211014.2.2 (Release [119987](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=119987&_a=release-pipeline-progress)), released on 10/20/2021
- Only consider stale shared opaques in minimal + alien enumeration mode when lazy scrubbing is enabled.
- Various fixes in DropDaemon service and SDK.
- Direct console output to parent when there is no stdout/stderr hook for UnsandboxedProcess.
- Improve compatibility with EnforceSomeTypeSanity linter policy.
- Add environment variable BuildXLGrpcVerbosityLevel to control the verbosity level of GRPC logging. 
- Use CloudBuild’s ESRP sign tool to sign BuildXL binaries.
- .NET 6 support for Nuget spec generator.
- Optimization: Replace blocking collection with concurrent queue for processing events in binary logger.
- Various bug fixes.


# 0.1.0-20211003.0.1 (Release [119237](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=119237)), released on 10/6/2021
- Multi-drop handling by a single daemon 
- Add various Materialization Daemon counters
- Add network configuration to Yarn SDK
- Fix taking file ownership and applying ACL

# 0.1.0-20210924.6.3 (Release [118701](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=118701)), released on 9/29/2021
- [Detours] Account for null terminator in GetFinalPathNameByHandle
- Reduce size of VSCode DScript package to allow auto-updating the package
- Add --ignore-optional option to yard install arguments
- Fix build manifest signing compatibility issue with some build queues
- Various bug fixes

# 0.1.0-20210917.2  (Release [117834](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=117843)), released on 9/22/2021
- Download resolver schedules proper pips
- Full reparse point resolution can be disabled at the pip level
- DScript support for expanding environment variables in paths and strings
- Improvements on the Ninja frontend in order to better deal with the environment
- Exclusive opaques can now be nested under shared opaques
- Various bug fixes

# 0.1.0-20210909.1  (Release [117108](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=117108)), released on 9/15/2021
- Add new option to force regenerate nuget package specs.
- Retry external VM processes on a different worker during retryable failures.

# 0.1.0-20210903.0  (Release [116707](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=116707)), released on 9/8/2021
- Support VS2022 to BuildXL VS extension
- Relax exclusive opaque violations to only consider actually produced files
- [QTest] Update NuGet package to incorporate Gradle fix
- [JavaScript] Deal with absent script resolution in the presence of project-to-project cycle
- [JavaScript] Add support for older Yarn workspace format
- Various bug fixes

# 0.1.0-20210826.4  (Release [116164](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=116164)), released on 9/1/2021
- [QTest] Add support for uploading test coverage for JS
- Various bugfixes

# 0.1.0-20210821.0  (Release [115264](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=115764)), released on 8/26/2021
- Enable useHistoricalCpuUsage by default in CloudBuild
- Fixes to worker early release handling to avoid failures in certain conditions
- Allow managing cluster state in distributed cache orchestrator
- Various bug fixes

# 0.1.0-20210813.0  (Release [115264](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=115264)), released on 8/18/2021
- Added Drive write counters for C Drive in all Windows OS
- Add ColdStorage using a FSCS
- Update Node.js version to v16.6.1
- Improve SandboxedProcess resource tracking for Unix
- [LinuxSandbox] Break symlink loops in resolve_path
- Adding a new dispatcher to choose a worker for light pips
- Various bug fixes

# 0.1.0-20210808.0  (Release [114717](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=114717)), released on 8/11/2021
- Light process pip improvements
- Support encryption and authentication for grpc.core
- Honor global passthroughs when building a Ninja pip's environment
- Fixes for Guardian support
- Limit the number of pip errors written into ADO summary file

# 0.1.0-20210730.1.1  (Release [114273](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=114273)), released on 8/4/2021
- [Guardian] Case insensitive directory name comparisons on Windows
- [IPC/Service pips] Keep track of assigned ports (to avoid collisions)
- [JavaScript] Use forwards slashes when passing paths to node.exe
- [Build Manifest] Additional checks and guards against expected exceptions
- [FileConsumptionAnalyzer] Include file hashes
- A few other scheduler fixes/optimizations

# 0.1.0-20210723.4  (Release [113559](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=113559)), released on 7/28/2021
- Address race condition around remote pip timeout
- Improvements for resource tracking for linux and macOS sandboxes
- BuildXL support for honoring externally configured build session timeout
- Support for building BuildXL repo with updated MSVC
- Fix bug that caused suboptimal distribution/parallelization of IPC pips
- Engine side changes to support encrypting RPC traffic in distributed builds

# 0.1.0-20210716.3.3  (Release [113341](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=113341)), released on 7/21/2021
- Performance improvements around logging.
- Fix failure when building BuildXL with missing VisualCppTools NuGet package
- Allow accessing mount information during config evaluation 
- Store the hash->buildManifestHash mapping in historic metadata cache
- Disable remote pip timeouts

# 0.1.0-20210709.1 (Release [112441](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=112441)), released on 7/14/2021
- [LinuxSandbox] Compile libDetours against glibc 2.17
- [Guardian] Add updates for compliance build
- [JS] Turn on full reparse point resolution whenever a JS resolver is present
- Dump Pip Lite - Add observed file accesses to post execution analyzer
- Support packed execution as analyzer in scheduler
- RocksDb Upgrade
- Minor Bug fixes and improvements

# 0.1.0-20210625.2.1 (Release [111646](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=111646)), released on 6/30/2021
- [Detours] Fix buggy trim functions used in substitute process execution.
- [JS] Enable extra dependencies to JavaScript projects.
- [Drop] Allow customization of drop paths for directory content.
- Handle non-existent safe rewrite.
- Add orchestrator-side pip timeout for pips scheduled in the remote workers.

# 0.1.0-20210618.5 (Release [110868](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=110868)), released on 6/23/2021
- InKernelFileCopy will no longer throw an exception when it is not supported by the OS.
- Print info about command-line length violation.
- Use scoped PAT for build cache.
- Workers message every two minutes even if there are no pip results to send.

# 0.1.0-20210611.5 (Release [110170](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=110170)), released on 6/16/2021
- Remap paths properly for reparse point paths to enforce
- Add a flag to disable source file verification
- Improve client-to-server connection monitoring
- Optimize file-based hashing
- Add enforceFullReparsePointsUnderPath argument
- Optimize searching for reparse points in a path

# 0.1.0-20210528.7 (Release [108880](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=108880)), released on 6/2/2021
- DumpStringTable Analyzer
- Deprecate IDistributionConfiguration.EnableSourceFileMaterialization
- Fix SandboxedProcessInfo.MaxCommandLineLength to reflect true OS-imposed limits
- Split Build Manifest Generation and Signing flags
- Bugfix: reparse point checking in Detours
- Bugfix: RemoteWorker xlg blob processing 
- Bugfix: avoid arithmetic overflow in ChooseQueueFastNextCount

# 0.1.0-20210521.7.1 (Release [108460](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=108460)), released on 5/26/2021
- Perf improvement on detours reparse point resolution logic
- Expanded capacity of StringTable to deal with overflows
- JavaScript SourceMap symbol support added to Symbol Daemon
- Enable source verification across workers
- Usability improvements for Guardian under BuildXL

# 0.1.0-20210507.6 (Release [106582](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=106582)), released on 5/12/2021
- [Dump pip Lite] Fixed minor issues in the output file
- [MS Guardian] Add DScript SDK and documentation
- Fixed an overflow in HistoricPerfDataTable
- Various fixes and improvements

# 0.1.0-20210430.2 (Release [105633](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=105633)), released on 5/6/2021
- [JavaScript] Allow configuring process exit/retry codes and max number of retries in JavaScript resolvers.
- Update the calculation of orchestrator's slots in the distributed builds.

# 0.1.0-20210423.8 (Release [104915](https://dev.azure.com/mseng/domino/_releaseProgress?_a=release-pipeline-progress&releaseId=104915)), released on 4/28/2021
- Rename “master” to “orchestrator” in distribution code.
- Fixes to path combination and normalization logic in Detours.
- Fix to delete present directories before materializing reparse points.
- Build manifest performance and file materialization race fixes.
- Add distribution testing.
- Update default process retries to 3 and honour global untracked scopes for JS builds.
- Various bug fixes to memory management.

# 0.1.0-20210416.5 (Release [104096](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=104096)), released on 4/21/2021
- Dump Pip Lite - Track observed file accesses for failed pips 
- Build Manifest: XLG event batching
- Track the size of the associated files in the drop
- Bug fixes for grpc communication layer.

# 0.1.0-20210409.1.1 (Release [103647](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=103647)), released on 4/15/2021
- Implement a file descriptor table in the Linux sandbox
- [AnyBuild] Ensure deterministic output of DLLs for native builders
- Improved logging in substitute shim, drop daemon and memory management
- [Drop] Support for long path names
- [AnyBuild + BuildXL] Various improvements
- Improvements in distributed builds
- Improvements in DumpPipLite and PipExecutionPerformance analyzers
- Added option to log all outputs of cached pips
- Added support for long file names in drop
- Minor adjustments for AnyBuild usage
- Various bug fixes

# 0.1.0-20210319.2 (Release [101111](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=101111)), released on 3/24/2021
- Fix reparse point cache invalidation logic
- Add azure artifacts credential helper support
- Ignore events sent to orchestrator when pip results marked as complete
- Invalidate reparse point creations on directories
- Fix MachineReimagesRule with empty stamp
- Other bug fixes, auth fixes and unit test improvements.

# 0.1.0-20210312.6 (Release [100149](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=100149)), released on 3/17/2021
- Improve early worker release logic

# 0.1.0-20210308.3 (Release [99402](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=99402)), released on 3/10/2021
- [JavaScript] Add Yarn/Rush/Npm install to JavaScript SDK
- [Build manifest] Generate and upload signed catalog file
- Configurable critical commit level
- Increase BuildXL API server concurrency to handle multiple daemons from service pips
- Various bug fixes

# 0.1.0-20210226.4.1 (Release [98790](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=98790)), released on 3/4/2021
- Fix allowed same content source rewrite on convergence.
- Warn on writes declared outside mounts. 
- [Frontend] Add a method for getting a subdir of a shared opaque
- [Build Manifest] Improve perf and logging, and add additional counter.
- Add Azure Artifacts Credential Provider
- Correct successfully attached workers count
- Fix a crash in DumpPip analyzer

# 0.1.0-20210205.3.1  (Release [96124](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=96124&_a=release-pipeline-progress)), released on 2/10/2021
- Added Dump pip lite analyzer
- Improved synchronization for detoured processes
- Better handling of connection timeouts on workers
- Various bug fixes and perf improvements


# 0.1.0-20210129.4 (Release [94335](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=94335)), released on 2/3/2021
- Added handler for XLG event for failed pips
- Improvements to Redis autoscaler
- Upgrade AsyncFixer to v1.5.1 and fix the new async cases
- QTest: Add LogUploadMode option and update nuget to change parser name
- Various bug fixes and documentation improvements

# 0.1.0-20210122.8 (Release [93209](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=93209)), released on 1/27/2021
- Add breakaway process option in JS resolvers
- [Detours] Fix symlink traversal for CreateFileW and NtCreateFile
- Add the ability for DScript modules to define mounts
- Various fixes and improvements

# 0.1.0-20210118.0 (Release [92598](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=92598)), released on 1/20/2021
- Process remoting via AnyBuild.
- Added JavaScript SDK
- Added Array.find ambient.
- Extend process timeout when a process get suspended.
- Various updates on documentations.
- Various bug fixes.

# 0.1.0-20210107.0 (Release [91421](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=91421)), released on 1/13/2021
- Added implicit DScript resolver to automatically reference build-in SDKs
- Added getdirectories DScript function to return all directories output by a pip
- Misc crash and bug fixes

# 0.1.0-20201211.3.1 (Release [89756](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=89756)), released on 12/16/2020
- Updated QTest nuget version
- Verifying VolatileSet before sending reconcile events
- Re-enable ChangingColumnFamilies KeyValueStoreTest
- Expanding the full framework compatible support in the NuGet resolver
- Made DropPipTracker track any service pip
- Detect connectivity issues between VM and Host (Improved VM retry logic)
- Some code clean-up. Removed some features that were unused for a long time.

# 0.1.0-20201204.5.1 (Release [88993](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=88993)), released on 12/9/2020
- Added succeed fast pips
- Better handling of frontend errors
- [QTest] Update QTest SDK to facilitate JavaScript integration
- Various fixes and improvements

# 0.1.0-20201125.2 (Release [87786](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=87786)), released on 12/2/2020
- File content table in server process
- Engine state becoming immutable.
- Exit-early-on-new-graph feature.
- Temp cleaner becomes best effort to avoid excessive retries.
- [QTest] QTestProcDump folder in QTest final outputs.
- [JS Frontend] Support for customizing scheduling to enable Qtest into JavaScript frontend.
- [LinuxSandbox] Report new process when libDetours.so is dynamically loaded

# 0.1.0-20201107.0 (Release [85798](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=85798)), released on 11/11/2020
- Don't delete parents of temp directories during scrubbing
- Enable GRPC keepalive by default
- Extra counters for Symbol Daemon
- Extra counters for file materialization
- Provide a knob to control "storing outputs to cache" concurrency
- Add "Build Session Info" file (bsi.json) to drop as part of build manifest
- Fix overflow exception during ChooseWorkerCpu
- Fix Detours resolution cache
- Make directory enumeration fingerprint more stable

# 0.1.0-20201030.5 (Release [84823](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=84823)), released on 11/4/2020
- [JavaScript] Support grouping script commands into a single pip
- Handle duplicate file registration with different content in drop
- Build manifest uploads from master to drop
- Extra telemetry
- Various bug fixes

# 0.1.0-20201023.7.4 (Release [84361](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=84361)), released on 10/28/2020
- Expose a DScript flag to control full reparse point resolving
- Detours Reparse Point Resolver Improvements
- Made directory fingerprint sensible to new undeclared directories for MinimalGraphWithAlienFiles
- Added support for untracked scopes/paths in output directories
- Pip executing in VM can access files using newly introduced fixed host name
- Add Kusto logging support to launcher and deployment service
- Split TotalMaterializedOutputsSize into two counters: TotalMaterializedOutputsSize and TotalMaterializedApiServerFilesSize
- Added limiting resource percentages to stats
- Detours: Implemented ZwSetFileInformationByHandle with FILE_DISPOSITION_INFO_EX
- Skip IPC pips when materializing outputs by default
- Minor bug fixes, flaky Unit Test fixes and some performance improvements

# 0.1.0-20201017.0 (Release [83009](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=83009)), released on 10/21/2020
- ModuleAffinity extra logging when used with earlyWorkerRelease
- Add a new machine CPU reporting and jobObject stats
- Untrack AppData and LocalAppData for QTest
- CancelSuspend mode to avoid slow thrashing
- Introduce FileAndParents supersede mode to address slowness in file change tracker
- [QTest support] Expose an option to let bxl communicate the retry attempt number to a pip via an env variable
- Various bug fixes

# 0.1.0-20201010.0.1 (Release [82325](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=82325)), released on 10/14/2020
- Support for specifying per process pip retry.
- Allow for specifying domain id in drop daemon.
- QTest: change default retry mode to full.
- Various bug fixes.

# 0.1.0-20201004.2.3 (Release [81534](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=81534)), released on 10/7/2020
- Allow creation junctions to non-existent targets.
- Some fixes on the managed reparse point resolver.
- SymbolDaemon - don't fail on empty SODs 
- Fix module affinity hangs.
- MaterializationDaemon - retry external parser on failure
- Ensure that a service is running before a finalization pip is called.
- Increase drop default timeout from 5m to 15m

# 0.1.0-20200927.0 (Release [80424](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=80424)), released on 9/30/2020
- Collecting machine counters became non-blocking for Scheduler
- Optionally disable IsObsolete check during Ast Conversion for perf reasons
- Expose preserve path casing as a DScript option
- QTest: Upgrade QTest package version to 20.9.22.220402
- QTest: Add untrackedPaths argument to QTest

# 0.1.0-20200918.3 (Release [79359](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=79359)), released on 9/23/2020
- Materialization daemon: Add support for materializing output directories
- Support for adding paths to graph file system
- Add the case for VisualBasic task in VBCSCompiler
- QTest: Fix DFA issue for target binaries and add retry mode
- Some polishing on the Lage frontend

# 0.1.0-20200914.5.2 (Release [78915](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=78915)), released on 9/16/2020
- Enable full reparse point resolving in Detours
- Materialization daemon prep work
- Add plugin support
- Add file based service lifetime manager
- Support caching pips producing junctions as outputs
- Various bug fixes

# 0.1.0-20200828.6.2 (Release [77557](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=77557)), released on 9/2/2020
- Scrub RestrictedTemp post build
- Scrubber does not traverse directory junctions/symlinks anymore
- Preserve path casing for all Unix systems inside of the observed path sets
- Add Lage javascript frontend
- Set shorter fingerprint store GC cancelation timer for short builds
- Add timeout for fingerprintstore operations
- Various bug fixes and optimizations

# 0.1.0-20200822.0 (Release [76190](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=76190)), released on 8/26/2020
- [QTest] Increate QTest timeout pip so BuildXL doesn’t cancel a test run before dbs.qtest.exe can
- Escaping for PipDescription in PipExecutionPerformanceAnalyzer
- Fix semistable hash in some log events
- Add ContentHasher counters
- Stop drop uploading temp files within shared opaque directories
- Misc changes to support chunk dedup
- Added timeouts for various cache operations
- More aggressive retries for pips running on Admin VM
- Logging for per-pip expected and actual disk usage

# 0.1.0-20200814.1 (Release [75164](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=75164)), released on 8/19/2020
- Retry processes that fail in VM
- Use existing artifacts on disk during file materializations in dev mode
- Safe source rewrite relaxation policy
- Report intermediate directory symlink resolved paths as probe/read

# 0.1.0-20200807.11.1 (Release [74593](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=74593)), released on 8/12/2020
- Fix blolbID mismatch due to incorrect chunkDedup hashes
- Enable manageMemoryMode.EmptyWorkingSet by default for CB 
- Remove pip description from distribution logs to reduce logging volume
- Disable enableEvaluationThrottling by default
- Add an env var to disable retry for detours-related failures
- Asynchronous FingerprintStore loading
- Fix retry crash with pip failing without logging an error
- [QTest] Add CorruptCoverageFileFixer package to QTest SDK files
- [JavaScript]Add support for untracking directories based on relative paths

# 0.1.0-20200801.0 (Release [73610](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=73610)), released on 8/5/2020
- Add symbols support for output directories 
- Add support for gRPC keepalive
- Fix KeyNotFoundException in SandboxedProcessPipExecutor

# 0.1.0-20200724.3.1 (Release [73072](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=73072)), released on 7/29/2020
- Fix passthrough user-profile environment variables when running in VM
- Fix several issues with fingerprint store lookups
- Fix double-write violations on temporary files under shared opaque directories
- Fix misclassified pip materialization failures
- Update to latest QTest

# 0.1.0-20200717.1 (Release [71912](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=71912)), released on 7/22/2020
- Perf improvements on junction resolution in detours
- Clean dynamic outputs on pip retry
- Update .NET Core runtime to 3.1.6
- Various bug fixes

# 0.1.0-20200711.0 (Release [71168](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=71168)), released on 7/15/2020
- Enable non-nullable reference types for Cache Library 
- Exclude ClientTelemetry.pdb from getting copied several times as a part of packages/final deployment.
- Add Chromium tracer for BuildXL
- Fix DistributedContentCopier logging
- Disable writing ClusterState to ContentLocationDatabase and make read-only on workers
- Add Redis autoscaling to the cache monitor
- Retry Proactive Copies (and clean-up)
- Add Yarn integration tests
- Embed debug information (pdb) into assemblies
- Checkpoint manager hardlink immutable files instead of copying
- Add a configuration option to make a pip fail when writing to standard error
- Fix under build related to directory enumerations and undeclared sources
- Some other bug fixes, documentation updates and test additions

# 0.1.0-20200703.4.1 (Release [70555](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=70555)), released on 7/8/2020
- A new Yarn frontend
- New counters for the size of materialized files
- Minimum value of MaxNumPipTelemetryBatches is now 0
- Saving fingerprintstore into the logs directory is now optional
- Process cancellation messages are now verbose
- Misc bug fixes

# 0.1.0-20200628.1 (Release [69479](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=69479)), released on 7/1/2020
- Improvements for FileConsumption analyzer to help optimize materialization
- DumpPip analyzer – filter by produced shared opaque directory
- Upgrade RocksDb to 6.10.2
- Rename file access whitelist to allowlist
- Take maximum of historical CPU usage and user-provided weight
- Reduce number of per-pip runtime telemetry events sent by default
- Update QTest to 20.6.233.220544
- Retry PipProcessStartFailed and PipTempDirectoryCleanup errors no another machine
- Misc bug fixes 

# 0.1.0-20200619.6.2 (Release [68625](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=68625)), released on 6/24/2020
- [Rush] Various fixes on Rush frontend.
- Retry Detours semaphore creation in VM
- Fix on Aria reporting limit.
- [Linux] Cache reads/writes in sandbox.
- [QTest] Upgrade QTest version to 20.6.12.220844 that includes fix for DFA in code coverage.

# 0.1.0-20200612.5.1 (Release [68123](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=68123)), released on 6/18/2020
- Refine the default value of disableDefaultSourceResolver to false.
- Clarify the cache saving message.
- Add the resolver kinds used by the build as part of the statistics we send to Kusto 
- Automatically add tags qualifierKey=qualifierValue for each process pip 
- Clean SOD outputs before retrying due to a retriable exit code 
- [Rush] Use redirected log directory for tool logs 
- Added telemetry tags to toppipsperformanceinfo
- Fixed Fields in graph fingerprints being ignored during fingerprint matching causing false matched

# 0.1.0-20200608.2 (Release [67013](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=67013)), released on 6/10/2020
- Add more data to the detouring status: Is64BitProcess
- Don't report surviving child processes when execution is canceled
- Upgrading CB.QTest that includes old version of vstest to avoid new DFAs
- Handle the case when a surviving child process crashes for linux-sandboxing

# 0.1.0-20200602.10.2 (Release [66472](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=66472)), released on 6/4/2020
- New JavaScript dependency fixer analyzer
- Composite shared opaque directories perf improvements
- More configuration options for Drop
- Report dynamic content of seal directory pips
- New feature: output directory exclusions
- Various Rush front end improvements 

# 0.1.0-20200517.1 (Release [64507](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=64507)), released on 5/20/2020
- Allow client to recreate directories
- Process Execution: implement chroot jail for *nix
- Updated QTest nuget package to 20.5.15.183816
- Introduce emptyWorkingSet feature as an alternative to cancellation
- Disable DScript default source resolver by default
- [Bug] Avoid ObjectDisposedException when the empty hash is pushed to the machine twice
- [Bug] Untrack CodeCoverage.pdb that cause DFA
- Track GVFS_projection file if configured
- [Rush] Allow configuring to preserve the pathset casing at the pip process level.
- Make composite Shared Opaque Directory content filters more flexible

# 0.1.0-20200506.4 (Release [63394](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=63394)), released on 5/13/2020
- Fix for DisallowedFileAccess under user profile when running pips under admin VM in CloudBuild
- [Windows] Not scrubbing directory symlinks anymore
- [macOS] Catalina adjustments
- Improvements to ADO listener
- Various bug fixes

# 0.1.0-20200501.3 (Release [62855](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=62855)), released on 5/6/2020
- Improved logging for memory AccessViolations
- Update linux runtime to Ubuntu 18.04
- Drop upload support for Rush resolver
- Incremental work towards supporting chunk level dedup in drop
- Allow pips to specify they are uncancelable
- Misc bug fixes

# 0.1.0-20200424.6 (Release [62176](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=62176)), released on 4/29/2020
- Nicer rendering for batched cache miss analysis result.
- Shared opaque filtering.
- Selfhost build in Linux.
- Use different server process for different hash type.
- [Mac] XPC communication for MacOS Detours and ES extension.

# 0.1.0-20200418.4 (Release [61546](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=61546)), released on 4/22/2020
- Deploy Rush builder tool in a way drop understands
- Add telemetry event with per-pip runtimes for top N pips
- Making external sandboxed process executor work on non-Windows 
- Do not retry some exceptions while uploading symbols 
- Fix shared opaque subdirectory DFA 
- Fix a bug to prevent new entries from getting added to HistoricPerfData 
- Include version in spec for unmanaged packages
- Update historic perf data format version


# 0.1.0-20200412.2.1 (Release [60874](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=60874)), released on 4/15/2020
- An option for less aggressive memory projection: /enableLessAggresiveMemoryProjection
- Various Rush frontend improvements and new features
- Do not publish empty fingerprint store 
- Allow double write policies to kick in when double write involves a dynamic dependency 
- Adding semaphore for pip execution in VM
- [QTest] Update Nuget Package to 20.04.06


# 0.1.0-20200403.2.1 (Release [60559](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=60559)), released on 4/8/2020
- Add BuildXL workingset usage to TotalAvailableRam at Execution phase 
- Hook up logging to execution analyzer for graph loading failure details
- Various Rush frontend improvements and new features
- Use dynamic commit memory size for pip cancellation
- [macOS] Interposing / Hybrid sandbox implementation 
- [symbols] Log collision messages as verbose rather than warning 
- Fix RuntimeCacheMissAnalyzer NagleQueue process hang problem
- Setup infrastructure to run CloudTests tests in our PR validation
- Enable support for fully seal directories to contain output directories
- Fix directory symlink scrubbing 
- Improve XUnit integration on generated solution for Resharper
- Add a logic to remove redirectedProfile directory when junction creation fails


# 0.1.0-20200315.0 (Release [58569](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=58569)), released on 3/18/2020
- Add support for nested shared opaque directories.
- Add Rush frontend.
- Optimizations for trusted file accesses.
- Improvements for memory estimation for cancelled process pips.
- Relax probing explicitly declared outputs that are also under an output directory.
- Various bug fixes and improvements.


# 0.1.0-20200315.0 (Release [58569](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=58569)), released on 3/18/2020
- Add support for nested shared opaque directories.
- Add Rush frontend.
- Optimizations for trusted file accesses.
- Improvements for memory estimation for cancelled process pips.
- Relax probing explicitly declared outputs that are also under an output directory.
- Various bug fixes and improvements.


# 0.1.0-20200306.7 (Release [57833](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=57833)), released on 3/11/2020
- Introduce EngineVersion feature to help management of breaking changes
- Check file existence or executable before reporting access in CreateProcess()
- Improve memory throttling and cancellation for dev builds
- [QTest] Update to 20.3.2.221403
- Progress towards Linux support for ContentStore layer
- Enable pip retry on another worker by default in CloudBuild
- Improve classification for failures that get retries on other workers
- Experiment for closing timing gap between cache lookup and execution to reduce redundant work across build sessions
- Fix CacheMiss bug around directory membership fingerprint missing


# 0.1.0-20200228.5 (Release [57057](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=57057)), released on 3/4/2020
- Added an ExecuteConvergedProcessDuration counter.
- Disable batch logging in cache miss analysis by default.
- Added full eviction sort which ensures content is sorted by distributed age.
- Added 'requiresMacOperatingSystem' to *IfSupported attributes.

# 0.1.0-20200221.3.1 (Release [56624](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=56624)), released on 2/26/2020
- Directory scrubber can follow directory symlinks
- A new dispatcher in scheduler for seal directory pips.
- [MacOS] Sandboxing using Endpoint Security API.


# 0.1.0-20200214.6 (Release [55661](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=55661)), released on 2/19/2020
- Stop running in unsafe_IgnorePreloadedDlls mode by default
- Convert to user-error: CTMIS due to 'access denied' errors
- Log available physical memory at the beginning of the build
- [PipInputAnalyzer] Compute files at most common intersection of failed pips to check whether a bunch of failures are attributable to the same intermediate file
- [QTest] Fix -testMethod problem
- [QTest] Add environment variable for dotnetcore path in Cloudbuild

# 0.1.0-20200207.4.1 (Release [55353](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=55353)), released on 2/13/2020
- Detours plugin for substitute process execution
- Plumb ctrl-c through to PlaceFilesAsync
- Add unsafe options detail to cache miss analysis result
- Add batch cache miss analysis kusto query 
- Track paths used for weak fingerprint augmentation
- Add new Dscript Api to Path: Path.createFromAbsolutePathString

# 0.1.0-20200131.5.1 (Release [54373](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=54373)), released on 2/5/2020
- Re-enable `-TestMethod` and `-TestClass` in BuildXL selfhost
- Make MSBuild resolver more robust
- Runtime cache miss analysis fixes
- Make minimumDiskSpaceForPipsGb feature working with retryOnAnotherWorker feature
- Fix a crash when processing tool output streams
- Retry failed pip on different worker
- [VsCode] Implement CodeLens for module references
- [QTest] Enable QTest for all module cache test assemblies
- [QTest] Plumb 'acquireSemaphores' into QTest
- [QTest] Enable code coverage

# 0.1.0-20200124.5 (Release [53523](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=53523)), released on 1/29/2020
- Add session ID and session related ID to fingerprint store entry
- Speed up write operations in fingerprint store
- Support running crossgen on produced netcore binaries
- DScript managed SDK improvements
- Various bug fixes

# 0.1.0-20200119.0 (Release [53036](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=53036)), released on 1/22/2020
- CriticalPath analyzer improvements
- Improvements to handle larger builds (office multiple architectures)
- [QTest] Add QTestMock support
- [QTest] Add dotnetcore support
- Cache diagnostic improvements  
- [Mac] Fix handle leak
- [Hybrid engine] Add support for latest msbuild version
- [IDE] Add cross platform solution generation support
- PipGraph api now available in BuildXL.Pips.dll rather than BuildXL.Scheduler.dll
- Various bugfixes


# 0.1.0-20200103.10.2 (Release [51927](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=49574)), released on 1/8/2020
- Server GC re-enabled for macOS platform
- Std err/out files get copied to the log directory upon pip failure
- Improvements for runtime cache miss analysis accuracy and presentation
- Externally configurable hash algorithm for ContentStore
- Lazy shared opaque directory output deletion
- Add childProcessToBreakawayFromSandbox option to allow pips to breakaway untracked and outlive process tree
- Highlight all lines of multiline errors/warnings in Azure DevOps
- Fix broken JSON format in cache miss analysis
- StringTable scale improvements for large strings
- Display more accurate related pip information for shared opaque Disallowed File Accesses
- Fix under aggressive memory resource throttling
- Stopped producing the last net451 cache binaries in nuget packages
- Stopped producing the net472 nuget package of BuildXL and the net472 version in drop
- Misc bug fixes

# 0.1.0-20191208.3 (Release [49574](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=49574)), released on 12/11/2019
- ADO Listener respects /nowarn and other warning mappings.
- Use cheaper hash algorithm for server deployment.
- Add more parallelism in (and make async) Fingerprint store event processing.
- Expose fingerprint computation data and shared opaque outputs.
- Ensure that different repositories get different app servers.
- DScript: detect cycles in ValueCache factories.
- Fix a race condition when flushing execution log blobs at the end of a build.
- Remove duplicates from ObservedAccessedFileNames

# 0.1.0-20191127.2 (Release [49076](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=49076)), released on 12/4/2019
- Capture average phase working set in stats
- Log duration of different phases during process execution
- Improve cached graph log readability
- RocksDB tuning for macOS
- Report number of physically running processes to status.csv
- Cache pip description in various places
- Enable earlyWorkerRelease by default
- Add /fireForgetMaterializeOutput feature
- Update ObservedInputSummaryAnalyzer to work consider ExistingFileProbe
- Various logging improvements

# 0.1.0-20191117.0 (Release [47500](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=47500&_a=release-pipeline-progress)), released on 11/20/2019
- Add vstestSettingsFileForCoverage in QTest SDK for code coverage setting file
- Support existing file probe in incremental scheduling
- Keep track of commit memory as a resource 
- Add support for shared compilation for MSBuild scheduled pips
- Improved handling of weak fingerprint augmentation


# 0.1.0-20191107.6 (Release [46433](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=46433&_a=release-pipeline-progress)), released on 11/13/2019
- Proper handling of exceptions when reading sideband files.
- Support for dotnetcore 3.0 for QTest.
- Use POSIX delete for CASAAS.
- Memory cleanup for DSCript evaluation.
- Using VM pressure events to decide on pip cancellation.
- Nuget credential provider improvements.


# 0.1.0-20191102.0.2 (Release [45962](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=45962&_a=release-pipeline-progress)), released on 11/6/2019
- Improvements in memory-based scheduler throttling.
- Make the ProcessRunScriptAnalyzer x-plat compatible.
- [macOS] Low memory resource throttling improvements.
- Include GlobalDependencies in DumpPip analyzer.
- Fixes/improvements in Xlg Debugger.
- Prevent crashes caused by FP store logging.
- New default arguments for CloudBuild.


# 0.1.0-20191025.2.1 (Release [45157](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=45157&_a=release-pipeline-progress)), released on 10/30/2019
- Fix crashes in process dumper, and only dump processes under the same username
- Fix access control check with scrubbing in net core builds.
- Extend waiting period when creating crash dumps.
- Remove /viewer command line argument temporarily until viewer is fixed.
- Reduced memory footprint of the sandbox kext.
- More robust marking of shared opaque outputs.
- Re-include pdb files in the released binary.
- Update documentation of several features.

# 0.1.0-20191019.0.2 (Release [44428](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=44428)), released on 10/23/2019
- Journal for shared opaque outputs.
- Add preserveoutput trust level to process serializer and pip dump.
- PipGraphFragmentGenerator: tool for generating pip graph fragment from specs
- Fix access control check with scrubbing in NetCore builds
- Fix the rendering of pip arguments for cache purposes
- [macOS] Allow VM max memory pressure level to be configurable

# 0.1.0-20191011.9.1 (Release [43540](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=43540)), released on 10/16/2019
- Support for partial preserve outputs
- Symbol daemon runner is configured for CloudBuild environment
- New QTest version (19.10.5.1051)
- Fix IPC pip ordering for BinaryGraph
- CBdependencies feature an opt-out instead of opt-in
- Improve time to first pip for distributed builds
- Enforce weak fingerprint augmentation for MSBuild-scheduled pips
- Use xattrs to mark shared opaque outputs on Mac

# 0.1.0-20191003.4 (Release [42310](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=42310)), released on 10/9/2019
- Removed old Microsoft.ContentStoreApp.exe from deployment (replaced with ContentStoreApp.exe)
- Proper SIGILL handling on macOS
- Optimizations for Office Mularchy builds
- Improvements to error messages in ADO
- Custom pip description in ADO
- Added -vsNew switch to bxl.ps1
- Catch obscure file writes on macOS (via the vnode_write listener)
- Build BuildXL VSCode extension with BuildXL
- Make QTest SDK public

# 0.1.0-20190925.15.6 (Release [42115](https://mseng.visualstudio.com/Domino/_releaseProgress?releaseId=42115&_a=release-pipeline-progress)), released on 10/2/2019
- Fix source files missing in drop
- Component Governance for NuGet Packages
- Various bug fixes and stability improvements

# 0.1.0-20190919.1.4  (Release [41538](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=41538&_a=release-pipeline-progress)), released on 9/26/2019
- Resource based cancelling with shared opaque output producers.
- Less data to Kusto telemetry.
- Reduce the amount of tracing produced by BuildXL’s cache client.

# 0.1.0-20190913.9  (Release [40498](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=40498)), released on 9/19/2019
- Use proper pip data support for directory id.
- Augment (strengthen) weak fingerprint with common paths from observed path sets when a certain threshold of path sets is reached.
- Rename DominoInvocation and ExtraEventDataReported Events.
- Timeout proactive copies for Caching.
- Fix Drop Associate method (missing files mismatch).
- Add user facing documentation for XLDB.
- Fix front end throttling arguments.

# 0.1.0-20190906.8.2  (Release [40211](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=40211)), released on 9/11/2019
- Optimization for elision of AbsentPathProbes under shared opaque directories
- Allow error regex to apply to multiple lines of tool output
- Improve caching when using GlobalUnsafePassthroughEnvironmentVariables
- Update to QTest 19.9.6.1149. Expose qTestExcludeCcTargetsFile to exclude files from code coverage computation 
- Various bug fixes and improvements

# 0.1.0-20190830.7 (Release [39177](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=39177)), released on 9/5/2019
- Ignore directory probes for produced files in shared opaques
- Add more information to Disallowed File Access summary messages
- Add support for parallel graph loading [BinaryGraph]
- Upgrade to a newer version of qtest
- Various bug fixes and improvements


# 0.1.0-20190823.1.1 (Release [38769](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=38769)), released on 8/28/2019
- Add CODE_OF_CONDUCT.md and SECURITY.md documents
- Support for AzureDevOps formatting, summary page, etc.
- Remove Bond RCP
- Remove old visualization model
- Various VSCode extension improvements (in preparation for adding it to the marketplace)
- Various macOS Catalina improvements 
- Various XLG++ improvements
- Update to newest version of QTest


# 0.1.0-20190816.9 (Release [37763](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=37763)), released on 8/21/2019
- Add support for Azure devops Optimized output
- Binary graph fragments for BuildXL
- Introduce external input change list to BuildXL
- Stop the build after the first materialization error
- Make the VsCode plugin publishable to the VisualStudio marketplace
- macOS customers shouldn’t use this release due to a regression


# 0.1.0-20190809.3.4 (Release [37480](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=37480)), released on 8/15/2019
- New macOS sandbox based on EndpointSecurity APIs
- Introduce incrementalTool option to support gradle 
- Add BuildXLRuntimeCacheMissAllPips environment variable
- Various bug fixes and improvements


# 0.1.0-20190803.1 (Release [36404](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=36404)), released on 8/7/2019
- Reclassify some drop errors as user or infrastructure errors
- CloudBuild paths are no longer hardcoded in BuildXL repo, but controlled externally by CloudBuild,
- Improve colorization of DScript by default in VsCode
- Session Guid now encodes some extra information for easier correlation
- Some micro perf optimizations
- Various bugfixes

# 0.1.0-20190729.0 (Release [35768](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=35768)), released on 7/31/2019
- **[BREAKING CHANGE]** Statistic and BulkStatistic tables will stopped being populated in Aria. FinalStatistics will still be populated
- **[BREAKING CHANGE]** /unsafe_IgnoreUndeclaredAccessUnderSharedOpaques will no longer be enabled by default
- File access monitoring fix for probing a synlink chaing without the reparse point flag
- Progress on the macOS Catalina Endpoint Security based sandbox
- Distributed build reliability improvements
- [QTest] Updated to 19.7.18.221046
- Misc bug fixes
- We no longer publish net461 assemblies in our nuget packages
- The VsCode language server now runs on netcoreapp3.0 rather than net472

# 0.1.0-20190720.3.1 (Release [35267](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=35267)), released on 7/24/2019
- Don't delete contents of preserved opaque directories on cache replay.
- VstsCache with CoreCLR bits on Windows.
- Fix overflow issue in FIleContentInfo.Existence.
- Untrack Detours internal files during pip execution.
- Increase telemetry flush timeout in CloudBuild.
- Add a warning when execution analyzer inputs are incomplete.
- Distributed build bug fixes.

# 0.1.0-20190707.2 (Release [33603](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=33603)), released on 7/10/2019
- Fix errors getting into custom logs. 
- Improve error handling on master when connection is lost with worker.
- Force server redeployment on pipe exceptions.
- Report violations under shared opaques for writes in existing files.

# 0.1.0-20190630.0 (Release [32989](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=32989)), released on 7/3/2019
- Added remote telemetry for ContentStoreApp.
- Demoted file open failure message to diagnostics.
- Fix hole in file access monitoring for shared opaques. Add /unsafe_IgnoreUndeclaredAccessesUnderSharedOpaques- to receive new behavior. Expect us to reach out to you to coordinate migration if you are a shared opaques user.
- Don't reuse files for incremental checkpointing between epoch changes.
- Updated VS Extension to support AsyncPackage and background autoload.
- Handle long paths in directory creation in BuildXL's cache layer.
- Reduction of Cache tracing in Kusto.
- New copy file analyzer that prints out a list of all copy file pips.
- Spec generator changes to always include dependency closure for full framework packages and allow for compatibility between 4.6.1+ full framework packages and .NETStandard only specs.

# 0.1.0-20190622.1 (Release [32218](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=32218)), released on 6/26/2019
- Improve Ctrl+C experience
- Increase default number of retries for IPC connections
- Don't force full framework for detours tests
- Pool GRPC buffers
- Fix unix time and risk coefficient issues in distributed evictibility metric
- QTest: Add Recycle Bin to untracked directories
- [DScript] Improve Grpc/Protobuf codegen api
- Add an analyzer for analyzing required vs optional dependencies
- Reenable opaque directory upload from our deployment to drop
- Enable additional DScript interpreter test for macOS
- [macOS] Make signal handling resilient to asynchronous calls
- Add better support for opaque directories in DependencyAnalyzer 

# 0.1.0-20190615.0 (Release [31601](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=31601)), released on 6/19/2019
- New arguments- unsafe_GlobalPassthroughEnvVars and unsafe_GlobalUntrackedScopes
- Add a way to specify passthrough environment variables for the MSBuild resolver
- Reduce noise in cachemiss.log (fixed the ordering of elements in a fingerprint)
- All BuildXL executables are now marked 64-bit
- Various perf improvements
- Various bug fixes

# 0.1.0-20190607.4 (Release [30942](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=30942)), released on 6/12/2019
- When watson crash handling reports 0xDEAD BuildXl will retry the pip.
- Various perf improvements
- Various bug fixes

# 0.1.0-20190531.4.3 (Release [30629](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=30629)), released on 6/5/2019
- Update QTest to 19.5.29.221321
- Redirect temp directories to be local when run in VM in CLoudBuild
- Csv option for PipExecutionPerformanceAnalyzer
- Improvements for cache logging
- Misc bug fixes

# 0.1.0-20190525.4.1 (Release [29991](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=29991)), released on 5/29/2019
- Switched BuildXL public LKG to be the DotNetCore one.
- Report the existence of outputs on workers.
- Enable preserve outputs mode for dynamic outputs.
- Fix crash when calling a worker with a disposed connection.
- A copy file pip referencing a non-existent source file is now a user error.
- [ContentStore] Avoid non-terminating process in quota keeper.
- Mac kext is now notarized.

# 0.1.0-20190518.0 (Release [29134](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=29134)), released on 5/22/2019
- Allowed injection of shim process in lieu of all or some child processes.
- Added ability to launch pips in VM.
- Updated Google.Protobuf to 3.7
- [Mac] Support for Apple kext notarization/staple.
- [Helium] Added ability to disable WCI and pipe in BindFlt exceptions.
- [Combined Engine] Added intermediate output path predictor.
- [QTest] Added qTestRuntimeDependencies for explicit specification of extra run-time dependencies.

# 0.1.0-20190510.8 (Release [28504](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=28504&_a=release-pipeline-progress)), released on 5/15/2019
- Increase WorkerAttachTimeout to 45min from 30min
- Fix long path with SetFileAccessControl and HasWritableAccessControl operations
- Adding qTestContextInfo to upload qtest results to VSTS
- [macOS] Enable distributed copies on BuildXL builds on macOS 
- [macOS] CoreRT native compilation for select projects targeting osx
- [macOS] Use 'dependsOnCurrentHostOSDirectories' for tool definitions 

# 0.1.0-20190503.9 (Release [27992](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=27992&_a=release-pipeline-progress)), released on 5/8/2019
- Add support for pip priority field and schedule according to pip priority as well as historic data.
- ExecutionLog events are now processed asynchronously on master
- Avoid using and killing VBCSCompiler.exe
- [MacSandbox] skip unnecessarily creating trie nodes on lookups
- [macOS] Use different BundleIDs for debug and release kexts
- Add double write policy to allow same content double writes
- Fix underbuild in incremental scheduling caused by disappearing output directory 
- Fix for BuildXL hangs in temporary cleaner 
- [macOS] Handle 'rename directory' operation on macOS
- Update Microsft.Net.Compilers(Roslyn) to 3.0.0 to enable C#/CSharp 8 features
- [macOS] Fix pip materialization issues on macOS

# 0.1.0-20190426.9 (Release [27516](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=27516)), released on 5/1/2019
- Fix EventCount telemetry for macOS
- Switch cache miss analysis to json diff  (new output format)
- Add lazy directory materialization for IPC pips
- Use net472 for the drop binaries in the Sdk folder
- Stop producing net461 nuget package
- Use historic cpu usage info as weight during scheduling
- Various bugfixes

# 0.1.0-20190419.5 (Release [26949](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=26949)), released on 4/24/2019
- Add support for default untracking for MacOs from DScript to match windows
- Support to parse and merge in additional config files
- MsBuild supports multi qualifeir builds
- Update QTest version
- Make helium sandbox tombstone aware
- Grpc reliability improvements
- Handle some new RS6 filesystem behavior changes
- Various bugfixes

# 0.1.0-20190412.7 (Release [26501](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=26501)), released on 4/17/2019
- Scrub stray files in QTest deployment directories
- Default mounts added for Mac
- Switch expression added to DScript
- Additional info added for composite opaques in DumpPip analyzer
- Some improvements in DScript SDK Transformer APIs
- Update DScript plugin to work with the latest VSCode
- Better support for GRPC mode
- Better exception handling on hardlink creation
- Handling of absent hash files
- Add process ‘weight’ to control scheduling parallel processes
- Various bug fixes

# 0.1.0-20190329.14.1 (Release [25731](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=25731&_a=release-pipeline-progress)), released on 4/4/2019
 - BuildXL now ships as a net472 instead of net461
 - Some initial long path support
 - Stop tracing TaskCanceledException as errors in cache
 - Fix drop failing to get a producer of a file
 - Fix reported CPU usage on macOS
 - Remove named pipes
 - Attempt to fix failures to delete files before requesting cache materialize
 - Allow absent path probes of temp files under opaque directories if a pip depends on them
 - Various fixes

# 0.1.0-20190324.0 (Release [24943](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=24943)), released on 3/27/2019
 - Net472 bits are ready-to-use. 
 - Delete extra files in fully sealed directories (if scrub flag is set).
 - Change OutputGraph file system to include dynamic outputs.
 - Allow default mounts to be respecified.
 - Various bug fixes.

# 0.1.0-20190316.0 (Release [24373](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=24373)), released on 3/20/2019
 - Rename BuildXLScript to DScript.
 - BuildXL selfhost on Mac.
 - DScript SDK for managed code contains cross-plat resgen.
 - DScript has Array.sort method.
 - Various bug fixes.

# 0.1.0-20190309.0.3 (Release [23983](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=23983)), released on 3/13/2019
 - Fixes for VsCode extensions
 - Fixes to FileImpactAnalyzer and PipExecutionPerformanceAnalyzer
 - Support for adding all kinds of artifacts to drop
 - CMake resolver for Ninja builds
 - Memory guardrails for p-invoke calls on Mac
 - Nuget packages for CloudBuild, VSO and cache now contain net472 versions

# 0.1.0-20190302.2 (Release [23179](https://dev.azure.com/mseng/Domino/_release?releaseId=23179&_a=release-summary)), released on 3/6/2019
- Improved graph cache hits when environment variables or mount accesses are removed
- Improved runtime cache miss analysis on strong fingerprint misses
- Update from NetCore2.2.0-preview3 to NetCore2.2.0
- Graceful handling for ctrl+c cancellation when cache is still being initialized
- Update QTest version to support qtestVstsContext flag

# 0.1.0-20190224.0 (Release [22568](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=22568)), released on 2/27/2019
- New Download resolver
- Fixed drop error when calling addFilesToDrop with empty list
- Fixed runtime cache miss analysis crashing a build
- Various bug fixes

# 0.1.0-20190215.7 (Release [22052](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=22052)), released on 2/20/2019
- Net472 drop and BuildXL.net472 package are now produced to improve long path support on windows
- SandboxExec tool is now a standalone package
- Improve KEXT installation process
- Various bug fixes

# 0.1.0-20190208.9.1 (Release [21776](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=21776)), released on 2/13/2019
- macOS KEXT performance improvements
- Detour hardlink creation via SetInformationFile(FileLinkInformationEx)
- Build runtime cache miss analysis improvements
- Fixed a crash around RocksDB
- Move telemetry tag statistics to a separate "PipCounters" kusto table and fix bug with tags containing underscores

# 0.1.0-20190202.3 (Release [21316](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=21316)), released on 2/6/2019
- Domino has been renamed to BuildXL
- Misc crash and bug fixes

# 0.20190119.2.2 (Release [20895](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=20895)), released on 1/24/2019
- Fix underbuild caused by directory junctions
- Fix IPC hanging issue
- PipFingerprintAnalyzer for dumping pip fingerprint inputs from FingerprintStore
- Perf improvements in the graph filesystem
- Made killing server process more robust
- Various OSS Preparation workitems
- Updated BuildXL Icon
- Optimized caching access reports for Mac
- Improved resiliency when operating with Redis
- Improved eviction tracing
- Add AddIf function to BuildXL
- Improved shared opaque reliability for distributed builds
- Various bug fixes

# 0.20181207.2.1 (Release [19532](https://dev.azure.com/mseng/domino/_releasereleaseId=19532&_a=release-pipeline-progress)), released on 12/12/2018
- Perf and logging improvements for macOS
- Better handling for ERROR_CANT_ACCESS_FILE result returned by CreateFile()
- Improvements for .net standard consumers of repo artifacts
- Crash fixes

# 0.20181130.7.1 (Release [19155](https://dev.azure.com/mseng/domino/_releasereleaseId=19155&_a=release-pipeline-progress)), released on 12/5/2018
- DominoScript's deployment Sdk has improved diagnostics for double deployment errors. The diagnostic now mentions the assemblies target framework as well.
- Add netCoreApp support to our NuGet package generation
- Cache tracing improvements
- Introduce Copy-on-Write for BuildXL Mac
- Keep RunInSubst.exe alive until child processes exit & reduce the wait time on substed drive from 30 to 5 seconds

# 0.20181124.1.0 (Release [18756](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=18756)), released on 11/28/2018
- Introducing File Content Table to BuildXL Mac
- Renaming the macOS sandbox to BuildXLSandbox
- Perf improvements for highly cached builds in CloudBuild
- Fix graph deserialization issue for the builds that enable on-the-fly cache miss
- Core dump creation support for abnormal process exits (macOS)
- Move CloudStoreSDk closer to DominoSdk

# 0.20181110.3.0  (Release [18159](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=18159)), released on 11/14/2018
- Adding option in DScript to glob folders recursively
- SharedOpaque distributed build fix
- Add static predictions to the MsBuild resolver
- Add telemetry tag in QTest SDK
- MacOS: perf fixes
- MacOS: control spotlight indexing of artifact folders

# 0.20181102.2.0  (Release [17787](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=17787)), released on 11/7/2018
- MacOS: Dynamic sandbox child process timeout & improved sandbox telemetry
- MacOS: Dedupe Kext file access reports before sending to BuildXL client
- Misc. bug fixes

# 0.20181029.4.0 (Release [17574](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=17574&view=mine)), released on 10/31/2018
- Don’t use POSIX delete on Windows to avoid hangs
- Fix deadlock on pass-through file system
- Handle files with multiple hard links in MacOS sandbox
- Identify writes after absent file probes violations by default
- Properly handle non-existent mount for graph reuse
- Experimental MsBuild resolver based on MsBuild build graph construction API

# 0.20181021.2.0 (Release [17290](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=17290)), released on 10/24/2018
- Fix false absent file probes under opaque directories with lazy materialization
- Fix directory enumeration filters to handle allowspace file names
- Detour move/rename directory correctly
- Multiple fixes around FingerprintStore
- Fix cache miss diff when UnsafeOptions are cut off

# 0.20181012.14.0 (Release [16997](https://dev.azure.com/mseng/domino/_releasereleaseId=16997&_a=release-pipeline-progress)), released on 10/17/2018
- Fix the deletion of hardlinked source files on Unix
- Enable cache miss analyzer (mode '/m:cachemiss') on macOS
- Fixes/improvement for macOS Sandbox
- Add information about input/output directory dependencies to DependencyAnalyzer
- Improved handling of Nuget packages
- Add support for opaque directories in Drop
- Miscellaneous bug fixes and perf improvements

# 0.20181005.9.0 (Release [16715](https://dev.azure.com/mseng/domino/_releasereleaseId=16715&_a=release-pipeline-progress)), released on 10/10/2018
- Opaque directory details exposed in execution analyzers
- Caching effectiveness improvement around weak fingerprint stability
- Fix detours file probing hole around CreateFile without any access mode requested
- BuildXL support for macOS Mojave
- Misc bug fixes and perf improvements 

# 0.20180928.5.0 (Release [16413](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=16413)), released on 10/3/2018
- Improved incremental scheduling to better handle file-system changes during build
- Improved telemetry for FileChangeTracker
- Added directory symlinks for macOS
- Made /cleanonly consistent with Opaque/Shared Opaque behavior
- Fixed Shared Data Queue and Process/Child-Process termination for macOS
- Handle cases where a non-existent file access is reported as enumeration by “detours”
- Improved Sandboxed-Process-Report 
- Fixed bug where master cannot read execution log sent by workers
- Improved ConsoleLogTest
- Handle cases where an output file is declared as well as part of output directory
- Fixed bug that can cause an incorrect graph to be reloaded when only the user account requesting a build changes

# 0.20180922.1.0 (Release [16133](https://dev.azure.com/mseng/domino/_releasereleaseId=16133&_a=release-pipeline-progress)), released on 9/26/2018
- Improved Shared Opaque directories
- Removed legacy symlink options
- Improved MacOs
- Made deleting files by the cache more robust
- Various bugfixes

# 0.20180914.8.2 (Release [15959](https://dev.azure.com/mseng/domino/_releasereleaseId=15959&_a=release-pipeline-progress)), released on 9/19/2018
- Remove Mono.Posix/Unix dependencies
- Logging updates to push status csv to Kusto in CloudBuild and include skipped/failed pips in dev.log.
- Undeclared source read mode fixes
- Update ENL to version that fixes Generated input flags for predicted outputs
- Delete roots of pips' temporary directories after pips finish their executions

# 0.20180907.10.0 (Release [15568](https://dev.azure.com/mseng/domino/_releasereleaseId=15568&_a=release-pipeline-progress)), released on 9/12/2018
- Published signed Kext version 1.4.99
- Enabled file change tracker to track paths crossing volume boundaries.
- Add correctness check for copy-file pips when copying symlinks.
- Handle dynamic writes (writes inside opaque directories) on paths that have been probed to be absent.
- Added environment options for APEX builds. 

# 0.20180905.5.0 (Release [15436](https://dev.azure.com/mseng/domino/_releasereleaseId=15436&_a=release-pipeline-progress)), released on 9/7/2018
- Deprecate the following options: HashSymlinkAsTargetPath and AllowMissingOutputs
- Store symlink targets as string
- Measure the difference between the status logging frequency vs. how frequently it was scheduled as a signal for how unresponsive a machine is.
- Add retries over VSTS calls 
- Prevent FingerprintStore size from slowly creeping up
- Introduce /masterDownThrottleCpu arg to reduce the load on master
- Misc. bug fixes

# 0.20180816.8.1 (Release [14801](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=14801)), released on 8/22/2018
- Optimize handling of reparse points in detours
- Some legacy flags removed for DScript 
- Event hub based event propagation for CloudStore
- Various bug fixes

# 0.20180810.7.0 (Release [14478](https://dev.azure.com/mseng/domino/_releasereleaseId=14478&_a=release-pipeline-progress)), released on 8/15/2018
- Optimize handling of reparse points in detours
- Some legacy flags removed for DScript 
- Event hub based event propagation for CloudStore
- Various bug fixes

#  0.20180803.11.1 (Release [14338](https://dev.azure.com/mseng/domino/_releasereleaseId=14338&_a=release-pipeline-progress)), released on 8/9/2018
- Pips can now safely produce symlinks
- More tests are now running on Mac
- GraphCache enabled for Mac
- Added `/CacheMiss` flag to BuildXL for doing cache miss analysis as part of the build.
- Distributed builds collect more counters
- Fingerprint cache perf improvements
- DScript will now actively block any usage of V1-style syntax after warning about it for a while
- Various DScript v1 have been removed from the codebase.
- Simplified knobs for the file monitoring violation analyzer
- Various bugfixes

#  0.20180727.13.1 (Release [14122](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=14122)), released on 8/3/2018
- Fix performance regression due to FingerprintStore hitting RocksDB write stalls 
- Fix graph agnostic incremental scheduling underbuild 
- Optimizations for graph agnostic incremental scheduling 
- Fix incorrect cache miss analyzer results due to mixed-up paths 
- Fix pip StdOut logs being truncated 
- Make ProcessRunScript setlocal and don't print invalid directories 

# 0.20180720.22.1 (Release [13820](https://dev.azure.com/mseng/domino/_releasereleaseId=13820&_a=release-pipeline-progress)), released on 7/25/2018
- Improved garbage collection in Fingerprint store
- Rename QuickBuild into MsBuild in DominoScript
- Add telemetry tags (allowing for aggregating telemetry stats based on special tags assigned to pips)
- Fix a crash in ObservedInputProcessor
- Fix underbuild due to File Content Table and File Change Tracker going out of sync
- Fix number overflow in DScript literals

# 0.20180713.2.0 (Release [13471](https://dev.azure.com/mseng/domino/_releasereleaseId=13471&_a=release-pipeline-progress)), released on 7/18/2018
- macOS sandbox features and fixes
- CacheMiss analyzer refinements for uncacheable pips
- Reliability improvements for shared opaque directories
- Fix race condition in graph construction validations
- Fix ram utilization counters in macOS
- Decrease FingerprintStore’s disk usage
- Fix effectiveness bug in Graph Agnostic Incremental Scheduling

# 0.20180706.11.2 (Release [13360](https://dev.azure.com/mseng/domino/_releasereleaseId=13360&_a=release-pipeline-progress)), released on 7/11/2018
- Create output file handle with sequential scan based on extension filter
- Fixes for graph agnostic incremental scheduling
- Updates to console logging to include copy, IPC, and write file status
- Per-session cache statistics in CloudBuild
- Fingerprint store size improvements
- Observed Input Analyzer output changed to json.
- Miscellanies bug fixes and perf improvements
- Added an Api for caching values in DScript in module `Sdk.ValueCache`

# 0.20180629.6.0 (Release [13031](https://dev.azure.com/mseng/domino/_releasereleaseId=13031&_a=release-pipeline-progress)), released on 7/5/2018
- Introduced composite shared opaque directories.
- Introduced safe handles for Helium containers.
- Revamped KEXT communication.
- Deprecated exportFingerprints option.
- Tokenized paths by mounts in FailedPipInputAnalyzer.
- Allowed for disabling output replication during distributed builds.
- Bug fixes:
  - Fix for crash in ObservedInputProcessor when saving XLG events for failed pips.
  - Reset engine environment settings for BuildXL server process.
  - Fix for spec filters that ignore IPC pips.

# 0.20180622.13.0 (Release [12817](https://dev.azure.com/mseng/domino/_releasereleaseId=12817&_a=release-pipeline-progress)), released on 6/27/2018
- Created Build Break Analyzer
- Experimental Graph Agnostic Incremental Scheduling with improvements
- Fixed Drop failing in CB for Office
- Miscellaneous bug fixes and perf improvements

# 0.20180615.11.0 (Release [12554](https://dev.azure.com/mseng/domino/_releasereleaseId=12554&_a=release-pipeline-progress)), released on 6/15/2018
- Output strong fingerprint calculation to XLG for failed pips
- Enable critical path telemetry event
- Handle anti dependency validation when file change tracker failed to track non-existent path
- Add configurable max entry age (TTL) for fingerprint store
- Turn on fingerprint store by default for desktop builds
- CacheMissBeta became CacheMiss analyzer. The current CacheMiss analyzer was renamed to CacheMissLegacy
- Bugfix: Fix OutOfMemoryException when reading large stdout stream

# 0.20180608.12.0 (Release [12344](https://dev.azure.com/mseng/domino/DominoCore/_releasereleaseId=12344&_a=release-pipeline-progress)), released on 6/13/2018
- Qualifier details are displayed as part of pip progress indicator
- XML transformers are removed
- Improved crash telemetry
- Fixed false positives on cached graph hits
- Cache miss analyzer improvements
- Several bug fixes

# 0.20180602.3.2 (Release [12226](https://dev.azure.com/mseng/domino/_releasereleaseId=12226&_a=release-pipeline-progress)), released on 6/6/2018
- Add per-phase disk active time to stats file
- Change the default of /escapeIdentifiers to true
- Exclude spec path from semi-stable hash in DScript V2 builds
- Build CloudStoreTests with Net461
- Show qualifiers for each running pip
- Make DScript not depend on PipBuilder 
- Detect absent file probes on macOS
- Add a dynamic interop library for macOS
- Bugfix: Fix crash when hardlinking fingerprint store log files
- Bugfix: Handle the case where Journal can go back in time
- Bugfix: Fix crash in LogStats
## Patches
1. 0.20180602.3.3 (Release [12283](https://dev.azure.com/mseng/domino/TSE%20Team/_releasereleaseId=12283&_a=release-pipeline-progress)), released on 6/7/2018
   - Fix counter collection for stopwatches

# 0.20180525.3.0 (Release [11939](https://dev.azure.com/mseng/domino/_releasereleaseId=11939&_a=release-pipeline-progress)), released on 5/31/2018
- Associate FileChangeTracker with BuildXL engine version
- Tokenize machine specific paths in pathsets to improve x-machine cache hit rate
- Allow source files to be inputs even if a directory is fully sealed
- Reduce size of BuildXL binary package
- Fixes for critical path analyzer
- IO reduction for FingerprintStore and HistoricMetadataCache

# 0.20180520.2.1 (Release [11913](https://dev.azure.com/mseng/domino/_releasereleaseId=11913&_a=release-pipeline-progress)), released on 5/24/2018
- Pip data paths should be case insensitive in fingerprints 

# 0.20180520.2.0 (Release [11772](https://dev.azure.com/mseng/domino/_releasereleaseId=11772&_a=release-pipeline-progress)), released on 5/24/2018
- Graph Agnostic Icnremental Scheduling
- Updated QTest version
- Starting to collect telemetry on the Cache Miss Analyzer, Viewer and XlgAnalyzer
- FancyConsole now supported on Mac
- DScript debugger now works on Mac
- DScript now exposes host information like OS, cpu type and admin or not on the Context object.
- Sandbox improvements for Mac
- Various WDG specific components have moved to their OsgTools repo
- Various DominoXml tools have been removed in preparation of sunsetting DominoXml
- BuildXL now runs on Net461, We'll stop building Net451 after 2 releases on June 10th.
- Various bug fixes

# 0.20180512.1.4 (Release [11662](https://dev.azure.com/mseng/domino/_releasereleaseId=11662&_a=release-pipeline-progress)), released on 5/18/2018
- Patch:  Garbage collect historic metadata cache in background on load rather than waiting till end of build

# 0.20180512.1.3 (Release [11610](https://dev.azure.com/mseng/domino/_release?releaseId=11610&_a=release-summary)), released on 5/16/2018
- /nowarn prints warning to log, but not to console.
- Fixes for enabling optimized mode of path mappings and journal for probing.
- Make dpc filter work.
- Pip static fingerprints.
- Sending debug messages from KExt to BuildXL.
- Bug fixes.
- Patches:
   - Fix for /nowarn that incorrectly sends warnings to .wrn file.
   - Fix for stack overflow due to large module filters.

# 0.20180504.4.2 (Release [11609](https://dev.azure.com/mseng/domino/_release?releaseId=11609&_a=release-summary)), released on 5/16/2018
- Fix for stack overflow due to large module filters.

# 0.20180504.4.1 (Release [11392](https://dev.azure.com/mseng/domino/_release?releaseId=11392&_a=release-summary)), released on 5/11/2018
- Extended telemetry for graph cache miss analysis
- Important perf update for Office
- Cloud perf fixes for Office
- Fix drops in Office to contain file length for all files
- Various bug fixes and error handling
- Fix incremental scheduling overbuild
- Patch: Incremental scheduling underbuild due to improperly order drives after deserialization

# 0.20180427.9.0 (Release [11050](https://dev.azure.com/mseng/domino/_release?releaseId=11050&_a=release-summary)), released on 5/2/2018
- Add more glob support to BuildXL. Glob can now skip one directory level via ``glob(d`.`, "*/module.config.dsc")`` ([Documentation](/BuildXL/User-Guide/Script/Globbing))
- Qualifiers can now be specified as value on the commandline i.e. ``/q:configuration=debug;platform=x64``. ([Documentation](/BuildXL/User-Guide/Script/Qualifiers))
- Writing out Json files now has a convenience feature for dynamic keys.
- Reduce frontend memory footprint
- Various bug fixes

# 0.20180420.10.0 (Release [10854](https://dev.azure.com/mseng/domino/_release?releaseId=10854&_a=release-workitems)), released on 4/25/2018

- Pin caching (in-memory caching of remote pin operations) 
- Journal scanning performance improvements 
- Cache miss analyzer for incremental scheduling & graph filtering (in beta) 
- Misc. bug fixes & performance improvements

# 0.20180413.5.0 (Release [10599](https://dev.azure.com/mseng/domino/_release?releaseId=10599&_a=release-summary)), released on 4/18/2018
- A built-in DScript prelude is used when not specified
- Scrubbing phase can now be cancelled
- Improved help for the execution analyzer
- An assortment of memory optimizations
- Policy for controlling directory creation under writable mounts
- Improved frontend statistics

# 0.20180405.5.0 (Release [10352](https://dev.azure.com/mseng/domino/_release?releaseId=10352&_a=release-summary)), released on 4/11/2018
- Various memory optimizations
- Add StringBuilder as ambient DScript type
- Address the /forceSkipDeps hang in Office
- Add Json.write support to DominoScript
- Fix an underbuild bug involving weird interplay between BuildXL and InputTracker
- Add support for optional outputs

# 0.20180330.2.4 (Release [10310](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=10310&_a=release-summary)), released on 4/4/2018
- Optimized directory membership fingerprint computation.
- Fix on the use of OutputDebugString in Detours.
- Retry when opening files for hashing time out.
- DScript SDK clean up.
- Patches: 
 - Fix non-terminating dirty build. 
 - Fix for underbuild due to cross-talk between different BuildXL versions through tracker file.

# 0.20180316.7.0 (Release [9737](https://dev.azure.com/mseng/domino/_release?releaseId=9737&_a=release-summary)), released on 3/21/2018
- Add RAM throttling
- Shrink serialized size for small StringTables
- Target TLS 1.2 in domino & drop
- Fix issue with public surface generator required by the incremental frontend
- Fix underbuild when a new directory is created under read-only mount

# 0.20180309.2.0 (Release [9452](https://dev.azure.com/mseng/domino/_release?releaseId=9452&_a=release-summary)), released on 3/14/2018
- Ability to run journal scan in verify mode for absent file probes
- Filename filter on Source Seal Directory
- Historic metadata cache perf improvement
- Misc. perf and bug fixes

# 0.20180303.2.1 
- Hotfix patched release for adding directory creation of copy file pips under no CAS

# 0.20180303.2.0 (Release [9249](https://dev.azure.com/mseng/domino/_release?releaseId=9249&_a=release-summary)), released on 3/7/2018
- BuildXL now compiles against net461 (next to net451 and netcore2.0)
- Improve obsolete feature in DominoScript
- Decrease amount of materialization for office builds
- Improve local engine cache performance
- Make allowlist regex matching case insensitive
- More foundational work for BuildXL on Mac
- Assorted bug fixes

# 0.20180226.2.1 (Release [9289](https://dev.azure.com/mseng/domino/_release?releaseId=9289&_a=release-summary)), released on 3/5/2018
- Hotfix patched release for Fix incremental scheduling underbuild by retracking absent paths.

# 0.20180226.2.0 (Release [9027](https://dev.azure.com/mseng/domino/_release?releaseId=9027&_a=release-summary)), released on 3/1/2018
- This release follows a week with no Prod release, so there is rather substantial set of changes.
- A fix for Office underbuild issue (due to ChangeTracking) is not included in this build, so the same underbuild exists with this build. I’m not aware of any specific issue for WDG
- Many reliability improvements for caching related issues
- Many reliability improvements for USN related issues
- Improvements to fancy Console
- Assorted bug fixes

# 0.20180209.14.0 (Release [8433](https://dev.azure.com/mseng/domino/_release?releaseId=8433&_a=release-summary?releaseId=8433)), released on 2/14/2018
- Auto recovery and reliability improvements for caching related issues
- Nuget improvements
- VsCode improvements
- Assorted bug fixes

# 0.20180202.6.2 (Release [8423](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=8423&_a=release-summary)), released on 2/12/2018
- Hotfix patched release for contract assertion failure in ObservedInputProcessor

# 0.20180202.6.1 (Release [8219](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=8219)), released on 2/7/2018
- Add support for incremental linker
- Faster cache lookups
- Separate existing file probes from file content reads to reduce cache sensitivity
- Use wildcard pattern when computing the directory fingerprint to reduce cache sensitivity
- Consolidate missing output log messages
- Add more details to Performance Summary
- Add help link when errors or warnings are logged
- CacheMissAnalyzer for distributed builds
- Globbing support for DominoXML
- VsCode plugin bug fixes 
- Various engine bug fixes 

# 0.20180119.7.0 (Release [7635](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=7635)), released on 1/24/2018
- Decorator support for string literals in DominoScript
- TTL support to BFS cache
- DScript plugin for Visual Studio Code improvements
- Re-routing all execution logs back to master in distributed builds
- Dedicated thread logger for CloudBuild

# 0.20180112.9.0 (Release [7383](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=7383)), released on 1/17/2018
- Add R/W (read/write) to new Disallowed File Access console messages
- Analog DominoXml simplifications for Xtensa Ipa and Designer workflow.
- Improve performance of Office Builds
- Use DScript V2 by default
- Various memory improvements
- Perf improvements for Find All References in Language Server

# 0.20180105.8.2 (Release [7366](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=7366)), released on 1/12/2018
- Fix under build in incremental scheduling after a cache hit

# 0.20180105.8.0 (Release [7123](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=7123)), released on 1/10/2018
- Directory deleting improvements
- DScript IDE perf improvements
- Fix for DScript frontend cache corruption
- Updated WDG LegacyBuilder to support Analog spec simplication
- BuildXL commandline improvements
- Added retry logic for nuget download
- Memory optimizations
- More code on .NetCore
- Cache database hardening for malformed image
- Various bug fixes

# 0.20171215.8.1 (Release [6740](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6740)), released on 12/20/2017
- New cache that can wrap existing cache and expose it through HTTP endpoint
- Error reclassifying into User, Infrastructure, and Internal
- Aggregating and simplifying file access violation logging
- /unsafe_allowMissingOutput without filename will allow all missing outputs
- DominoScript: Removed the rule enforcing enums to be exported
- DominoScript: Disallowing nested Any type in top level declarations.
- Various bug fixes

# 0.20171207.10.0 (Release [6377](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6377)), released on 12/12/2017
- BuildXL Engine: Post graph validation
- DScript workspace fixes and improvements
- DominoScript: Allow @@Tool.option on types
- VS BuildXL: Disable qualifiers in generated .csproj files
- Directory deletion fixes
- Various bug fixes

# 0.20171203.2.0 (Release [6242](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6242))
- Fixes to Directory ASL (to really remove from the WDG build)
- Different fixes to Cache/Lazy Materialization and Incremental Scheduling/Scrub
- /warnaserror doesn’t cache pips
- Add Pip id to the FileAccessManifest payload
- Fix a very rare deadlock for Office builds (HandleOverlayMap <-----> OS Heap locks)

# 0.20171128.6.0 (Release [6043](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6043))
- Create symlinks inside BuildXL (i.e., symlink definition file)
- Compress graph files in CB
- Load balancing for drop pips
- Fix perf issue when choosing a worker
- Retry process pips if allowed by specified exit codes
- Various bug fixes

# 0.20171111.1.4 (Release [5820](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=5820))
- Fix under-build problem when DScript configuration files change (fix for #1129847)
- Fix issue where ChooseWorker is continually active if there are constrained resources (fix for #1124383) 

# 0.20171111.1.1 (Release [5677](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=5677))
- Make NtCreateFile return NULL instead of INVALID_HANDLE_VALUE (fix for #1126681)

# 0.20171111.1.0 (Release [5547](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5547))
- Enable DScript workspace by default (former `/exp:UseWorkspace`)
- Various improvements for DScript language service
- Analyzer for incremental scheduling
- Various CloudBuild perf optimizations
- Improvements for historic metadata cache lookups
- Add DsDoc tool that generates md files from DScript specs


# 0.20171103.8.2 (Release [5519](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5519))
- SSL retries in cache

# 0.20171103.8.1 (Release [5458](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5458))
- Update incremental scheduling state correctly when pips are clean and materialized (#1119672)

# 0.20171103.8.0 (Release [5275](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5275))
- Graph caching from content cache
- Memory optimization for tagged template expressions
- Lazily materialize drop inputs and don't redundantly process service pip
- Include all seal directories for a path in the filter passing nodes
- Reduce the size of HistoricMetadataCache
- Code completion for importing modules

# 0.20171025.6.1
- **Breaks Fingerprint**
- Various performance improvements for Distribution & Caching
- Determinism probe supports output directories
- Memory consumption improvements for path/symbol/string tables
- DScript memory optimizations
- CopyFile makeOutputsWriteable support
- Historic Metadata Cache (perf improvement on processing cache hits in larger builds)
- Misc Bug fixes

# 0.20171019.10.1
- **Breaks Fingerprint**
- Patched bug with filtering that can keep all dependencies from being included in the build

# 0.20171019.10.0 
- Better diagnostics for when files are open externally, preventing BuildXL from performing operations on those files.
- Introduce /historicalMetadataCache option
- CloudBuild reliability improvements
- Various optimizations (engine, fingerprinting and reduced memory dominoscript frontend)
- Various bugfixes

# 0.20171013.7.0
- Introduce /scheduleMetaPips. When false, BuildXL neither creates group meta pip nor schedules any meta pips. Default value is false.
- Allow forcing using historic perf. data from cache. Default value is true in Cloud build.
- Copy file pip support “keep writable” destination.
- Quickbuild resolver (preview).
- Optimization on the application of filter outputs.
- Consolidation of integration tests.
- More statistics on DominoScript.
- Various optimizations (particularly, memory-wise) on DominoScript

# 0.20171006.10.0 
- Perf improvements in DominoScipt, Engine, Filters and Cache.
- Fixes in DScript and Cache.
- Deleting files with POSICS_SEMANTIC fix. Changes to directory deletions and junctions.
- Some logging changes for more clarity.

# 0.20170929.12.0 
- Telemetry for incremental scheduling
- Fix for restore cache content from outputs on disk
- Symlink fixes
- DScript memory improvements
- Misc bug fixes

# 0.20170925.1.0 
- Various scheduler optimizations for distributed builds
- Performance improvements for IPC pips (service pips)
- Deprecated ChangeJournalService
- Frontend optimizations (DominoScript)
- Misc bug fixes 

# 0.20170911.3.0 
- Various optimizations for incremental parsing and type checking phases for the frontend
- Support for importFile function in module configuration files
- Fix memory leak in the frontend that prevented frontend memory to be released
- Work in progress for moving BuildXL to CoreCLR
- Allowed grouping for Sealed Directories.
- Misc bug fixes

# 0.20170830.16.0 
- Fix small frontend memory leak
- Better diagnostics for change journal scanning failures
- Misc bug fixes

# 0.20170825.10.0 
- Early termination of evaluator in case of error (controlled by the /stonOnFirstError switch)
- WDG Analog Rollout Support and OneCoreUAP Component Support
- Cache with fixed convergence and heartbeats
- Turn ZwOtherFileInformation by default
- Remove HashSourceFile pips from execution
- Compute and save file interaction fingerprint
- Make evaluation AST serializable
- Various optimizations/bugfixes: 
- Directory rename causes underbuild
- Release worker on failure
- BTW build running out of space appears to hang- Stop puts when out of space
- BuildXL produces incorrect input lists corrupting cache
- Do not materialize outputs when pip failed

# 0.20170810.1.0 
- Follow symlinks for all detoured APIs.
- DScript Optimizations: removal of expensive closure allocations from checker.
- Allow qualifier property types to be aliased.
- Workspace construction progress reporting.
- Support for comments on generated AST nodes.
- Import file.
- Feature for scrubbing multiple directories under scrubbable mounts.
- Module dirty builds: /unsafe_forceSkipDeps:module

# 0.20170806.1.0 
- Vertical Aggregator improperly resolving unbacked values on AddOrGet DScript graph patching compatibility improvements
- Underbuild on deleting/renaming existing member followed by adding a new member that was probe non-existent previously
- Fixes to Dirty builds
- Better graph patching with closure computations
- Bump up cloudstore package to include hash optimization
- Catching more exceptions during querying journal to be more fault-tolerant (bugfix #1045996)
- Introduce ReadThrough Metadata Cache
- New ZwSetFileInformation sub-routines detoured.
- Various DS related fixes.
- Making Detours allocate memory in its own private heap.
- Numerous bug fixes

# 0.20170727.9.0 
- Improvements to OOM prevention
- DScript graph patching compatibility improvements
- DScript tweaks related to v2
- Numerous bug fixes

# 0.20170718.8.0 
- Pip cancellation to prevent out-of-memory
- DScript graph patching
- Perf improvement for incremental scheduling
- Historical perf data in cache
- Replicate outputs for distributed metabuild
- Various bug fixes

# 0.20170710.7.0 
- Fix for cache blob upload
- Fix for overwritten pip failure error
- /unsafe_SourceFileCanBeInsideOutputDirectory is now on by default
- /validateExistingFileAccessesForOutputs is now off by default
- Distributed output replication to all workers to enable distributing metabuild
- Force materializing inputs on the worker in case of disabled lazy materialization
- Other bug fixes

# 0.20170703.10.0 
- Improved performance for Office builds
- Two CAS – Metadata, Content
- Fixed pip execution time shown in the critical path analyzer
- Removed deny write attribute to clear readonly flag in domino
- Various bug fixes

# 0.20170623.2.0 
- Addressed Server Deployment perf issue 
- Introduced a bug that sometimes throws away the graph cache, that fix went in with: PR: 231258
- Improve setup, and update more robust.
- Improved logging for memory usage
- IPC Pips can be distributed
- Improve robustness around directory handling
- DScript perf & memory improvements
- Updated Cache bits
- Improvements in Analyzers

# 0.20170619.4.0 
- ChangeJournal improvements 
- Fix ChangeJournal auto-upgrade bug
- Improve setup, and update more robust.
- DScript Workspace improvements 
- Office v2 compat work
- Some foundational work for incremental dominoscript frontend
- Release management improvements

# 20170604.1.0 
- Various improvements (reporting and performance) on incremental scheduling.
- Parallel server deployment.
- Allowed delete files with different ownership or ACLs.
- Auto-fallback for multi-level caches with vertical aggregator.
- Added machine-wide network usage telemetry.
- Revived absent path probe elision.
- Ctrl-break for ungraceful shutdown.
- Forced large object heap to compact when server mode process is idle.
- Enabled masking untracked accesses by default.
- Added Process creation time to execution log.
- Revived minimal server mode deployment.
- Enabled BuildXL to queue requested builds if there is one running.
- Misc. bug fixes

# 20170522.2.0 
- Added IPC pip support to the viewer.
- Added more reporting to make analyzers richer.
- Detoured GetFinalPathNameByHandle API.
- Fixes and implementations in the Distributed build functionality
- Using CloudStore 117.1.3
- DScript parallel checker enabled by default.
- Fixes for broken bandwidth stats
- Performance analysis and improvement
- Misc. bug fixes

# 20170515.3.0 
- Add feature to use outputs from output directory even when evicted from cache (/reuseOutputsOnDisk)
- Templates in DominoScript
- Fix memory leak in server mode
- Add support for specifying directory translations to BuildXL (for use with directory junctions)
- Misc. bug fixes

# 20170510.1.0 
- Allow source file materialization on distributed workers 
- DScript features: initial DScript analyzer framework
- Misc. DScript perf improvements
- Misc. bug fixes

# 20170508.1.0 
- Resource-aware scheduling
- Distribution as first-class citizen
- New DScript lint rule: no logic in project files

# 20170501.2.0 
- DScript Extensions: initial implementation and updated spec
- Drop fixes: (1) Implicitly schedule service finalizers in filtered builds, (2) Reload DropServiceClient when VssUnauthorizedException is thrown (due to expired VSS credential manager's session token)
- /FancyConsole is on by default (breaking change for people relying on BuildXL's stdout)
- Fixes regarding inconsistent process/cache counters
- DScript features: (1) support for backslashes in paths, (2) introduce "template" as a weak keyword, (3) initial spec for extensions
- Incremental scheduling features: better support for opaque directories and directory changes
- Unify /s and /filter:spec= options
- Log critical path at the end of the build

# 20170419.1 
- Revert logging changes for DX64
- Better caching for pips using _NTDRIVE and _NTBINDRIVE environment variables
- Correct perf summary at end of build
- Misc perf improvements
- Misc bug fixes

# 20170410.1 
- Various DScript V2 performance improvements
- Differentiate between existing directory probes & directory enumerations
- Fix bug with /warnaserror+ option
- Various rewording of verbose/warning/error messages
- Execution log enabled by default
- Implicit filtering translates to paths instead of values. ex: bxl.exe foo\bar\myBinary.exe
- Memory usage improvements for idle server mode process
- Perf improvements for drop integration
- Dump full process tree when a pip times out
- Misc bug fixes

# 20170321.5 
- Multiplexing capabilities for IPC
- Performance improvements for DScript V2
- XML wrapper spec builder simplifications
- More message word-crafting changes.
- Disallow copying symlinks
- Drop pip performance improvements

# 20170316.1 
- Optimization for bulk edge additions to MutableDirectedGraph.
- Enable DScript V2 and several bug fixes in this area.
- New CloudStore package included.
- Invalidating cache when changes in search path.
- Optimizations in DScript parsing.
- Preserve file name casing in cache.
- Various bug fixes.
- Some message word-crafting changes.
- Misc crash fixes

# 20170308.3 
- Console changes: more readable errors for DX64 and ability to filter out lines with regular expressions
- Seal directories work for distributed builds
- Filtering changes: support for seal directories, ipc, wildcards
- Determinism probe to test for non-deterministic pips
- Dependency violations are reported to execution log
- Better support for CloudBuild events
- Misc crash fixes

# 20170221.1 
- Allow configurable pass through environment variables

# 20170215.4 
- Native SDK authoring (CL Runner, Link runner)
- CL Runner improvements
- Unit Testing framework for DominoScript
- Add diagnostic options for server mode
- Various perf improvements and bug fixes
- Enable GC statistics events telemetry
- KeyForm support for DominoScript
- Make final pip status message stick on console with fancy console
- Remove metapips from Json graph
- SourceSealDirectories don't work with /unsafe_forceSkipDeps (dirty build)
- Fix crosstalk between architectures when using server mode
- Fix PipViewer: Can't expand 'Repro' section after collapsing

# 20170127.1 
- Misc crash fixes
- Fix precision error in RAM utilization based scheduling
- Improved CloudBuild reporting integration

# 20170123.1 
- Speed up execution log on large builds.
- Fix race in Hiearachical name table.
- Fix process counting log so that skip pips are included and "processes that were launched" only include external processes.
- Optimization for BasicFileSystemCache.

# 20170117.1 
- RAM availability can be configured for the pip execution phase
- Filtering by output directories correctly interact with opaque directories
- Drive mapping synchronization via dominow.exe, so a user can run sequential builds from different repos
- Seal source directories can be configured to operate recursively or top-level

# 20170106.4 
- Sealed source directories
- Support opaque output directories in distributed build
- Search path directory enumeration configuration for limiting cache invalidation in sealed directories
- Disk free percentage configuration for cache
- Use correct USN for symlinks hashed as target path.
- Handle probe access to files with wildcard characters in Detours
- Change default of flushing files to off for performance improvement.
- Revive /unsafe_MonitorFileAccesses to allow disabling detouring
- /specrefs feature to build all pips in spec files of pip dependencies
- Misc. bug fixes and performance improvements

# 20161102.5 
- Changes to the Cl Runner (more options and switches supported).
- Different optimizations in logging and telemetry.
- Different fixes in the detours implementation and detoured the ZwSetFileInformation to enforce access from "cmd.exe move".
- Improvements and fixes to the Git Runner.
- New UixCompiler Runner.
- Misc. bug fixes

# 20161002.4 
- Performance improvements for up to date checks
- Optimize creation of response files
- 2 phase lookup and new cache is default
- VS integration for native projects
- Misc. bug fixes

# 20160920.1 
- Untracked

# 20160908.1 
- /unsafe_forceSkipDeps
- Monitor NTCreateFile by default
- Use VSO hashing
- Deeper Nuget package fetching integration
- DScript evaluation phase performance improvements
- Execute phase performance improvements
- Pip repro script body generated in viewer
- Many bug fixes

# 20160823.4 
- Untracked

# 20160514.1 
- Prevent computer from sleeping while build is running

# 20160511.1 
- Experimental console (/exp:fancyconsole)

# 20160509.1 
- Use partial evaluation by default for XML- considerably speeds up evaluation for builds using spec file filters
- Option to not track AppData directory and exclude it from pip fingerprints

# 20160505.2 
- Graph and spec caching for DominoScript

# 20160429.1 
- Fix crash around SpecCache file locking
- Make ctrl-c immediately kill running processes rather than waiting for them to complete

# 20160428.1 
- Perf improvements for graph reloading on cold disk cache

# 20160421.1 
- Significant perf improvements for parsing on a cold filesystem cache when using change journal