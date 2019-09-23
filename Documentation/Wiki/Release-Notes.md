> Each release should have an entry in this document.

BuildXL has an official build weekly and a canary build nightly. 

The BuildXL team sends out a release email to:
* 1ES - BuildXL Release Notifications <bxl-relnot@microsoft.com>

each time an official release is triggered since: 0.20170619.4.0.

This page is a curated list of the release notes for releases after 0.20170619.4.0 and a manual copy of notable changes from each build before that. 
See the [the BuildXL Release Management page for the Production environment](https://dev.azure.com/mseng/domino/_release?definitionId=21&definitionEnvironmentId=112&_a=environment-summary) for full commit-level details for what is included in each build.
When a BuildXL developer implements a feature, fixes an important bug, solves an issue brought up by a customer, or makes any other notable change, they are encouraged to add an entry here.

# Upcoming release: 
- Removed old Microsoft.ContentStoreApp.exe from deployment (replaced with ContentStoreApp.exe)
- ...

# 0.1.0-20190913.9  (Release [40498](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=40498)). Released 09/19/2019
- Use proper pip data support for directory id.
- Augment (strengthen) weak fingerprint with common paths from observed path sets when a certain threshold of path sets is reached.
- Rename DominoInvocation and ExtraEventDataReported Events.
- Timeout proactive copies for Caching.
- Fix Drop Associate method (missing files mismatch).
- Add user facing documentation for XLDB.
- Fix front end throttling arguments.

# 0.1.0-20190906.8.2  (Release [40211](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=40211)). Released 09/11/2019
- Optimization for elision of AbsentPathProbes under shared opaque directories
- Allow error regex to apply to multiple lines of tool output
- Improve caching when using GlobalUnsafePassthroughEnvironmentVariables
- Update to QTest 19.9.6.1149. Expose qTestExcludeCcTargetsFile to exclude files from code coverage computation 
- Various bug fixes and improvements

# 0.1.0-20190830.7 (Release [39177](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=39177)). Released 9/05/2019
- Ignore directory probes for produced files in shared opaques
- Add more information to Disallowed File Access summary messages
- Add support for parallel graph loading [BinaryGraph]
- Upgrade to a newer version of qtest
- Various bug fixes and improvements


# 0.1.0-20190823.1.1 (Release [38769](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=38769)). Released 8/28/2019
- Add CODE_OF_CONDUCT.md and SECURITY.md documents
- Support for AzureDevOps formatting, summary page, etc.
- Remove Bond RCP
- Remove old visualization model
- Various VSCode extension improvements (in preparation for adding it to the marketplace)
- Various macOS Catalina improvements 
- Various XLG++ improvements
- Update to newest version of QTest


# 0.1.0-20190816.9 (Release [37763](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=37763)). Released 8/21/2019
- Add support for Azure devops Optimized output
- Binary graph fragments for BuildXL
- Introduce external input change list to BuildXL
- Stop the build after the first materialization error
- Make the VsCode plugin publishable to the VisualStudio marketplace
- macOS customers shouldn’t use this release due to a regression


# 0.1.0-20190809.3.4 (Release [37480](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=37480)). Released 8/15/2019
- New macOS sandbox based on EndpointSecurity APIs
- Introduce incrementalTool option to support gradle 
- Add BuildXLRuntimeCacheMissAllPips environment variable
- Various bug fixes and improvements


# 0.1.0-20190803.1 (Release [36404](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=36404)). Released 8/07/2019
- Reclassify some drop errors as user or infrastructure errors
- CloudBuild paths are no longer hardcoded in BuildXL repo, but controlled externally by CloudBuild,
- Improve colorization of DScript by default in VsCode
- Session Guid now encodes some extra information for easier correlation
- Some micro perf optimizations
- Various bugfixes

# 0.1.0-20190729.0 (Release [35768](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=35768)). Released 7/31/2019
- **[BREAKING CHANGE]** Statistic and BulkStatistic tables will stopped being populated in Aria. FinalStatistics will still be populated
- **[BREAKING CHANGE]** /unsafe_IgnoreUndeclaredAccessUnderSharedOpaques will no longer be enabled by default
- File access monitoring fix for probing a synlink chaing without the reparse point flag
- Progress on the macOS Catalina Endpoint Security based sandbox
- Distributed build reliability improvements
- [QTest] Updated to 19.7.18.221046
- Misc bug fixes
- We no longer publish net461 assemblies in our nuget packages
- The VsCode language server now runs on netcoreapp3.0 rather than net472

# 0.1.0-20190720.3.1 (Release [35267](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=35267)). Released 7/24/2019
- Don't delete contents of preserved opaque directories on cache replay.
- VstsCache with CoreCLR bits on Windows.
- Fix overflow issue in FIleContentInfo.Existence.
- Untrack Detours internal files during pip execution.
- Increase telemetry flush timeout in CloudBuild.
- Add a warning when execution analyzer inputs are incomplete.
- Distributed build bug fixes.

# 0.1.0-20190707.2 (Release [33603](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=33603)). Released 7/10/2019
- Fix errors getting into custom logs. 
- Improve error handling on master when connection is lost with worker.
- Force server redeployment on pipe exceptions.
- Report violations under shared opaques for writes in existing files.

# 0.1.0-20190630.0 (Release [32989](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=32989)). Released 7/3/2019
- Added remote telemetry for ContentStoreApp.
- Demoted file open failure message to diagnostics.
- Fix hole in file access monitoring for shared opaques. Add /unsafe_IgnoreUndeclaredAccessesUnderSharedOpaques- to receive new behavior. Expect us to reach out to you to coordinate migration if you are a shared opaques user.
- Don't reuse files for incremental checkpointing between epoch changes.
- Updated VS Extension to support AsyncPackage and background autoload.
- Handle long paths in directory creation in BuildXL's cache layer.
- Reduction of Cache tracing in Kusto.
- New copy file analyzer that prints out a list of all copy file pips.
- Spec generator changes to always include dependency closure for full framework packages and allow for compatibility between 4.6.1+ full framework packages and .NETStandard only specs.

# 0.1.0-20190622.1 (Release [32218](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=32218)). Released 6/26/2019
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

# 0.1.0-20190615.0 (Release [31601](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=31601)). Released 6/19/2019
- New arguments - unsafe_GlobalPassthroughEnvVars and unsafe_GlobalUntrackedScopes
- Add a way to specify passthrough environment variables for the MSBuild resolver
- Reduce noise in cachemiss.log (fixed the ordering of elements in a fingerprint)
- All BuildXL executables are now marked 64-bit
- Various perf improvements
- Various bug fixes

# 0.1.0-20190607.4 (Release [30942](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=30942)). Released 6/12/2019
- When watson crash handling reports 0xDEAD BuildXl will retry the pip.
- Various perf improvements
- Various bug fixes

# 0.1.0-20190531.4.3 (Release [30629](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=30629)). Released 6/5/2019
- Update QTest to 19.5.29.221321
- Redirect temp directories to be local when run in VM in CLoudBuild
- Csv option for PipExecutionPerformanceAnalyzer
- Improvements for cache logging
- Misc bug fixes

# 0.1.0-20190525.4.1 (Release [29991](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=29991)). Released 5/29/2019

- Switched BuildXL public LKG to be the DotNetCore one.
- Report the existence of outputs on workers.
- Enable preserve outputs mode for dynamic outputs.
- Fix crash when calling a worker with a disposed connection.
- A copy file pip referencing a non-existent source file is now a user error.
- [ContentStore] Avoid non-terminating process in quota keeper.
- Mac kext is now notarized.

# 0.1.0-20190518.0 (Release [29134](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=29134)). Released 5/22/2019
 - Allowed injection of shim process in lieu of all or some child processes.
 - Added ability to launch pips in VM.
 - Updated Google.Protobuf to 3.7
 - [Mac] Support for Apple kext notarization/staple.
 - [Helium] Added ability to disable WCI and pipe in BindFlt exceptions.
 - [Combined Engine] Added intermediate output path predictor.
 - [QTest] Added qTestRuntimeDependencies for explicit specification of extra run-time dependencies.


# 0.1.0-20190510.8 (Release [28504](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=28504&_a=release-pipeline-progress)). Released 5/15/2019
 - Increase WorkerAttachTimeout to 45min from 30min
 - Fix long path with SetFileAccessControl and HasWritableAccessControl operations
 - Adding qTestContextInfo to upload qtest results to VSTS
 - [macOS] Enable distributed copies on BuildXL builds on macOS 
 - [macOS] CoreRT native compilation for select projects targeting osx
 - [macOS] Use 'dependsOnCurrentHostOSDirectories' for tool definitions 

# 0.1.0-20190503.9 (Release [27992](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=27992&_a=release-pipeline-progress)). Released 5/8/2019
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

# 0.1.0-20190426.9 (Release [27516](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=27516)). Released 5/1/2019
- Fix EventCount telemetry for macOS
- Switch cache miss analysis to json diff  (new output format)
- Add lazy directory materialization for IPC pips
- Use net472 for the drop binaries in the Sdk folder
- Stop producing net461 nuget package
- Use historic cpu usage info as weight during scheduling
- Various bugfixes

# 0.1.0-20190419.5 (Release [26949](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=26949)). Released 4/24/2019
 - Add support for default untracking for MacOs from DScript to match windows
 - Support to parse and merge in additional config files
 - MsBuild supports multi qualifeir builds
 - Update QTest version
 - Make helium sandbox tombstone aware
 - Grpc reliability improvements
 - Handle some new RS6 filesystem behavior changes
 - Various bugfixes

# 0.1.0-20190412.7 (Release [26501](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=26501)). Released 4/17/2019
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


# 0.1.0-20190329.14.1 (Release [25731](https://dev.azure.com/mseng/Domino/_releaseProgress?releaseId=25731&_a=release-pipeline-progress)). Released 4/4/2019
  - BuildXL now ships as a net472 instead of net461
  - Some initial long path support
  - Stop tracing TaskCanceledException as errors in cache
  - Fix drop failing to get a producer of a file
  - Fix reported CPU usage on macOS
  - Remove named pipes
  - Attempt to fix failures to delete files before requesting cache materialize
  - Allow absent path probes of temp files under opaque directories if a pip depends on them
  - Various fixes

# 0.1.0-20190324.0 (Release [24943](https://mseng.visualstudio.com/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=24943)). Released 3/27/2019
  - Net472 bits are ready-to-use. 
  - Delete extra files in fully sealed directories (if scrub flag is set).
  - Change OutputGraph file system to include dynamic outputs.
  - Allow default mounts to be respecified.
  - Various bug fixes.

# 0.1.0-20190316.0 (Release [24373](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=24373)). Released 3/20/2019
  - Rename BuildXLScript to DScript.
  - BuildXL selfhost on Mac.
  - DScript SDK for managed code contains cross-plat resgen.
  - DScript has Array.sort method.
  - Various bug fixes.

# 0.1.0-20190309.0.3 (Release [23983](https://dev.azure.com/mseng/Domino/_releaseProgress?_a=release-pipeline-progress&releaseId=23983)). Released 3/13/2019
  - Fixes for VsCode extensions
  - Fixes to FileImpactAnalyzer and PipExecutionPerformanceAnalyzer
  - Support for adding all kinds of artifacts to drop
  - CMake resolver for Ninja builds
  - Memory guardrails for p-invoke calls on Mac
  - Nuget packages for CloudBuild, VSO and cache now contain net472 versions

# 0.1.0-20190302.2 (Release [23179](https://dev.azure.com/mseng/Domino/_release?releaseId=23179&_a=release-summary)). Released 3/6/2019
- Improved graph cache hits when environment variables or mount accesses are removed
- Improved runtime cache miss analysis on strong fingerprint misses
- Update from NetCore2.2.0-preview3 to NetCore2.2.0
- Graceful handling for ctrl+c cancellation when cache is still being initialized
- Update QTest version to support qtestVstsContext flag


# 0.1.0-20190224.0 (Release [22568](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=22568)). Released 2/27/2019
- New Download resolver
- Fixed drop error when calling addFilesToDrop with empty list
- Fixed runtime cache miss analysis crashing a build
- Various bug fixes

# 0.1.0-20190215.7 (Release [22052](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=22052)). Released 2/20/2019
- Net472 drop and BuildXL.net472 package are now produced to improve long path support on windows
- SandboxExec tool is now a standalone package
- Improve KEXT installation process
- Various bug fixes

# 0.1.0-20190208.9.1 (Release [21776](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=21776)). Released 2/13/2019
- macOS KEXT performance improvements
- Detour hardlink creation via SetInformationFile(FileLinkInformationEx)
- Build runtime cache miss analysis improvements
- Fixed a crash around RocksDB
- Move telemetry tag statistics to a separate "PipCounters" kusto table and fix bug with tags containing underscores


# 0.1.0-20190202.3 (Release [21316](https://dev.azure.com/mseng/Domino/_apps/hub/ms.vss-releaseManagement-web.cd-release-progress?_a=release-pipeline-progress&releaseId=21316)). Released 2/6/2019
- Domino has been renamed to BuildXL
- Misc crash and bug fixes

# 0.20190119.2.2 (Release [20895](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=20895)). Released 1/24/2019
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

# 0.20181207.2.1 (Release [19532](https://dev.azure.com/mseng/domino/_releasereleaseId=19532&_a=release-pipeline-progress)). Released 12/12/2018
- Perf and logging improvements for macOS
- Better handling for ERROR_CANT_ACCESS_FILE result returned by CreateFile()
- Improvements for .net standard consumers of repo artifacts
- Crash fixes

# 0.20181130.7.1 (Release [19155](https://dev.azure.com/mseng/domino/_releasereleaseId=19155&_a=release-pipeline-progress)). Released 12/5/2018.
- DominoScript's deployment Sdk has improved diagnostics for double deployment errors. The diagnostic now mentions the assemblies target framework as well.
- Add netCoreApp support to our NuGet package generation
- Cache tracing improvements
- Introduce Copy-on-Write for BuildXL Mac
- Keep RunInSubst.exe alive until child processes exit & reduce the wait time on substed drive from 30 to 5 seconds


# 0.20181124.1.0 (Release [18756](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=18756)). Released 11/28/2018.
- Introducing File Content Table to BuildXL Mac
- Renaming the macOS sandbox to BuildXLSandbox
- Perf improvements for highly cached builds in CloudBuild
- Fix graph deserialization issue for the builds that enable on-the-fly cache miss
- Core dump creation support for abnormal process exits (macOS)
- Move CloudStoreSDk closer to DominoSdk


# 0.20181110.3.0  (Release [18159](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=18159)). Released 11/14/2018.
- Adding option in DScript to glob folders recursively
- SharedOpaque distributed build fix
- Add static predictions to the MsBuild resolver
- Add telemetry tag in QTest SDK
- MacOS: perf fixes
- MacOS: control spotlight indexing of artifact folders

# 0.20181102.2.0  (Release [17787](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=17787)). Released 11/7/2018.
- MacOS: Dynamic sandbox child process timeout & improved sandbox telemetry
- MacOS: Dedupe Kext file access reports before sending to BuildXL client
- Misc. bug fixes

# 0.20181029.4.0 (Release [17574](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=17574&view=mine)). Released 10/31/2018.
- Don’t use POSIX delete on Windows to avoid hangs
- Fix deadlock on pass-through file system
- Handle files with multiple hard links in MacOS sandbox
- Identify writes after absent file probes violations by default
- Properly handle non-existent mount for graph reuse
- Experimental MsBuild resolver based on MsBuild build graph construction API


# 0.20181021.2.0 (Release [17290](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=17290)). Released 10/24/2018.
- Fix false absent file probes under opaque directories with lazy materialization
- Fix directory enumeration filters to handle whitespace file names
- Detour move/rename directory correctly
- Multiple fixes around FingerprintStore
- Fix cache miss diff when UnsafeOptions are cut off


# 0.20181012.14.0 (Release [16997](https://dev.azure.com/mseng/domino/_releasereleaseId=16997&_a=release-pipeline-progress)). Released 10/17/2018.
-	Fix the deletion of hardlinked source files on Unix
-	Enable cache miss analyzer (mode '/m:cachemiss') on macOS
-	Fixes/improvement for macOS Sandbox
-	Add information about input/output directory dependencies to DependencyAnalyzer
-	Improved handling of Nuget packages
-	Add support for opaque directories in Drop
-	Miscellaneous bug fixes and perf improvements


# 0.20181005.9.0 (Release [16715](https://dev.azure.com/mseng/domino/_releasereleaseId=16715&_a=release-pipeline-progress)). Released 10/10/2018.
-	Opaque directory details exposed in execution analyzers
-	Caching effectiveness improvement around weak fingerprint stability
-	Fix detours file probing hole around CreateFile without any access mode requested
-	BuildXL support for macOS Mojave
-	Misc bug fixes and perf improvements 


# 0.20180928.5.0 (Release [16413](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=16413)). Released 10/03/2018.
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

# 0.20180922.1.0 (Release [16133](https://dev.azure.com/mseng/domino/_releasereleaseId=16133&_a=release-pipeline-progress)). Released 9/26/2018.
- Improved Shared Opaque directories
- Removed legacy symlink options
- Improved MacOs
- Made deleting files by the cache more robust
- Various bugfixes


# 0.20180914.8.2 (Release [15959](https://dev.azure.com/mseng/domino/_releasereleaseId=15959&_a=release-pipeline-progress)). Released 9/19/2018.
- Remove Mono.Posix/Unix dependencies
- Logging updates to push status csv to Kusto in CloudBuild and include skipped/failed pips in dev.log.
- Undeclared source read mode fixes
- Update ENL to version that fixes Generated input flags for predicted outputs
- Delete roots of pips' temporary directories after pips finish their executions

# 0.20180907.10.0 (Release [15568](https://dev.azure.com/mseng/domino/_releasereleaseId=15568&_a=release-pipeline-progress)). Released 9/12/2018.
- Published signed Kext version 1.4.99
- Enabled file change tracker to track paths crossing volume boundaries.
- Add correctness check for copy-file pips when copying symlinks.
- Handle dynamic writes (writes inside opaque directories) on paths that have been probed to be absent.
- Added environment options for APEX builds. 

# 0.20180905.5.0 (Release [15436](https://dev.azure.com/mseng/domino/_releasereleaseId=15436&_a=release-pipeline-progress)). Released 9/7/2018.
- Deprecate the following options: HashSymlinkAsTargetPath and AllowMissingOutputs
- Store symlink targets as string
- Measure the difference between the status logging frequency vs. how frequently it was scheduled as a signal for how unresponsive a machine is.
- Add retries over VSTS calls 
- Prevent FingerprintStore size from slowly creeping up
- Introduce /masterDownThrottleCpu arg to reduce the load on master
- Misc. bug fixes

# 0.20180816.8.1 (Release [14801](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=14801)). Released 8/22/2018.
- Optimize handling of reparse points in detours
- Some legacy flags removed for DScript 
- Event hub based event propagation for CloudStore
- Various bug fixes

# 0.20180810.7.0 (Release [14478](https://dev.azure.com/mseng/domino/_releasereleaseId=14478&_a=release-pipeline-progress)). Released 8/15/2018.
- Optimize handling of reparse points in detours
- Some legacy flags removed for DScript 
- Event hub based event propagation for CloudStore
- Various bug fixes

#  0.20180803.11.1 (Release [14338](https://dev.azure.com/mseng/domino/_releasereleaseId=14338&_a=release-pipeline-progress)). Released 8/9/2018.
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

#  0.20180727.13.1 (Release [14122](https://dev.azure.com/mseng/domino/_release_a=release-pipeline-progress&releaseId=14122)). Released 8/3/2018.

- Fix performance regression due to FingerprintStore hitting RocksDB write stalls 
- Fix graph agnostic incremental scheduling underbuild 
- Optimizations for graph agnostic incremental scheduling 
- Fix incorrect cache miss analyzer results due to mixed-up paths 
- Fix pip StdOut logs being truncated 
- Make ProcessRunScript setlocal and don't print invalid directories 

# 0.20180720.22.1 (Release [13820](https://dev.azure.com/mseng/domino/_releasereleaseId=13820&_a=release-pipeline-progress)). Released 7/25/2018.
* Improved garbage collection in Fingerprint store
* Rename QuickBuild into MsBuild in DominoScript
* Add telemetry tags (allowing for aggregating telemetry stats based on special tags assigned to pips)
* Fix a crash in ObservedInputProcessor
* Fix underbuild due to File Content Table and File Change Tracker going out of sync
* Fix number overflow in DScript literals

# 0.20180713.2.0 (Release [13471](https://dev.azure.com/mseng/domino/_releasereleaseId=13471&_a=release-pipeline-progress)). Released 7/18/2018.
* macOS sandbox features and fixes
* CacheMiss analyzer refinements for uncacheable pips
* Reliability improvements for shared opaque directories
* Fix race condition in graph construction validations
* Fix ram utilization counters in macOS
* Decrease FingerprintStore’s disk usage
* Fix effectiveness bug in Graph Agnostic Incremental Scheduling


# 0.20180706.11.2 (Release [13360](https://dev.azure.com/mseng/domino/_releasereleaseId=13360&_a=release-pipeline-progress)). Released 7/11/2018.
* Create output file handle with sequential scan based on extension filter
* Fixes for graph agnostic incremental scheduling
* Updates to console logging to include copy, IPC, and write file status
* Per-session cache statistics in CloudBuild
* Fingerprint store size improvements
* Observed Input Analyzer output changed to json.
* Miscellanies bug fixes and perf improvements
* Added an Api for caching values in DScript in module `Sdk.ValueCache`

# 0.20180629.6.0 (Release [13031](https://dev.azure.com/mseng/domino/_releasereleaseId=13031&_a=release-pipeline-progress)). Released 7/5/2018.
* Introduced composite shared opaque directories.
* Introduced safe handles for Helium containers.
* Revamped KEXT communication.
* Deprecated exportFingerprints option.
* Tokenized paths by mounts in FailedPipInputAnalyzer.
* Allowed for disabling output replication during distributed builds.
* Bug fixes:
   - Fix for crash in ObservedInputProcessor when saving XLG events for failed pips.
   - Reset engine environment settings for BuildXL server process.
   - Fix for spec filters that ignore IPC pips.


# 0.20180622.13.0 (Release [12817](https://dev.azure.com/mseng/domino/_releasereleaseId=12817&_a=release-pipeline-progress)). Released 6/27/2018.
* Created Build Break Analyzer
* Experimental Graph Agnostic Incremental Scheduling with improvements
* Fixed Drop failing in CB for Office
* Miscellaneous bug fixes and perf improvements


# 0.20180615.11.0 (Release [12554](https://dev.azure.com/mseng/domino/_releasereleaseId=12554&_a=release-pipeline-progress)). Released 6/15/2018.
* Output strong fingerprint calculation to XLG for failed pips
* Enable critical path telemetry event
* Handle anti dependency validation when file change tracker failed to track non-existent path
* Add configurable max entry age (TTL) for fingerprint store
* Turn on fingerprint store by default for desktop builds
* CacheMissBeta became CacheMiss analyzer. The current CacheMiss analyzer was renamed to CacheMissLegacy
* Bugfix: Fix OutOfMemoryException when reading large stdout stream


# 0.20180608.12.0 (Release [12344](https://dev.azure.com/mseng/domino/DominoCore/_releasereleaseId=12344&_a=release-pipeline-progress)). Released 6/13/2018.
* Qualifier details are displayed as part of pip progress indicator
* XML transformers are removed
* Improved crash telemetry
* Fixed false positives on cached graph hits
* Cache miss analyzer improvements
* Several bug fixes


# 0.20180602.3.2 (Release [12226](https://dev.azure.com/mseng/domino/_releasereleaseId=12226&_a=release-pipeline-progress)) Released 6/6/2018
* Add per-phase disk active time to stats file
* Change the default of /escapeIdentifiers to true
* Exclude spec path from semi-stable hash in DScript V2 builds
* Build CloudStoreTests with Net461
* Show qualifiers for each running pip
* Make DScript not depend on PipBuilder 
* Detect absent file probes on macOS
* Add a dynamic interop library for macOS
* Bugfix: Fix crash when hardlinking fingerprint store log files
* Bugfix: Handle the case where Journal can go back in time
* Bugfix: Fix crash in LogStats
## Patches
1. 0.20180602.3.3 (Release [12283](https://dev.azure.com/mseng/domino/TSE%20Team/_releasereleaseId=12283&_a=release-pipeline-progress)) Released 6/7/2018
   * Fix counter collection for stopwatches

# 0.20180525.3.0 (Release [11939](https://dev.azure.com/mseng/domino/_releasereleaseId=11939&_a=release-pipeline-progress)) Released 5/31/2018
* Associate FileChangeTracker with BuildXL engine version
* Tokenize machine specific paths in pathsets to improve x-machine cache hit rate
* Allow source files to be inputs even if a directory is fully sealed
* Reduce size of BuildXL binary package
* Fixes for critical path analyzer
* IO reduction for FingerprintStore and HistoricMetadataCache

# 0.20180520.2.1 (Release [11913](https://dev.azure.com/mseng/domino/_releasereleaseId=11913&_a=release-pipeline-progress)) Released 5/24/2018
* Pip data paths should be case insensitive in fingerprints 

# 0.20180520.2.0 (Release [11772](https://dev.azure.com/mseng/domino/_releasereleaseId=11772&_a=release-pipeline-progress)) Released 5/24/2018
* Graph Agnostic Icnremental Scheduling
* Updated QTest version
* Starting to collect telemetry on the Cache Miss Analyzer, Viewer and XlgAnalyzer
* FancyConsole now supported on Mac
* DScript debugger now works on Mac
* DScript now exposes host information like OS, cpu type and admin or not on the Context object.
* Sandbox improvements for Mac
* Various WDG specific components have moved to their OsgTools repo
* Various DominoXml tools have been removed in preparation of sunsetting DominoXml
* BuildXL now runs on Net461, We'll stop building Net451 after 2 releases on June 10th.
* Various bug fixes

# 0.20180512.1.4 (Release [11662](https://dev.azure.com/mseng/domino/_releasereleaseId=11662&_a=release-pipeline-progress)) Released 5/18/2018
* Patch:  Garbage collect historic metadata cache in background on load rather than waiting till end of build

# 0.20180512.1.3 (Release [11610](https://dev.azure.com/mseng/domino/_release?releaseId=11610&_a=release-summary)) Released 5/16/2018
* /nowarn prints warning to log, but not to console.
* Fixes for enabling optimized mode of path mappings and journal for probing.
* Make dpc filter work.
* Pip static fingerprints.
* Sending debug messages from KExt to BuildXL.
* Bug fixes.
* Patches:
   * Fix for /nowarn that incorrectly sends warnings to .wrn file.
   * Fix for stack overflow due to large module filters.

# 0.20180504.4.2 (Release [11609](https://dev.azure.com/mseng/domino/_release?releaseId=11609&_a=release-summary)) Released 5/16/2018
* Fix for stack overflow due to large module filters.

# 0.20180504.4.1 (Release [11392](https://dev.azure.com/mseng/domino/_release?releaseId=11392&_a=release-summary)) Released 5/11/2018
* Extended telemetry for graph cache miss analysis
* Important perf update for Office
* Cloud perf fixes for Office
* Fix drops in Office to contain file length for all files
* Various bug fixes and error handling
* Fix incremental scheduling overbuild
* Patch: Incremental scheduling underbuild due to improperly order drives after deserialization

# 0.20180427.9.0 (Release [11050](https://dev.azure.com/mseng/domino/_release?releaseId=11050&_a=release-summary)) Released 5/2/2018
* Add more glob support to BuildXL. Glob can now skip one directory level via ``glob(d`.`, "*/module.config.dsc")`` ([Documentation](/BuildXL/User-Guide/Script/Globbing))
* Qualifiers can now be specified as value on the commandline i.e. ``/q:configuration=debug;platform=x64``. ([Documentation](/BuildXL/User-Guide/Script/Qualifiers))
* Writing out Json files now has a convenience feature for dynamic keys.
* Reduce frontend memory footprint
* Various bug fixes

# 0.20180420.10.0 (Release [10854](https://dev.azure.com/mseng/domino/_release?releaseId=10854&_a=release-workitems)) Released 4/25/2018

- Pin caching (in-memory caching of remote pin operations) 
- Journal scanning performance improvements 
- Cache miss analyzer for incremental scheduling & graph filtering (in beta) 
- Misc. bug fixes & performance improvements

# 0.20180413.5.0 (Release [10599](https://dev.azure.com/mseng/domino/_release?releaseId=10599&_a=release-summary)) Released 4/18/2018
* A built-in DScript prelude is used when not specified
* Scrubbing phase can now be cancelled
* Improved help for the execution analyzer
* An assortment of memory optimizations
* Policy for controlling directory creation under writable mounts
* Improved front-end statistics


# 0.20180405.5.0 (Release [10352](https://dev.azure.com/mseng/domino/_release?releaseId=10352&_a=release-summary)) Released 4/11/2018
* Various memory optimizations
* Add StringBuilder as ambient DScript type
* Address the /forceSkipDeps hang in Office
* Add Json.write support to DominoScript
* Fix an underbuild bug involving weird interplay between BuildXL and InputTracker
* Add support for optional outputs

# 0.20180330.2.4 (Release [10310](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=10310&_a=release-summary)) Released 4/4/2018
* Optimized directory membership fingerprint computation.
* Fix on the use of OutputDebugString in Detours.
* Retry when opening files for hashing time out.
* DScript SDK clean up.
* Patches: 
  - Fix non-terminating dirty build. 
  - Fix for underbuild due to cross-talk between different BuildXL versions through tracker file.

# 0.20180316.7.0 (Release [9737](https://dev.azure.com/mseng/domino/_release?releaseId=9737&_a=release-summary)) Released 3/21/2018
* Add RAM throttling
* Shrink serialized size for small StringTables
* Target TLS 1.2 in domino & drop
* Fix issue with public surface generator required by the incremental front-end
* Fix underbuild when a new directory is created under read-only mount


# 0.20180309.2.0 (Release [9452](https://dev.azure.com/mseng/domino/_release?releaseId=9452&_a=release-summary)) Released 3/14/2018
* Ability to run journal scan in verify mode for absent file probes
* Filename filter on Source Seal Directory
* Historic metadata cache perf improvement
* Misc. perf and bug fixes

# 0.20180303.2.1 
* Hotfix patched release for adding directory creation of copy file pips under no CAS

# 0.20180303.2.0 (Release [9249](https://dev.azure.com/mseng/domino/_release?releaseId=9249&_a=release-summary)) Released 3/7/2018
* BuildXL now compiles against net461 (next to net451 and netcore2.0)
* Improve obsolete feature in DominoScript
* Decrease amount of materialization for office builds
* Improve local engine cache performance
* Make whitelist regex matching case insensitive
* More foundational work for BuildXL on Mac
* Assorted bug fixes

# 0.20180226.2.1 (Release [9289](https://dev.azure.com/mseng/domino/_release?releaseId=9289&_a=release-summary)) Released 3/5/2018
* Hotfix patched release for Fix incremental scheduling underbuild by retracking absent paths.

# 0.20180226.2.0 (Release [9027](https://dev.azure.com/mseng/domino/_release?releaseId=9027&_a=release-summary)) Released 3/1/2018
* This release follows a week with no Prod release, so there is rather substantial set of changes.
* A fix for Office underbuild issue (due to ChangeTracking) is not included in this build, so the same underbuild exists with this build. I’m not aware of any specific issue for WDG
* Many reliability improvements for caching related issues
* Many reliability improvements for USN related issues
* Improvements to fancy Console
* Assorted bug fixes

# 0.20180209.14.0 (Release [8433](https://dev.azure.com/mseng/domino/_release?releaseId=8433&_a=release-summary?releaseId=8433)) Released 2/14/2018
* Auto recovery and reliability improvements for caching related issues
* Nuget improvements
* VsCode improvements
* Assorted bug fixes

# 0.20180202.6.2 (Release [8423](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=8423&_a=release-summary)) Released 2/12/2018
* Hotfix patched release for contract assertion failure in ObservedInputProcessor

# 0.20180202.6.1 (Release [8219](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=8219)) Released 2/7/2018
* Add support for incremental linker
* Faster cache lookups
* Separate existing file probes from file content reads to reduce cache sensitivity
* Use wildcard pattern when computing the directory fingerprint to reduce cache sensitivity
* Consolidate missing output log messages
* Add more details to Performance Summary
* Add help link when errors or warnings are logged
* CacheMissAnalyzer for distributed builds
* Globbing support for DominoXML
* VsCode plugin bug fixes 
* Various engine bug fixes 

# 0.20180119.7.0 (Release [7635](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=7635)) Released 1/24/2018
* Decorator support for string literals in DominoScript
* TTL support to BFS cache
* DScript plugin for Visual Studio Code improvements
* Re-routing all execution logs back to master in distributed builds
* Dedicated thread logger for CloudBuild

# 0.20180112.9.0 (Release [7383](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=7383)) Released 1/17/2018
* Add R/W (read/write) to new Disallowed File Access console messages
* Analog DominoXml simplifications for Xtensa Ipa and Designer workflow.
* Improve performance of Office Builds
* Use DScript V2 by default
* Various memory improvements
* Perf improvements for Find All References in Language Server

# 0.20180105.8.2 (Release [7366](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=7366)) Released 1/12/2018
* Fix under build in incremental scheduling after a cache hit

# 0.20180105.8.0 (Release [7123](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=7123)) Released 1/10/2018
* Directory deleting improvements
* DScript IDE perf improvements
* Fix for DScript frontend cache corruption
* Updated WDG LegacyBuilder to support Analog spec simplication
* BuildXL commandline improvements
* Added retry logic for nuget download
* Memory optimizations
* More code on .NetCore
* Cache database hardening for malformed image
* Various bug fixes

# 0.20171215.8.1 (Release [6740](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6740)) Released 12/20/2017
* New cache that can wrap existing cache and expose it through HTTP endpoint
* Error reclassifying into User, Infrastructure, and Internal
* Aggregating and simplifying file access violation logging
* /unsafe_allowMissingOutput without filename will allow all missing outputs
* DominoScript: Removed the rule enforcing enums to be exported
* DominoScript: Disallowing nested Any type in top level declarations.
* Various bug fixes


# 0.20171207.10.0 (Release [6377](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6377)) Released 12/12/2017
* BuildXL Engine: Post graph validation
* DScript workspace fixes and improvements
* DominoScript: Allow @@Tool.option on types
* VS BuildXL: Disable qualifiers in generated .csproj files
* Directory deletion fixes
* Various bug fixes

# 0.20171203.2.0 (Release [6242](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6242))
* Fixes to Directory ASL (to really remove from the WDG build)
* Different fixes to Cache/Lazy Materialization and Incremental Scheduling/Scrub
* /warnaserror doesn’t cache pips
* Add Pip id to the FileAccessManifest payload
* Fix a very rare deadlock for Office builds (HandleOverlayMap <-----> OS Heap locks)

# 0.20171128.6.0 (Release [6043](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=6043))
* Create symlinks inside BuildXL (i.e., symlink definition file)
* Compress graph files in CB
* Load balancing for drop pips
* Fix perf issue when choosing a worker
* Retry process pips if allowed by specified exit codes
* Various bug fixes

# 0.20171111.1.4 (Release [5820](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=5820))
*	Fix under-build problem when DScript configuration files change (fix for #1129847)
*	Fix issue where ChooseWorker is continually active if there are constrained resources (fix for #1124383) 

# 0.20171111.1.1 (Release [5677](https://dev.azure.com/mseng/domino/_release?definitionId=21&releaseId=5677))
* Make NtCreateFile return NULL instead of INVALID_HANDLE_VALUE (fix for #1126681)

# 0.20171111.1.0 (Release [5547](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5547))
* Enable DScript workspace by default (former `/exp:UseWorkspace`)
* Various improvements for DScript language service
* Analyzer for incremental scheduling
* Various CloudBuild perf optimizations
* Improvements for historic metadata cache lookups
* Add DsDoc tool that generates md files from DScript specs


# 0.20171103.8.2 (Release [5519](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5519))
* SSL retries in cache

# 0.20171103.8.1 (Release [5458](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5458))
* Update incremental scheduling state correctly when pips are clean and materialized (#1119672)

# 0.20171103.8.0 (Release [5275](https://dev.azure.com/mseng/domino/_release?definitionId=21&_a=release-summary&releaseId=5275))
* Graph caching from content cache
* Memory optimization for tagged template expressions
* Lazily materialize drop inputs and don't redundantly process service pip
* Include all seal directories for a path in the filter passing nodes
* Reduce the size of HistoricMetadataCache
* Code completion for importing modules

# 0.20171025.6.1
* **Breaks Fingerprint**
* Various performance improvements for Distribution & Caching
* Determinism probe supports output directories
* Memory consumption improvements for path/symbol/string tables
* DScript memory optimizations
* CopyFile makeOutputsWriteable support
* Historic Metadata Cache (perf improvement on processing cache hits in larger builds)
* Misc Bug fixes


# 0.20171019.10.1
* **Breaks Fingerprint**
* Patched bug with filtering that can keep all dependencies from being included in the build

# 0.20171019.10.0 
* Better diagnostics for when files are open externally, preventing BuildXL from performing operations on those files.
* Introduce /historicalMetadataCache option
* CloudBuild reliability improvements
* Various optimizations (engine, fingerprinting and reduced memory dominoscript frontend)
* Various bugfixes

# 0.20171013.7.0
* Introduce /scheduleMetaPips. When false, BuildXL neither creates group meta pip nor schedules any meta pips. Default value is false.
* Allow forcing using historic perf. data from cache. Default value is true in Cloud build.
* Copy file pip support “keep writable” destination.
* Quickbuild resolver (preview).
* Optimization on the application of filter outputs.
* Consolidation of integration tests.
* More statistics on DominoScript.
* Various optimizations (particularly, memory-wise) on DominoScript


# 0.20171006.10.0 
* Perf improvements in DominoScipt, Engine, Filters and Cache.
* Fixes in DScript and Cache.
* Deleting files with POSICS_SEMANTIC fix. Changes to directory deletions and junctions.
* Some logging changes for more clarity.


# 0.20170929.12.0 
* Telemetry for incremental scheduling
* Fix for restore cache content from outputs on disk
* Symlink fixes
* DScript memory improvements
* Misc bug fixes


# 0.20170925.1.0 
* Various scheduler optimizations for distributed builds
* Performance improvements for IPC pips (service pips)
* Deprecated ChangeJournalService
* Frontend optimizations (DominoScript)
* Misc bug fixes 


# 0.20170911.3.0 
* Various optimizations for incremental parsing and type checking phases for the front-end
* Support for importFile function in module configuration files
* Fix memory leak in the front-end that prevented front-end memory to be released
* Work in progress for moving BuildXL to CoreCLR
* Allowed grouping for Sealed Directories.
* Misc bug fixes


# 0.20170830.16.0 
* Fix small frontend memory leak
* Better diagnostics for change journal scanning failures
* Misc bug fixes


# 0.20170825.10.0 
* Early termination of evaluator in case of error (controlled by the /stonOnFirstError switch)
* WDG Analog Rollout Support and OneCoreUAP Component Support
* Cache with fixed convergence and heartbeats
* Turn ZwOtherFileInformation by default
* Remove HashSourceFile pips from execution
* Compute and save file interaction fingerprint
* Make evaluation AST serializable
* Various optimizations/bugfixes: 
* Directory rename causes underbuild
* Release worker on failure
* BTW build running out of space appears to hang - Stop puts when out of space
* BuildXL produces incorrect input lists corrupting cache
* Do not materialize outputs when pip failed


# 0.20170810.1.0 
* Follow symlinks for all detoured APIs.
* DScript Optimizations: removal of expensive closure allocations from checker.
* Allow qualifier property types to be aliased.
* Workspace construction progress reporting.
* Support for comments on generated AST nodes.
* Import file.
* Feature for scrubbing multiple directories under scrubbable mounts.
* Module dirty builds: /unsafe_forceSkipDeps:module


# 0.20170806.1.0 
* Vertical Aggregator improperly resolving unbacked values on AddOrGet DScript graph patching compatibility improvements
* Underbuild on deleting/renaming existing member followed by adding a new member that was probe non-existent previously
* Fixes to Dirty builds
* Better graph patching with closure computations
* Bump up cloudstore package to include hash optimization
* Catching more exceptions during querying journal to be more fault-tolerant (bugfix #1045996)
* Introduce ReadThrough Metadata Cache
* New ZwSetFileInformation sub-routines detoured.
* Various DS related fixes.
* Making Detours allocate memory in its own private heap.
* Numerous bug fixes


# 0.20170727.9.0 
* Improvements to OOM prevention
* DScript graph patching compatibility improvements
* DScript tweaks related to v2
* Numerous bug fixes


# 0.20170718.8.0 
* Pip cancellation to prevent out-of-memory
* DScript graph patching
* Perf improvement for incremental scheduling
* Historical perf data in cache
* Replicate outputs for distributed metabuild
* Various bug fixes


# 0.20170710.7.0 
* Fix for cache blob upload
* Fix for overwritten pip failure error
* /unsafe_SourceFileCanBeInsideOutputDirectory is now on by default
* /validateExistingFileAccessesForOutputs is now off by default
* Distributed output replication to all workers to enable distributing metabuild
* Force materializing inputs on the worker in case of disabled lazy materialization
* Other bug fixes


# 0.20170703.10.0 
* Improved performance for Office builds
* Two CAS – Metadata, Content
* Fixed pip execution time shown in the critical path analyzer
* Removed deny write attribute to clear readonly flag in domino
* Various bug fixes


# 0.20170623.2.0 
* Addressed Server Deployment perf issue 
* Introduced a bug that sometimes throws away the graph cache, that fix went in with: PR: 231258
* Improve setup, and update more robust.
* Improved logging for memory usage
* IPC Pips can be distributed
* Improve robustness around directory handling
* DScript perf & memory improvements
* Updated Cache bits
* Improvements in Analyzers


# 0.20170619.4.0 
* ChangeJournal improvements 
* Fix ChangeJournal auto-upgrade bug
* Improve setup, and update more robust.
* DScript Workspace improvements 
* Office v2 compat work
* Some foundational work for incremental dominoscript frontend
* Release management improvements

# 20170604.1.0 
* Various improvements (reporting and performance) on incremental scheduling.
* Parallel server deployment.
* Allowed delete files with different ownership or ACLs.
* Auto-fallback for multi-level caches with vertical aggregator.
* Added machine-wide network usage telemetry.
* Revived absent path probe elision.
* Ctrl-break for ungraceful shutdown.
* Forced large object heap to compact when server mode process is idle.
* Enabled masking untracked accesses by default.
* Added Process creation time to execution log.
* Revived minimal server mode deployment.
* Enabled BuildXL to queue requested builds if there is one running.
* Misc. bug fixes

# 20170522.2.0 
* Added IPC pip support to the viewer.
* Added more reporting to make analyzers richer.
* Detoured GetFinalPathNameByHandle API.
* Fixes and implementations in the Distributed build functionality
* Using CloudStore 117.1.3
* DScript parallel checker enabled by default.
* Fixes for broken bandwidth stats
* Performance analysis and improvement
* Misc. bug fixes

# 20170515.3.0 
* Add feature to use outputs from output directory even when evicted from cache (/reuseOutputsOnDisk)
* Templates in DominoScript
* Fix memory leak in server mode
* Add support for specifying directory translations to BuildXL (for use with directory junctions)
* Misc. bug fixes

# 20170510.1.0 
* Allow source file materialization on distributed workers 
* DScript features: initial DScript analyzer framework
* Misc. DScript perf improvements
* Misc. bug fixes

# 20170508.1.0 
* Resource-aware scheduling
* Distribution as first-class citizen
* New DScript lint rule: no logic in project files

# 20170501.2.0 
* DScript Extensions: initial implementation and updated spec
* Drop fixes: (1) Implicitly schedule service finalizers in filtered builds, (2) Reload DropServiceClient when VssUnauthorizedException is thrown (due to expired VSS credential manager's session token)
* /FancyConsole is on by default (breaking change for people relying on BuildXL's stdout)
* Fixes regarding inconsistent process/cache counters
* DScript features: (1) support for backslashes in paths, (2) introduce "template" as a weak keyword, (3) initial spec for extensions
* Incremental scheduling features: better support for opaque directories and directory changes
* Unify /s and /filter:spec= options
* Log critical path at the end of the build

# 20170419.1 
* Revert logging changes for DX64
* Better caching for pips using _NTDRIVE and _NTBINDRIVE environment variables
* Correct perf summary at end of build
* Misc perf improvements
* Misc bug fixes

# 20170410.1 
* Various DScript V2 performance improvements
* Differentiate between existing directory probes & directory enumerations
* Fix bug with /warnaserror+ option
* Various rewording of verbose/warning/error messages
* Execution log enabled by default
* Implicit filtering translates to paths instead of values. ex: bxl.exe foo\bar\myBinary.exe
* Memory usage improvements for idle server mode process
* Perf improvements for drop integration
* Dump full process tree when a pip times out
* Misc bug fixes

# 20170321.5 
* Multiplexing capabilities for IPC
* Performance improvements for DScript V2
* XML wrapper spec builder simplifications
* More message word-crafting changes.
* Disallow copying symlinks
* Drop pip performance improvements

# 20170316.1 
* Optimization for bulk edge additions to MutableDirectedGraph.
* Enable DScript V2 and several bug fixes in this area.
* New CloudStore package included.
* Invalidating cache when changes in search path.
* Optimizations in DScript parsing.
* Preserve file name casing in cache.
* Various bug fixes.
* Some message word-crafting changes.
* Misc crash fixes

# 20170308.3 
* Console changes: more readable errors for DX64 and ability to filter out lines with regular expressions
* Seal directories work for distributed builds
* Filtering changes: support for seal directories, ipc, wildcards
* Determinism probe to test for non-deterministic pips
* Dependency violations are reported to execution log
* Better support for CloudBuild events
* Misc crash fixes

# 20170221.1 
* Allow configurable pass through environment variables

# 20170215.4 
* Native SDK authoring (CL Runner, Link runner)
* CL Runner improvements
* Unit Testing framework for DominoScript
* Add diagnostic options for server mode
* Various perf improvements and bug fixes
* Enable GC statistics events telemetry
* KeyForm support for DominoScript
* Make final pip status message stick on console with fancy console
* Remove metapips from Json graph
* SourceSealDirectories don't work with /unsafe_forceSkipDeps (dirty build)
* Fix crosstalk between architectures when using server mode
* Fix PipViewer: Can't expand 'Repro' section after collapsing

# 20170127.1 
* Misc crash fixes
* Fix precision error in RAM utilization based scheduling
* Improved CloudBuild reporting integration

# 20170123.1 
* Speed up execution log on large builds.
* Fix race in Hiearachical name table.
* Fix process counting log so that skip pips are included and "processes that were launched" only include external processes.
* Optimization for BasicFileSystemCache.

# 20170117.1 
* RAM availability can be configured for the pip execution phase
* Filtering by output directories correctly interact with opaque directories
* Drive mapping synchronization via dominow.exe, so a user can run sequential builds from different repos
* Seal source directories can be configured to operate recursively or top-level

# 20170106.4 
* Sealed source directories
* Support opaque output directories in distributed build
* Search path directory enumeration configuration for limiting cache invalidation in sealed directories
* Disk free percentage configuration for cache
* Use correct USN for symlinks hashed as target path.
* Handle probe access to files with wildcard characters in Detours
* Change default of flushing files to off for performance improvement.
* Revive /unsafe_MonitorFileAccesses to allow disabling detouring
* /specrefs feature to build all pips in spec files of pip dependencies
* Misc. bug fixes and performance improvements

# 20161102.5 
* Changes to the Cl Runner (more options and switches supported).
* Different optimizations in logging and telemetry.
* Different fixes in the detours implementation and detoured the ZwSetFileInformation to enforce access from "cmd.exe move".
* Improvements and fixes to the Git Runner.
* New UixCompiler Runner.
* Misc. bug fixes

# 20161002.4 
* Performance improvements for up to date checks
* Optimize creation of response files
* 2 phase lookup and new cache is default
* VS integration for native projects
* Misc. bug fixes

# 20160920.1 
* Untracked

# 20160908.1 
* /unsafe_forceSkipDeps
* Monitor NTCreateFile by default
* Use VSO hashing
* Deeper Nuget package fetching integration
* DScript evaluation phase performance improvements
* Execute phase performance improvements
* Pip repro script body generated in viewer
* Many bug fixes

# 20160823.4 
* Untracked

# 20160514.1 
* Prevent computer from sleeping while build is running

# 20160511.1 
* Experimental console (/exp:fancyconsole)

# 20160509.1 
* Use partial evaluation by default for XML - considerably speeds up evaluation for builds using spec file filters
* Option to not track AppData directory and exclude it from pip fingerprints

# 20160505.2 
* Graph and spec caching for DominoScript

# 20160429.1 
* Fix crash around SpecCache file locking
* Make ctrl-c immediately kill running processes rather than waiting for them to complete

# 20160428.1 
* Perf improvements for graph reloading on cold disk cache

# 20160421.1 
* Significant perf improvements for parsing on a cold filesystem cache when using change journal

