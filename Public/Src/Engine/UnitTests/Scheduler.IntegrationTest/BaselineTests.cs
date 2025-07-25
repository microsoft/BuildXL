// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using LogEventId = BuildXL.Scheduler.Tracing.LogEventId;
using PipsTracingLogEventId = BuildXL.Pips.Tracing.LogEventId;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;
using SchedulerLogEventId = BuildXL.Scheduler.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Tests that validate basic functionality and caching behavior
    /// </summary>
    [Trait("Category", "BaselineTests")]
    public class BaselineTests : SchedulerIntegrationTestBase
    {

        public BaselineTests(ITestOutputHelper output) : base(output)
        {
        }

        // Simulated memory conditions
        private static readonly PerformanceCollector.MachinePerfInfo s_highMemoryPressurePerf =
            new PerformanceCollector.MachinePerfInfo()
            {
                AvailableRamMb = 100,
                EffectiveAvailableRamMb = 100,
                RamUsagePercentage = 99,
                EffectiveRamUsagePercentage = 99,
                TotalRamMb = 10000,
                CommitUsedMb = 5000,
                CommitUsagePercentage = 50,
                CommitLimitMb = 10000,
            };

        private static readonly PerformanceCollector.MachinePerfInfo s_normalPerf =
            new PerformanceCollector.MachinePerfInfo()
            {
                AvailableRamMb = 9000,
                EffectiveAvailableRamMb = 9000,
                RamUsagePercentage = 10,
                EffectiveRamUsagePercentage = 10,
                TotalRamMb = 10000,
                CommitUsedMb = 5000,
                CommitUsagePercentage = 50,
                CommitLimitMb = 10000,
            };

        /// <summary>
        /// Verifies that when a pip fails and exits the build, the code
        /// paths entered do not throw exceptions.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyGracefulTeardownOnPipFailure(bool readOnlyMount)
        {
            var testRoot = readOnlyMount ? ReadonlyRoot : ObjectRoot;

            // Set up a standard directory that will have a non-zero fingerprint when enumerated
            var dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(testRoot));
            var dirString = ArtifactToString(dir);
            var absentFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix, dirString));
            var nestedFile = CreateSourceFile(dirString);

            // Set up a seal directory
            var sealDirPath = CreateUniqueDirectory();
            var sealDirString = sealDirPath.ToString(Context.PathTable);
            var absentSealDirFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix, sealDirString));
            var nestedSealDirFileForProbe = CreateSourceFile(sealDirString);
            var nestedSealDirFileForRead = CreateSourceFile(sealDirString);

            var sealDir = SealDirectory(sealDirPath, SealDirectoryKind.SourceAllDirectories);

            var upstreamOutput = CreateOutputFileArtifact();
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(upstreamOutput)
            });

            // Create a pip that does various dynamically observed inputs, then fails
            var output = CreateOutputFileArtifact();
            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(upstreamOutput),
                Operation.Probe(absentFile),
                Operation.ReadFile(nestedFile),
                Operation.Probe(absentSealDirFile, doNotInfer: true),
                Operation.Probe(nestedSealDirFileForProbe, doNotInfer: true),
                Operation.ReadFile(nestedSealDirFileForRead, doNotInfer: true),
                Operation.EnumerateDir(dir),
                Operation.WriteFile(output),
                Operation.Fail(),
            });
            pipBuilder.AddInputDirectory(sealDir);
            SchedulePipBuilder(pipBuilder);

            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(output),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
        }

        [Fact]
        public void StopSchedulerDueToLowPhysicalMemory()
        {
            Configuration.Schedule.MaximumRamUtilizationPercentage = 95;

            var output = CreateOutputFileArtifact();

            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact(output)),
            };

            var builder = CreatePipBuilder(operations);
            SchedulePipBuilder(builder);

            RunScheduler(testHooks: new SchedulerTestHooks()
            {
                GenerateSyntheticMachinePerfInfo = (lc, s) => s_highMemoryPressurePerf
            });

            AssertVerboseEventLogged(LogEventId.LowRamMemory);
            AssertVerboseEventLogged(LogEventId.StoppingProcessExecutionDueToMemory);
        }

        [Fact]
        public void StopSchedulerDueToLowCommitMemory()
        {
            Configuration.Schedule.MaximumRamUtilizationPercentage = 95;

            var output = CreateOutputFileArtifact();

            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact(output)),
            };

            var builder = CreatePipBuilder(operations);
            SchedulePipBuilder(builder);

            RunScheduler(testHooks: new SchedulerTestHooks()
            {
                GenerateSyntheticMachinePerfInfo = (lc, s) => new PerformanceCollector.MachinePerfInfo()
                {
                    AvailableRamMb = 9000,
                    EffectiveAvailableRamMb = 9000,
                    RamUsagePercentage = 10,
                    EffectiveRamUsagePercentage = 10,
                    TotalRamMb = 10000,
                    CommitUsedMb = 99000,
                    CommitUsagePercentage = 99,
                    CommitLimitMb = 100000,
                }

            });

            AssertVerboseEventLogged(LogEventId.LowCommitMemory);
            AssertVerboseEventLogged(LogEventId.StoppingProcessExecutionDueToMemory);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyGracefulTeardownWhenObservedInputProcessorInAbortedState(bool shouldPipSucceed)
        {
            // Set up a pip that has reads from a SealDirectory that's under an untracked mount.
            // This will cause a failure in the ObservedInputProcessor that causes processing to
            // abort. Make sure this doesn't crash
            var untrackedSealDir = CreateUniqueDirectory(NonHashableRoot);
            var sealDirUntracked = SealDirectory(untrackedSealDir, SealDirectoryKind.SourceAllDirectories);

            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.ReadFile(CreateSourceFile(untrackedSealDir.ToString(Context.PathTable)), doNotInfer: true)
            };

            if (!shouldPipSucceed)
            {
                operations.Add(Operation.Fail());
            }

            var pipBuilder = CreatePipBuilder(operations);
            pipBuilder.AddInputDirectory(sealDirUntracked);
            SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(SchedulerLogEventId.AbortObservedInputProcessorBecauseFileUntracked);
            if (!shouldPipSucceed)
            {
                AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyGracefulTeardownWhenObservedInputProcessorInMismatchStateAndPipFails(bool shouldPipSucceed)
        {
            // Set up a pip that has reads a file under the same root as a sealed directory but not part of the sealed
            // directory. This will cause the ObservedInputProcessor to be in a ObservedInputProcessingStatus.Mismatched
            // state. Make sure this doesn't crash
            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(CreateUniqueDirectory(ReadonlyRoot), SealDirectoryKind.Partial);
            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.ReadFile(CreateSourceFile(sealedDirectory.Path.ToString(Context.PathTable)), doNotInfer: true),
            };

            if (!shouldPipSucceed)
            {
                operations.Add(Operation.Fail());
            }

            var pipBuilder = CreatePipBuilder(operations);
            pipBuilder.AddInputDirectory(sealedDirectory);
            SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertFailure();
            IgnoreWarnings();
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            if (!shouldPipSucceed)
            {
                AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
            }
        }

        [Fact]
        public void VerifyGracefulTeardownWhenAvailableDiskSpaceLowerThanMinimumDiskSpaceForPipsGb()
        {
            Configuration.Schedule.MinimumDiskSpaceForPipsGb = int.MaxValue - 1;

            var output = CreateOutputFileArtifact();

            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact(output)),
                Operation.Block(),
            };

            var builder = CreatePipBuilder(operations);
            SchedulePipBuilder(builder);

            using (PerformanceCollector performanceCollector = new PerformanceCollector(System.TimeSpan.FromMilliseconds(10), testHooks: new PerformanceCollector.TestHooks() { AvailableDiskSpace = 0 }))
            {
                RunScheduler(performanceCollector: performanceCollector, updateStatusTimerEnabled: true).AssertFailure();
                IgnoreWarnings();
                AssertErrorEventLogged(LogEventId.WorkerFailedDueToLowDiskSpace);
            }
        }

        // TODO: Investigate why this times out on Linux, work item#1984802
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void VerifyGracefulTeardownWhenAvailableDiskSpaceReducesBelowMinimumDiskSpaceForPipGb()
        {
            Configuration.Schedule.MinimumDiskSpaceForPipsGb = 3;

            var output = CreateOutputFileArtifact();

            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact(output)),
                Operation.Block(),
            };

            var builder = CreatePipBuilder(operations);
            SchedulePipBuilder(builder);

            DateTime delayedTime = DateTime.UtcNow.AddSeconds(5);

            using (PerformanceCollector performanceCollector = new PerformanceCollector(System.TimeSpan.FromMilliseconds(10),
                testHooks: new PerformanceCollector.TestHooks()
                {
                    AvailableDiskSpace = 10,
                    DelayedAvailableDiskSpace = 2,
                    DelayAvailableDiskSpaceUtcTime = delayedTime
                }))
            {
                RunScheduler(performanceCollector: performanceCollector, updateStatusTimerEnabled: true).AssertFailure();
                IgnoreWarnings();
                AssertErrorEventLogged(LogEventId.WorkerFailedDueToLowDiskSpace);
            }
        }

        [Feature(Features.UndeclaredAccess)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FailOnUnspecifiedInput(bool partialSealDirectory)
        {
            // Process depends on unspecified input
            var ops = new Operation[]
            {
                Operation.ReadFile(CreateSourceFile(), doNotInfer: true /* causes unspecified input */ ),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            if (partialSealDirectory)
            {
                // Create a graph with a Partial SealDirectory
                DirectoryArtifact dir = SealDirectory(SourceRootPath, SealDirectoryKind.Partial /* don't specify input */ );
                builder.AddInputDirectory(dir);
            }

            SchedulePipBuilder(builder);

            // Fail on unspecified input
            RunScheduler().AssertFailure();

            if (partialSealDirectory)
            {
                AssertVerboseEventLogged(LogEventId.DisallowedFileAccessInSealedDirectory);
            }
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess, allowMore: true);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CheckCleanTempDirDependingOnPipResult(bool shouldPipFail)
        {
            Configuration.Engine.CleanTempDirectories = true;

            var tempdir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            var tempdirStr = ArtifactToString(tempdir);
            var file = CreateOutputFileArtifact(tempdirStr);
            var fileStr = ArtifactToString(file);
            var operations = new List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.WriteFile(file, doNotInfer:true),
            };

            if (shouldPipFail)
            {
                operations.Add(Operation.Fail());
            }

            var builder = CreatePipBuilder(operations);
            builder.SetTempDirectory(tempdir);
            SchedulePipBuilder(builder);

            using (var tempCleaner = new TempCleaner(LoggingContext))
            {
                if (shouldPipFail)
                {
                    RunScheduler(tempCleaner: tempCleaner).AssertFailure();
                    AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
                    tempCleaner.WaitPendingTasksForCompletion();
                    XAssert.IsTrue(Directory.Exists(tempdirStr), $"TEMP directory deleted but wasn't supposed to: {tempdirStr}");
                    XAssert.IsTrue(File.Exists(fileStr), $"Temp file deleted but wasn't supposed to: {fileStr}");
                }
                else
                {
                    RunScheduler(tempCleaner: tempCleaner).AssertSuccess();
                    tempCleaner.WaitPendingTasksForCompletion();
                    XAssert.IsFalse(File.Exists(fileStr), $"Temp file not deleted: {fileStr}");
                    XAssert.IsFalse(Directory.Exists(tempdirStr), $"TEMP directory not deleted: {tempdirStr}");
                }
            }
        }

        [Feature(Features.UndeclaredAccess)]
        [Fact]
        public void FailOnUndeclaredOutput()
        {
            // Process depends on unspecified output
            var undeclaredOutFile = CreateOutputFileArtifact();
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.WriteFile(undeclaredOutFile, doNotInfer: true /* undeclared output */)
            });
            RunScheduler().AssertFailure();

            // Fail on unspecified output
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess, count: 1, allowMore: OperatingSystemHelper.IsUnixOS);
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void FailOnMissingOutput()
        {
            string missingFileName = "missing.txt";
            FileArtifact missingFileArtifact = CreateFileArtifactWithName(missingFileName, ObjectRoot);

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            // Declare /missingFileArtifact as output, even though the pip will not create /missingFileARtifact
            pipBuilder.AddOutputFile(missingFileArtifact, FileExistence.Required);
            Process pip = SchedulePipBuilder(pipBuilder).Process;

            // Fail on missing output
            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessMissingExpectedOutputOnCleanExit);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessExpectedMissingOutputs);
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
        }

        [Fact]
        public void WritingToStandardErrorFailsExecution()
        {
            // Schedule a pip that succeeds but writes to standard error
            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Echo("some message", useStdErr: true)
            });

            pipBuilder.Options |= Process.Options.WritingToStandardErrorFailsExecution;

            Process pip = SchedulePipBuilder(pipBuilder).Process;

            // Should fail since stderr was written
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessWroteToStandardErrorOnCleanExit);
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
        }

        [Fact]
        public void StandardErrorWithZeroLengthDoesNotFailExecution()
        {
            // Schedule a pip that does not write to stderr
            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            pipBuilder.Options |= Process.Options.WritingToStandardErrorFailsExecution;

            Process pip = SchedulePipBuilder(pipBuilder).Process;

            // Should succeed since stderr was not written
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void ValidateCachingCommandLineChange()
        {
            // Graph construction
            var outFile = CreateOutputFileArtifact();
            var consumerOutFile = CreateOutputFileArtifact();

            Process[] ResetAndConstructGraph(bool modifyCommandLine = false)
            {
                ResetPipGraphBuilder();

                var builder = CreatePipBuilder(new Operation[]
                {
                        Operation.WriteFile(outFile)
                });

                if (modifyCommandLine)
                {
                    // Adds an argument to the test process' command line that won't crash the test process
                    // and won't cause disallowed file access
                    var nonExistentDir = CreateOutputDirectoryArtifact();
                    builder.ArgumentsBuilder.Add(Operation.EnumerateDir(nonExistentDir).ToCommandLine(Context.PathTable));
                }

                Process pip1 = SchedulePipBuilder(builder).Process;

                // This pip consumes the output of pip1.
                var pip2 = CreateAndSchedulePipBuilder(new Operation[]
                {
                            Operation.ReadFile(outFile),
                            Operation.WriteFile(consumerOutFile)
                }).Process;

                return new Process[] { pip1, pip2 };
            }

            // Perform the builds
            var firstRun = ResetAndConstructGraph();
            RunScheduler().AssertCacheMiss(firstRun[0].PipId, firstRun[1].PipId);

            // Reset the graph and re-schedule the same pip to verify
            // the generating pip Ids is stable across graphs and gets a cache hit
            var secondRun = ResetAndConstructGraph();

            XAssert.AreEqual(firstRun[0].PipId, secondRun[0].PipId);
            RunScheduler().AssertCacheHit(firstRun[0].PipId, firstRun[1].PipId);

            // Reset the graph and re-schedule the same pip but with an added command line arg.
            // This should invalidate the pip with the change as well as the consuming pip
            var thirdRun = ResetAndConstructGraph(modifyCommandLine: true);

            // Make sure the new argument didn't churn the pip id
            XAssert.AreEqual(firstRun[0].PipId, thirdRun[0].PipId);
            RunScheduler().AssertCacheMiss(firstRun[0].PipId, firstRun[1].PipId);
        }

        [Fact]
        public void ValidateCachingEnvVarOrderChange()
        {
            // Graph construction
            var outFile = CreateOutputFileArtifact();
            var consumerOutFile = CreateOutputFileArtifact();

            Process ResetAndConstructGraph(bool modifyEnvVarOrder = false)
            {
                ResetPipGraphBuilder();

                var envVars = !modifyEnvVarOrder ?
                    new Dictionary<string, string>() { { "a", "A" }, { "b", "B" } } :
                    new Dictionary<string, string>() { { "b", "B" }, { "a", "A" } };

                var builder = CreatePipBuilder(new Operation[]
                {
                        Operation.WriteFile(outFile)
                }, null, null, envVars);

                return SchedulePipBuilder(builder).Process;
            }

            // Perform the builds
            var firstRun = ResetAndConstructGraph();
            RunScheduler().AssertCacheMiss(firstRun.PipId);

            // Reset the graph and re-schedule the same pip to verify
            // the generating pip Ids is stable across graphs and gets a cache hit
            var secondRun = ResetAndConstructGraph(true);
            RunScheduler().AssertCacheHit(secondRun.PipId);
        }

        [Fact]
        public void ValidateCachingDeletedOutput()
        {
            var outFile = CreateOutputFileArtifact();
            var pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(outFile)
            }).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete output file
            File.Delete(ArtifactToString(outFile));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Double check that cache replay worked
            XAssert.IsTrue(File.Exists(ArtifactToString(outFile)));
        }

        [Fact]
        public void ValidateCachingDisableCacheLookup()
        {
            var outFile = CreateOutputFileArtifact();
            var pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(outFile) });
            var pip = SchedulePipBuilder(pipBuilder).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Now mark the pip as having cache lookup disabled. We expect to get a miss each execution of the scheduler
            ResetPipGraphBuilder();
            pipBuilder.Options = Process.Options.DisableCacheLookup;
            pip = SchedulePipBuilder(pipBuilder).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheMiss(pip.PipId);
        }

        [Fact]
        public void SandboxDisabledPipIsCacheMiss()
        {
            var outFile = CreateOutputFileArtifact();
            var pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(outFile) });
            pipBuilder.Options = Process.Options.DisableSandboxing;

            var pip = SchedulePipBuilder(pipBuilder).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Unsandboxed pips are always misses
            RunScheduler().AssertCacheMiss(pip.PipId);
        }

        [Feature(Features.AbsentFileProbe)]
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void ValidateCachingAbsentFileProbes(bool sourceMount, bool partialSealDirectory)
        {
            // Set up absent file
            FileArtifact absentFile;
            AbsolutePath root;
            if (sourceMount)
            {
                // Read only mount
                absentFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix, ReadonlyRoot));
                AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out root);
            }
            else
            {
                // Read/write mount
                absentFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix, ObjectRoot));
                root = ObjectRootPath;
            }

            // Pip probes absent input and directory
            var ops = new Operation[]
            {
                Operation.Probe(absentFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            if (partialSealDirectory)
            {
                // Partially seal the directory containing absentFile
                DirectoryArtifact absentFileDir = SealDirectory(root, SealDirectoryKind.Partial, absentFile);
                builder.AddInputDirectory(absentFileDir);
            }

            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /absentFile
            WriteSourceFile(absentFile);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /absentFile
            var file = ArtifactToString(absentFile);
            File.WriteAllText(ArtifactToString(absentFile), System.Guid.NewGuid().ToString());
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Feature(Features.ExistingFileProbe)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingExistingFileProbes(bool sourceMount)
        {
            // Set up absent file
            FileArtifact fileProbe;
            AbsolutePath root;
            if (sourceMount)
            {
                // Read only mount
                fileProbe = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix, ReadonlyRoot));
                AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out root);
            }
            else
            {
                // Read/write mount
                fileProbe = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix, ObjectRoot));
                root = ObjectRootPath;
            }

            // Pip probes absent input and directory
            var ops = new Operation[]
            {
                Operation.Probe(fileProbe, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            // Partially seal the directory containing absentFile
            DirectoryArtifact absentFileDir = SealDirectory(root, SealDirectoryKind.Partial, fileProbe);
            builder.AddInputDirectory(absentFileDir);

            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /absentFile
            WriteSourceFile(fileProbe);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /absentFile
            var file = ArtifactToString(fileProbe);
            File.WriteAllText(ArtifactToString(fileProbe), System.Guid.NewGuid().ToString());
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Feature(Features.DirectoryProbe)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingAbsentDirectoryProbes(bool sourceMount)
        {
            // Set up absent directory
            DirectoryArtifact absentDirectory;
            if (sourceMount)
            {
                // Source mounts (i.e. read only mounts) use the actual filesystem and check the existence of files/directories on disk
                absentDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));
                Directory.Delete(ArtifactToString(absentDirectory)); // start with absent directory
            }
            else
            {
                // Output mounts (i.e. read/write mounts) use the graph filesystem and do not check the existence of files/directories on disk
                absentDirectory = CreateOutputDirectoryArtifact();
            }

            // Pip probes absent input and directory
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Probe(absentDirectory),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /absentDirectory
            Directory.CreateDirectory(ArtifactToString(absentDirectory));

            // Source mounts check the existence of files/directories on disk (so cache miss)
            // Output mounts do not check the existence of files/directories on disk (so cache hit)
            if (sourceMount)
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
            }

            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /absentDirectory/newFile
            CreateSourceFile(ArtifactToString(absentDirectory));
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// Tests whether a directory probe will cause an underbuild.
        /// </summary>
        /// <remarks>See bug 1816341 for more context on what is being tested here.</remarks>
        [Feature(Features.DirectoryProbe)]
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void ValidatePipUnderbuildWithDirectoryProbes(bool explicitlyReport)
        {
            Configuration.Sandbox.ExplicitlyReportDirectoryProbes = explicitlyReport;

            DirectoryArtifact directoryToProbe = CreateUniqueDirectoryArtifact(root: Path.Combine(SourceRoot, "Foo"));
            DirectoryArtifact dir = SealDirectory(directoryToProbe.Path.GetParent(Context.PathTable), SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.DirProbe(directoryToProbe),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(dir);

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            Directory.Delete(ArtifactToString(directoryToProbe));

            if (explicitlyReport)
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
                RunScheduler().AssertCacheHit(pip.PipId);
            }
            else
            {
                // Since detours does not report directory probes, we see a cache hit here even though we expect a cache miss (causing an underbuild)
                // See where the explicitReport variable is being calcuated in PolicyResult::CheckReadAccess to see why these are not reported
                RunScheduler().AssertCacheHit(pip.PipId);
            }
        }

        /// <summary>
        /// Tests that a directory probe will cause an underbuild if the probed directory is not under input directory.
        /// </summary>
        /// <remarks>See bug 2146397 for more context on what is being tested here.</remarks>
        [Feature(Features.DirectoryProbe)]
        [Fact]
        public void ValidatePipUnderbuildWithDirectoryProbesNotUnderInputDirectory()
        {
            Configuration.Sandbox.ExplicitlyReportDirectoryProbes = true;

            DirectoryArtifact directoryToProbe = CreateUniqueDirectoryArtifact(root: Path.Combine(SourceRoot, "Foo"));
            
            var builder = CreatePipBuilder(new[]
            {
                Operation.DirProbe(directoryToProbe),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            Directory.Delete(ArtifactToString(directoryToProbe));

            // Even when ExplicitlyReportDirectoryProbes is true, Detours only report existing directory probe if ReportAccessIfExistent is set in
            // the file access manifest. Such a flag is only set for directory dependencies.
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// Tests that a directory probe will not cause an underbuild if the probed directory is under input directory.
        /// </summary>
        /// <remarks>See bug 2146397 for more context on what is being tested here.</remarks>
        [Feature(Features.DirectoryProbe)]
        [Theory]
        [InlineData(SealDirectoryKind.SourceAllDirectories)]
        [InlineData(SealDirectoryKind.SourceTopDirectoryOnly)]
        [InlineData(SealDirectoryKind.Partial)]
        public void ValidateDirectoryProbesUnderInputDirectory(SealDirectoryKind kind)
        {
            Configuration.Sandbox.ExplicitlyReportDirectoryProbes = true;

            string rootDirectory = Path.Combine(SourceRoot, "Foo");
            AbsolutePath rootDirectoryPath = AbsolutePath.Create(Context.PathTable, rootDirectory);

            // Create directory to probe at SourceRoot/Foo/Bar/src_1
            DirectoryArtifact directoryToProbe = CreateUniqueDirectoryArtifact(root: Path.Combine(rootDirectory, "Bar"));
            FileArtifact file = CreateSourceFile(root: directoryToProbe.Path.GetParent(Context.PathTable));

            // Seal the directory SourceRoot/Foo.
            DirectoryArtifact dir = kind switch
            {
                SealDirectoryKind.SourceAllDirectories => SealDirectory(rootDirectoryPath, kind),
                SealDirectoryKind.SourceTopDirectoryOnly => SealDirectory(rootDirectoryPath, kind),
                SealDirectoryKind.Partial => SealDirectory(rootDirectoryPath, kind, file),
                _ => throw new ArgumentException("Invalid SealDirectoryKind")
            };

            var builder = CreatePipBuilder(new[]
            {
                Operation.DirProbe(directoryToProbe),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(dir);

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            Directory.Delete(ArtifactToString(directoryToProbe));

            // Even when ExplicitlyReportDirectoryProbes is true, Detours only report existing directory probe if ReportAccessIfExistent is set in
            // the file access manifest. Such a flag is only set for directory dependencies.

            if (kind == SealDirectoryKind.SourceAllDirectories)
            {
                // If it's under SourceAllDirectories, the observed input processor will treat the probe as an existing directory probe
                // because the directory is considered as a source directory.
                RunScheduler().AssertCacheMiss(pip.PipId);
                RunScheduler().AssertCacheHit(pip.PipId);
            }
            else
            {
                // Even if the directory itself is under a sealed directory, but because the directory is not part of that sealed directory (i.e.,
                // the sealed directory is only SourceTopDirectoryOnly or is only partial/full), the observed input processor will treat the probe as probing
                // an output directory, and determine the probe as non-existent (to avoid cache churn).
                RunScheduler().AssertCacheHit(pip.PipId);
            }
        }

        [Feature(Features.DirectoryProbe)]
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)] // we currently cannot simulate directory probes for macOS.
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateTreatAbsentDirectoryAsExistentUnderOpaque(bool disableTreatAbsentDirectoryAsExistentUnderOpaque)
        {
            if (disableTreatAbsentDirectoryAsExistentUnderOpaque)
            {
                // Only set it in case of disableTreatAbsentDirectoryAsExistentUnderOpaque,
                // so that we can also test the default behavior.
                Configuration.Schedule.TreatAbsentDirectoryAsExistentUnderOpaque = false;
            }

            // Pip1 probes a directory: "opaque/childDir".
            // Pip2 produces a file under a regular or shared opaque directory probe: "opaque/childDir/output".
            // If Pip1 executes before Pip2, then Pip1 has an AbsentPathProbe for probing "opaque/childDir.
            // In the next run, if Pip2 executes before Pip1, Pip1 has an ExistingDirectoryProbe for "opaque/childDir",
            // so it will get a miss because the parent directories of outputs are added to Output / Graph file system after Pip2 is executed.

            // Incremental scheduling does not seem compatible with constraintExecutionOrder feature
            Configuration.Schedule.IncrementalScheduling = false;

            string opaqueDir = Path.Combine(ObjectRoot, "opaqueDir");
            var dirUnderOpaque = CreateOutputDirectoryArtifact(opaqueDir);

            Process pip1 = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.DirProbe(dirUnderOpaque),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(dirUnderOpaque), doNotInfer: true)
            });

            builder.AddOutputDirectory(AbsolutePath.Create(Context.PathTable, opaqueDir), kind: SealDirectoryKind.Opaque);
            var pip2 = SchedulePipBuilder(builder).Process;

            RunScheduler(constraintExecutionOrder: new List<(Pip, Pip)>() { (pip1, pip2) }).AssertCacheMiss(pip1.PipId, pip2.PipId);

            var secondRunResult = RunScheduler(constraintExecutionOrder: new List<(Pip, Pip)>() { (pip2, pip1) });
            if (Configuration.Schedule.TreatAbsentDirectoryAsExistentUnderOpaque)
            {
                secondRunResult.AssertCacheHit(pip1.PipId, pip2.PipId);
            }
            else
            {
                // If pip2 executes before pip1, then dirUnderOpaque probe would be ExistingDirectoryProbe
                // instead of AbsentPathProbe. It would result in a cache miss.
                secondRunResult.AssertCacheMiss(pip1.PipId);
            }

            // This can be a miss if we do not preserve flags for ExistingDirectory probes.
            RunScheduler(constraintExecutionOrder: new List<(Pip, Pip)>() { (pip1, pip2) }).AssertCacheHit(pip1.PipId, pip2.PipId);

            // This is always a hit.
            RunScheduler(constraintExecutionOrder: new List<(Pip, Pip)>() { (pip2, pip1) }).AssertCacheHit(pip1.PipId, pip2.PipId);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { true, false },
            new object[] { true, false },
            new object[] { "", "*" })]
        public void ValidateCachingDirectoryEnumerationReadOnlyMount(bool logObservedFileAccesses, bool topLevelTest, string enumeratePattern)
        {
            Configuration.Sandbox.LogObservedFileAccesses = logObservedFileAccesses;

            AbsolutePath readonlyRootPath;
            AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out readonlyRootPath);
            DirectoryArtifact readonlyRootDir = DirectoryArtifact.CreateWithZeroPartialSealId(readonlyRootPath);
            DirectoryArtifact childDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));

            // Enumerate /readonlyroot and /readonlyroot/childDir
            FileArtifact outFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(readonlyRootDir, enumeratePattern: enumeratePattern),
                Operation.EnumerateDir(childDir, enumeratePattern: enumeratePattern),
                Operation.WriteFile(outFile)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint1 = File.ReadAllText(ArtifactToString(outFile));

            DirectoryArtifact targetDir = topLevelTest ? readonlyRootDir : childDir;

            // Create /targetDir/nestedFile
            FileArtifact nestedFile = CreateSourceFile(ArtifactToString(targetDir));
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /targetDir/nestedFile
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /targetDir/nestedFile
            File.Delete(ArtifactToString(nestedFile));
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint2 = File.ReadAllText(ArtifactToString(outFile));

            // Filesystem should match state from original run, so cache replays output from that run
            XAssert.AreEqual(checkpoint1, checkpoint2);

            // Create /targetDir/nestedDir
            AbsolutePath nestedDir = CreateUniqueDirectory(ArtifactToString(targetDir));
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create file /targetDir/nestedDir/doubleNestedFile
            var doubleNestedFile = CreateOutputFileArtifact(nestedDir.ToString(Context.PathTable));
            File.WriteAllText(ArtifactToString(doubleNestedFile), "doubleNestedFile");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete file /targetDir/nestedDir/doubleNestedFile
            File.Delete(ArtifactToString(doubleNestedFile));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /targetDir/nestedDir
            Directory.Delete(nestedDir.ToString(Context.PathTable));
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint3 = File.ReadAllText(ArtifactToString(outFile));

            // Filesystem should match state from original run, so cache replays output from that run
            XAssert.AreEqual(checkpoint1, checkpoint3);

            // Delete /targetDir
            if (topLevelTest)
            {
                // Delete /readonlyroot/childDir so /readonlyroot is empty
                Directory.Delete(ArtifactToString(childDir));
            }
            Directory.Delete(ArtifactToString(targetDir));
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void ValidateCachingDirectoryEnumerationWithFilterReadOnlyMount(bool logObservedFileAccesses)
        {
            Configuration.Sandbox.LogObservedFileAccesses = logObservedFileAccesses;

            AbsolutePath readonlyPath;
            AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out readonlyPath);
            DirectoryArtifact readonlyDir = DirectoryArtifact.CreateWithZeroPartialSealId(readonlyPath);

            // Enumerate /readonly
            FileArtifact outFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(readonlyDir, enumeratePattern: "*.txt"),
                Operation.EnumerateDir(readonlyDir, enumeratePattern: "fILe*.*"),
                Operation.WriteFile(outFile)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint1 = File.ReadAllText(ArtifactToString(outFile));

            // Create /readonly/a.txt
            FileArtifact aTxtFile = CreateFileArtifactWithName("a.txt", ReadonlyRoot);
            WriteSourceFile(aTxtFile);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /readonly/nestedfile.cpp
            FileArtifact nestedFile = CreateFileArtifactWithName("nestedfile.cpp", ReadonlyRoot);
            // This should not affect the fingerprint as we only care about '*.txt' and 'file*.*' files
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /readonly/file.cpp
            FileArtifact filecpp = CreateFileArtifactWithName("file.cpp", ReadonlyRoot);
            WriteSourceFile(filecpp);
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Delete /readonly/file.cpp but create /readonly/FILE.CPP
            File.Delete(ArtifactToString(filecpp));
            FileArtifact filecppUpperCase = CreateFileArtifactWithName("FILE.CPP", ReadonlyRoot);
            WriteSourceFile(filecppUpperCase);

            var result = RunScheduler();

            if (OperatingSystemHelper.IsPathComparisonCaseSensitive)
            {
                result.AssertCacheMiss(pip.PipId);
            }
            else
            {
                result.AssertCacheHit(pip.PipId);
            }

            // Modify /readonly/a.txt
            File.WriteAllText(ArtifactToString(aTxtFile), "aTxtFile");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /readonly/a.txt
            // Delete /readonly/FILE.CPP
            File.Delete(ArtifactToString(aTxtFile));
            File.Delete(ArtifactToString(filecppUpperCase));
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint2 = File.ReadAllText(ArtifactToString(outFile));

            // Filesystem should match state from original run, so cache replays output from that run
            XAssert.AreEqual(checkpoint1, checkpoint2);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // we currently cannot detect enumerate pattern with macOS sandbox
        public void ValidateCachingDirectoryEnumerationWithComplexFilterReadOnlyMount()
        {
            AbsolutePath readonlyPath;
            AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out readonlyPath);
            DirectoryArtifact readonlyDir = DirectoryArtifact.CreateWithZeroPartialSealId(readonlyPath);

            // Enumerate /readonly
            FileArtifact outFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(readonlyDir, enumeratePattern: "*.txt"),
                Operation.EnumerateDir(readonlyDir, enumeratePattern: "*.cs"),
                Operation.EnumerateDir(readonlyDir, enumeratePattern: "*.cpp"),
                Operation.WriteFile(outFile)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint1 = File.ReadAllText(ArtifactToString(outFile));

            // Create /readonly/a.txt
            FileArtifact aTxtFile = CreateFileArtifactWithName("a.txt", ReadonlyRoot);
            WriteSourceFile(aTxtFile);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /readonly/a.cs
            FileArtifact aCsFile = CreateFileArtifactWithName("a.cs", ReadonlyRoot);
            WriteSourceFile(aCsFile);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /readonly/a.h
            FileArtifact aHFile = CreateFileArtifactWithName("a.h", ReadonlyRoot);
            WriteSourceFile(aHFile);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create /readonly/a.cpp
            FileArtifact aCppFile = CreateFileArtifactWithName("a.cpp", ReadonlyRoot);
            WriteSourceFile(aCppFile);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /readonly/a.txt
            File.WriteAllText(ArtifactToString(aTxtFile), "aTxtFile");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /readonly/a.txt
            File.Delete(ArtifactToString(aTxtFile));
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create files which are only partial matches for the enumeration filters and thus
            // should be ignored since the filter should match the full string.
            // Create /readonly/a.cs.miss
            // Create /readonly/b.cpp.1
            // Create /readonly/c.cpp.txt.ext
            WriteSourceFile(CreateFileArtifactWithName("a.cs.miss", ReadonlyRoot));
            WriteSourceFile(CreateFileArtifactWithName("b.cpp.1", ReadonlyRoot));
            WriteSourceFile(CreateFileArtifactWithName("c.cpp.txt.ext", ReadonlyRoot));
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { true, false },
            new object[] { "*", "*.txt" })]
        public void ValidateCachingDirectoryEnumerationReadWriteMount(bool logObservedFileAccesses, string enumeratePattern)
        {
            Configuration.Sandbox.LogObservedFileAccesses = logObservedFileAccesses;

            var outDir = CreateOutputDirectoryArtifact();
            var outDirStr = ArtifactToString(outDir);
            Directory.CreateDirectory(outDirStr);

            // PipA enumerates directory \outDir, creates file \outDir\outA
            var outA = CreateOutputFileArtifact(outDirStr);
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                // Enumerate pattern does not matter in the ReadWrite mount because we use the graph based enumeration.
                Operation.EnumerateDir(outDir, enumeratePattern: enumeratePattern),
                Operation.WriteFile(outA)
            }).Process;

            // PipB consumes directory \outA, creates file \outDir\outB
            var outB = CreateOutputFileArtifact(outDirStr);
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(outA),
                Operation.WriteFile(outB)
            }).Process;

            RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);
            RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Delete files in enumerated directory
            File.Delete(ArtifactToString(outA));
            File.Delete(ArtifactToString(outB));

            RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Double check that cache replay worked
            XAssert.IsTrue(File.Exists(ArtifactToString(outA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outB)));
        }

        [Fact]
        [Feature(Features.RewrittenFile)]
        public void ValidateCachingRewrittenFiles()
        {
            // Three pips sharing the same file as input and rewriting it.
            // Each pip has a unique src file that is modified to trigger that individual pip to re-run.
            // When a pip is run, it reads in the shared file, then appends one line of random output.
            // The random output triggers the subsequent, dependent pips to re-run.

            var srcA = CreateSourceFile();
            var rewrittenFile = CreateOutputFileArtifact();
            var paoA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcA),
                Operation.WriteFile(rewrittenFile), /* unique output on every pip run */
                Operation.WriteFile(rewrittenFile, System.Environment.NewLine)
            }, null, "ProcessA");
            Process pipA = paoA.Process;

            var srcB = CreateSourceFile();
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcB),
                Operation.WriteFile(rewrittenFile, doNotInfer: true), /* unique output on every pip run */
                Operation.WriteFile(rewrittenFile, System.Environment.NewLine, doNotInfer: true)
            }, null, "ProcessB");
            pipBuilderB.AddRewrittenFileInPlace(paoA.ProcessOutputs.GetOutputFile(rewrittenFile.Path));
            var paoB = SchedulePipBuilder(pipBuilderB);
            Process pipB = paoB.Process;

            var srcC = CreateSourceFile();
            var pipBuilderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcC),
                Operation.WriteFile(rewrittenFile, doNotInfer: true), /* unique output on every pip run */
                Operation.WriteFile(rewrittenFile, System.Environment.NewLine, doNotInfer: true)
            }, null, "ProcessC");
            pipBuilderC.AddRewrittenFileInPlace(paoB.ProcessOutputs.GetOutputFile(rewrittenFile.Path));
            Process pipC = SchedulePipBuilder(pipBuilderC).Process;

            RunScheduler().AssertSuccess();

            // Modify input to pipA to trigger re-run, cache miss on all three
            File.AppendAllText(ArtifactToString(srcA), "srcA");
            ScheduleRunResult resultA = RunScheduler().AssertSuccess();
            resultA.AssertCacheMiss(pipA.PipId);
            resultA.AssertCacheMiss(pipB.PipId);
            resultA.AssertCacheMiss(pipC.PipId);

            // Store results of file to check cache replay
            var outRunA = File.ReadAllLines(ArtifactToString(rewrittenFile));
            XAssert.AreEqual(3, outRunA.Length); /* exactly one line of output per pip */

            // Modify input to pipB to trigger re-run, cache miss on B and C
            File.AppendAllText(ArtifactToString(srcB), "srcB");
            ScheduleRunResult resultB = RunScheduler().AssertSuccess();
            resultB.AssertCacheHit(pipA.PipId);
            resultB.AssertCacheMiss(pipB.PipId);
            resultB.AssertCacheMiss(pipC.PipId);

            // Check cache hit from resultB replayed version of file written only by pipA
            var outRunB = File.ReadAllLines(ArtifactToString(rewrittenFile));
            XAssert.AreEqual(3, outRunB.Length); /* exactly one line of output per pip */

            XAssert.AreEqual(outRunA[0], outRunB[0]);
            XAssert.AreNotEqual(outRunA[1], outRunB[1]);
            XAssert.AreNotEqual(outRunA[2], outRunB[2]);

            // Modify input to pipC to trigger re-run, cache miss on only C
            File.AppendAllText(ArtifactToString(srcC), "srcC");
            ScheduleRunResult resultC = RunScheduler().AssertSuccess();
            resultC.AssertCacheHit(pipA.PipId);
            resultC.AssertCacheHit(pipB.PipId);
            resultC.AssertCacheMiss(pipC.PipId);

            // Check cache hit from resultC replayed version of file run written only by pipA and pipB
            var outRunC = File.ReadAllLines(ArtifactToString(rewrittenFile));
            XAssert.AreEqual(outRunA.Length, 3); /* exactly one line of output per pip */

            XAssert.AreEqual(outRunB[0], outRunC[0]);
            XAssert.AreEqual(outRunB[1], outRunC[1]);
            XAssert.AreNotEqual(outRunB[2], outRunC[2]);
        }

        [Fact]
        public void DirectoryEnumerationUnderWritableMount()
        {
            var sealDirectoryPath = CreateUniqueDirectory(ObjectRoot);
            var path = sealDirectoryPath.ToString(Context.PathTable);

            DirectoryArtifact dir = SealDirectory(sealDirectoryPath, SealDirectoryKind.Partial, CreateSourceFile(path));

            var ops = new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            Process pip = SchedulePipBuilder(builder).Process;

            var result = RunScheduler().AssertSuccess();
            result.AssertObservation(pip.PipId, new ObservedPathEntry(sealDirectoryPath, false, true, true, RegexDirectoryMembershipFilter.AllowAllRegex, false));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DirectoryWritesNotReportedAsObservations(bool underOpaque)
        {
            FileOrDirectoryArtifact dirA;
            ProcessBuilder pip1;
            if (underOpaque)
            {
                // PipA creates and removes a directory
                var sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
                var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
                dirA = CreateOutputFileArtifact(sharedOpaqueDir);
                pip1 = CreatePipBuilder(new Operation[]
                                                       {
                                                       Operation.CreateDir(dirA, doNotInfer: true),
                                                       Operation.DeleteDir(dirA, doNotInfer: true),
                                                       });
                pip1.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            }
            else
            {
                // PipA creates a directory
                dirA = FileOrDirectoryArtifact.Create(DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniquePath("outputDir", ObjectRoot)));
                pip1 = CreatePipBuilder(new Operation[]
                {
                    Operation.CreateDir(dirA),
                    Operation.WriteFile(CreateOutputFileArtifact()),
                });
            }

            Process pip = SchedulePipBuilder(pip1).Process;

            var result = RunScheduler().AssertSuccess();

            var observations = result.PathSets[pip.PipId];
            XAssert.IsTrue(observations.HasValue);
            XAssert.IsFalse(observations.Value.Paths.Any(obs => obs.Path == dirA.Path));
        }

        [Fact]
        public void ValidateCreatingDirectoryRetracksDirectoriesNeededForTrackedChildAbsentPaths()
        {
            // Set up absent file
            AbsolutePath readOnlyRoot = AbsolutePath.Create(Context.PathTable, ReadonlyRoot);

            DirectoryArtifact dir = SealDirectory(readOnlyRoot, SealDirectoryKind.SourceAllDirectories);

            // Pip probes absent input and directory
            var ops = new Operation[]
            {
                ProbeOp(ReadonlyRoot),
                ProbeOp(ReadonlyRoot, @"dir1\a\dir1_a.txt"),
                ProbeOp(ReadonlyRoot, @"dir1\b\dir1_b.txt"),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            builder.AddInputDirectory(dir);

            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);

            // Create probed file path which with a parent directory
            // which is also a parent of another absent path probe
            CreateDir(ReadonlyRoot, @"dir1");
            CreateDir(ReadonlyRoot, @"dir1\a");
            CreateFile(ReadonlyRoot, @"dir1\a\dir1_a.txt");

            RunScheduler().AssertCacheMiss(pip.PipId);

            // Now validate that creating a file at the location of
            // the other absent path probe invalidates the pip
            // In the original bug, the parent directory becomes untracked such that
            // changes to the directory go unnoticed
            CreateDir(ReadonlyRoot, @"dir1");
            CreateDir(ReadonlyRoot, @"dir1\b");
            CreateFile(ReadonlyRoot, @"dir1\b\dir1_b.txt");

            RunScheduler().AssertCacheMiss(pip.PipId);
        }

        [Fact]
        public void SearchPathEnumerationTool()
        {
            var sealDirectoryPath = CreateUniqueDirectory(ObjectRoot);
            var path = sealDirectoryPath.ToString(Context.PathTable);
            var nestedFile = CreateSourceFile(path);

            DirectoryArtifact dir = SealDirectory(sealDirectoryPath, SealDirectoryKind.Partial, nestedFile);
            Configuration.SearchPathEnumerationTools = new List<RelativePath>
            {
                RelativePath.Create(Context.StringTable, TestProcessToolName)
            };

            var ops = new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.ReadFile(nestedFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            Process pip = SchedulePipBuilder(builder).Process;

            var result = RunScheduler().AssertSuccess();

            result.AssertObservation(pip.PipId, new ObservedPathEntry(sealDirectoryPath, true, true, true, RegexDirectoryMembershipFilter.AllowAllRegex, false));

            // Make a change in the directory content, but it should not cause a cache miss due to the searchpathenumeration optimization.
            CreateSourceFile(path);

            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// This test goes back to "Bug #1343546: ObservedInputProcessor;ProcessInternal: If the access is a file content read, then the FileContentInfo cannot be null"
        /// </summary>
        /// <remarks>
        /// Scenario:
        /// - The tool enumerates C:\D1\D2\f.txt\*, where C:\D1\D2\f.txt doesn't exist (if it exists, it should exists as a file). Sandbox (particularly Detours) categorizes this activity as enumeration.
        /// - After the enumeration the file C:\D1\D2\f.txt appears.
        /// - Observed input runs sees it as enumerating file C:\D1\D2\f.txt, and is unable to handle that, which in turn throws a contract exception.
        /// </remarks>
        [Fact]
        public void ToolEnumeratingFile()
        {
            var sealDirectoryPath = CreateUniqueDirectory(ObjectRoot);
            var path = sealDirectoryPath.ToString(Context.PathTable);
            var nestedFile1 = CreateSourceFile(path);
            var nestedFile2 = CreateUniquePath("nonExistentFile", path);
            DirectoryArtifact dir = SealDirectory(sealDirectoryPath, SealDirectoryKind.SourceAllDirectories);
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;

            var ops = new Operation[]
            {
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(nestedFile2), doNotInfer: true, enumeratePattern: "*"),
                Operation.ReadFile(nestedFile1, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()),

                // This write will be allowed because UnexpectedFileAccessesAreErrors = false.
                // But this will make the above reporting of enumerating non-existing file as enumerating existing file.
                Operation.WriteFile(FileArtifact.CreateOutputFile(nestedFile2), doNotInfer: true)
            };

            var builder = CreatePipBuilder(ops);
            builder.AddInputDirectory(dir);
            Process pip = SchedulePipBuilder(builder).Process;

            var result = RunScheduler().AssertSuccess();

            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);
            AssertWarningEventLogged(LogEventId.FileMonitoringWarning, count: 1);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // WriteFile operation failed on MacOS and Linux; need further investigation.
        public void MoveDirectory()
        {
            // Create \temp.
            var tempDirectoryPath = CreateUniqueDirectory(root: ObjectRoot, prefix: "temp");

            // Create \temp\out.
            var tempOutputDirectoryPath = CreateUniqueDirectory(root: tempDirectoryPath, prefix: "out");

            // Specify \temp\out\f.txt.
            FileArtifact tempOutput = FileArtifact.CreateOutputFile(tempOutputDirectoryPath.Combine(Context.PathTable, "f.txt"));

            // Specify \final.
            DirectoryArtifact finalDirectory = CreateOutputDirectoryArtifact(ObjectRoot);

            // Specify \final\out.
            DirectoryArtifact finalOutputDirectory = CreateOutputDirectoryArtifact(finalDirectory.Path.ToString(Context.PathTable));

            // Specify \final\out\f.txt.
            AbsolutePath finalOutput = finalOutputDirectory.Path.Combine(Context.PathTable, "f.txt");

            var ops = new Operation[]
            {
                // Write to \temp\out\f.txt.
                Operation.WriteFile(tempOutput, content: "Hello", doNotInfer: true),

                // Move \temp\out to \final\out.
                Operation.MoveDir(DirectoryArtifact.CreateWithZeroPartialSealId(tempOutputDirectoryPath), finalOutputDirectory),
            };

            var builder = CreatePipBuilder(ops);

            // Untrack \temp.
            builder.AddUntrackedDirectoryScope(tempDirectoryPath);

            // Specify \final as opaque directory.
            builder.AddOutputDirectory(finalDirectory.Path, SealDirectoryKind.Opaque);

            // Specify explicitly \final\out\f.txt as output.
            builder.AddOutputFile(finalOutput);

            SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
        }

        /// <summary>
        /// This test shows our limitation in supporting MoveDirectory.
        /// </summary>
        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)] // WriteFile operation failed on MacOS; need further investigation.
        public void MoveDirectoryFailed()
        {
            // With EBPF we don't have blocking capabilities.
            if (UsingEBPFSandbox)
            {
                return;
            }

            // Create \temp.
            var tempDirectoryPath = CreateUniqueDirectory(root: ObjectRoot, prefix: "temp");

            // Create \temp\out.
            var tempOutputDirectoryPath = CreateUniqueDirectory(root: tempDirectoryPath, prefix: "out");

            // Specify \temp\out\f.txt.
            FileArtifact tempOutput = FileArtifact.CreateOutputFile(tempOutputDirectoryPath.Combine(Context.PathTable, "f.txt"));

            // Specify \finalOut.
            DirectoryArtifact finalDirectory = CreateOutputDirectoryArtifact(ObjectRoot);

            // Specify \finalOut\f.txt.
            AbsolutePath finalOutput = finalDirectory.Path.Combine(Context.PathTable, "f.txt");

            var ops = new Operation[]
            {
                // Write to \temp\out\f.txt.
                Operation.WriteFile(tempOutput, content: "Hello", doNotInfer: true),

                // Move \temp\out to \finalOut
                Operation.MoveDir(DirectoryArtifact.CreateWithZeroPartialSealId(tempOutputDirectoryPath), finalDirectory),
            };

            var builder = CreatePipBuilder(ops);

            // Untrack \temp.
            builder.AddUntrackedDirectoryScope(tempDirectoryPath);

            // Specify \finalOut\f.txt as output.
            builder.AddOutputFile(finalOutput);

            SchedulePipBuilder(builder);

            // This test failed because in order to move \temp\out to \finalOut, \finalOut needs to be wiped out first.
            // However, to wipe out \finalOut, one needs to have a write access to \finalOut. Currently, we only have create-directory access
            // to \finalOut.
            RunScheduler().AssertFailure();

            // Error DX0064: The test process failed to execute an operation: MoveDir (access denied)
            // Error DX0500: Disallowed write access.
            SetExpectedFailures(2, 0);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void LimitPathSetsOnCacheLookup(bool limitPathSets)
        {
            const int MaxPathSetsOnCacheLookup = 2;
            Configuration.Cache.MaxPathSetsOnCacheLookup = limitPathSets ? MaxPathSetsOnCacheLookup : 0;

            var ssdRoot = CreateUniqueDirectory(root: SourceRoot, prefix: "ssd");

            var fileInSSD = CreateSourceFile(root: ssdRoot, prefix: "file");
            var fileInSSD0 = CreateSourceFile(root: ssdRoot, prefix: "file0");
            var fileInSSD1 = CreateSourceFile(root: ssdRoot, prefix: "file1");
            var fileInSSD2 = CreateSourceFile(root: ssdRoot, prefix: "file2");
            var fileInSSD3 = CreateSourceFile(root: ssdRoot, prefix: "file3");
            var ssd = CreateAndScheduleSealDirectory(ssdRoot, SealDirectoryKind.SourceAllDirectories, fileInSSD, fileInSSD0, fileInSSD1, fileInSSD2, fileInSSD3);

            var untrackedFile = CreateSourceFileWithPrefix(root: SourceRoot, prefix: "untrackedFile");
            var untrackedFilePath = untrackedFile.Path.ToString(Context.PathTable);

            var pipBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(fileInSSD, doNotInfer: true),

                // Create non-deterministic operations using untracked file so that the pip can have multiple unique path sets.
                Operation.ReadFileIfInputEqual(fileInSSD0, untrackedFilePath, "0", doNotInfer: true),
                Operation.ReadFileIfInputEqual(fileInSSD1, untrackedFilePath, "1", doNotInfer: true),
                Operation.ReadFileIfInputEqual(fileInSSD2, untrackedFilePath, "2", doNotInfer: true),
                Operation.ReadFileIfInputEqual(fileInSSD3, untrackedFilePath, "3", doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });
            pipBuilder.AddInputDirectory(ssd.Directory);
            pipBuilder.AddUntrackedFile(untrackedFile);

            Process pip = SchedulePipBuilder(pipBuilder).Process;

            int i = 0;
            for (; i < MaxPathSetsOnCacheLookup + 1; i++)
            {
                WriteSourceFile(fileInSSD, $"content-{i}");
                WriteSourceFile(untrackedFile, i.ToString());
                RunScheduler(runNameOrDescription: $"Run-{i}").AssertCacheMiss(pip.PipId);
            }

            WriteSourceFile(fileInSSD, "content-0");
            WriteSourceFile(untrackedFile, i.ToString());

            var runResult = RunScheduler(runNameOrDescription: "Run-final");

            if (!limitPathSets)
            {
                runResult.AssertCacheHit(pip.PipId);
            }
            else
            {
                runResult.AssertCacheMiss(pip.PipId);
            }

            AssertVerboseEventLogged(LogEventId.TwoPhaseReachMaxPathSetsToCheck, limitPathSets ? 1 : 0);
        }

        /// <summary>
        /// Test to validate that global passthrough environment variables are visible to processes
        /// </summary>
        [Fact]
        public void GlobalPassthroughEnvironmentVariables()
        {
            string passedEnvironmentVariable = "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty);
            string passedOriginalValue = "TestValue";
            string passedUpdatedValue = "SomeOtherValue";
            string unpassedEnvironmentVariable = "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty);
            string unpassedValue = "UnpassedValue";

            Environment.SetEnvironmentVariable(passedEnvironmentVariable, passedOriginalValue);
            Environment.SetEnvironmentVariable(unpassedEnvironmentVariable, unpassedValue);
            Configuration.Sandbox.GlobalUnsafePassthroughEnvironmentVariables = new List<string>() { passedEnvironmentVariable };
            Configuration.Sandbox.OutputReportingMode = global::BuildXL.Utilities.Configuration.OutputReportingMode.FullOutputAlways;

            var ops = new Operation[]
            {
                Operation.ReadEnvVar(passedEnvironmentVariable),
                Operation.ReadEnvVar(unpassedEnvironmentVariable),
                Operation.ReadEnvVar(BuildParameters.RelatedSessionIdVariableName),
                Operation.WriteFile(CreateOutputFileArtifact()),
            };

            var builder = CreatePipBuilder(ops);
            builder.SetPassthroughEnvironmentVariable(StringId.Create(Context.StringTable, BuildParameters.RelatedSessionIdVariableName));
            builder.Options |= Process.Options.RequireGlobalDependencies;
            var process = SchedulePipBuilder(builder).Process;

            var result = RunScheduler().AssertSuccess();
            var pipProcessOutputEvents = EventListener.GetLogMessagesForEventId((int)ProcessesLogEventId.PipProcessOutput);
            XAssert.AreEqual(1, pipProcessOutputEvents.Length, "Should have one pip process output event");
            string log = pipProcessOutputEvents[0];
            XAssert.IsTrue(log.Contains(result.Session.RelatedId));
            XAssert.IsTrue(log.Contains(passedOriginalValue));
            XAssert.IsFalse(log.Contains(unpassedValue));

            // We should get a cache hit even if the value changes.
            Environment.SetEnvironmentVariable(passedEnvironmentVariable, passedUpdatedValue);
            RunScheduler().AssertCacheHit(process.PipId);
        }

        /// <summary>
        /// Test to validate that global passthrough environment variables are visible to processes
        /// </summary>
        [Theory]
        [InlineData(true, "local-test-value")]
        [InlineData(true, null)]
        [InlineData(false, "local-test-value")]
        public void PerVariablePassThroughIsHonored(bool isPassThrough, string value)
        {
            string envVarName = "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty);
            string envVarValue = "env-test-value";
            string updatedValue = Guid.NewGuid().ToString();

            Environment.SetEnvironmentVariable(envVarName, envVarValue);

            Configuration.Sandbox.OutputReportingMode = global::BuildXL.Utilities.Configuration.OutputReportingMode.FullOutputAlways;

            var ops = new Operation[]
            {
                Operation.ReadEnvVar(envVarName),
                Operation.WriteFile(CreateOutputFileArtifact()),
            };

            // The value we pass to the pip builder for the given env var can be null, which means that it will be left unset and the environment will take effect
            var builder = CreatePipBuilderWithEnvironment(ops, environmentVariables: new Dictionary<string, (string, bool)>() { [envVarName] = (value, isPassThrough) });
            var process = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertSuccess();
            string log = EventListener.GetLog();
            // The value the process sees needs to be the one we set from the outside
            XAssert.IsTrue(log.Contains(value ?? envVarValue));

            // Let's change the value on the environment and the corresponding one we pass to the pip
            Environment.SetEnvironmentVariable(envVarName, updatedValue);

            ResetPipGraphBuilder();
            builder = CreatePipBuilderWithEnvironment(ops, environmentVariables: new Dictionary<string, (string, bool)>() { [envVarName] = (updatedValue, isPassThrough) });
            process = SchedulePipBuilder(builder).Process;

            // We should only get a cache hit when the variable is passthrough
            if (isPassThrough)
            {
                RunScheduler().AssertCacheHit(process.PipId);
            }
            else
            {
                RunScheduler().AssertCacheMiss(process.PipId);
            }
        }

#if (MICROSOFT_INTERNAL && NETCOREAPP)
        /// <summary>
        /// This test to ensure that CredentialScanner detects the credentials when passed in environment variables and logs a warning in this case.
        /// Most of these test cases are an assumption of what the credentialscanner might or might not detect as a credential.
        /// </summary>
        [InlineData(false, "src/checking/for/credentials/123/testing", "path", false)]
        [InlineData(true, null, "password", false)]
        [InlineData(false, "123", "id", false)]
        [Theory]
        public void TestCredScan(bool isPassThrough, string envVarValue, string envVarKey, bool expectCredentialDetected, bool setValueOnPip = true)
        {
            Configuration.FrontEnd.EnableCredScan = true;
            ResetPipGraphBuilder();
            Environment.SetEnvironmentVariable(envVarKey, envVarValue);
            var ops = new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                // Echo out the environment variable value so the test can ensure it was visible to the process pip
                Operation.ReadEnvVar(envVarKey),
            };

            // The value we pass to the pip builder for the given env var can be null, which means that it will be left unset and the environment will take effect
            var builder = CreatePipBuilderWithEnvironment(ops, environmentVariables: new Dictionary<string, (string, bool)>() { [envVarKey] = (setValueOnPip ? envVarValue : null, isPassThrough) });
            SchedulePipBuilder(builder);
            Configuration.Sandbox.OutputReportingMode = OutputReportingMode.FullOutputAlways;

            // This event is logged when a credential is detected in the env variables.
            var result = expectCredentialDetected ? RunScheduler().AssertFailure() : RunScheduler().AssertSuccess();
            
            // This event is logged when a credential is detected in the env variables.
            AssertErrorEventLogged(PipsTracingLogEventId.CredentialsDetectedInEnvVar, expectCredentialDetected ? 1 : 0);
            if (expectCredentialDetected)
            {
                AssertLogContains(caseSensitive: true, [$"The following environment variables - '{envVarKey}' either need to be removed or made passthrough"]);
            }
            else if (envVarValue != null)
            {
                // Verify that the process actuall saw the env var
                AssertLogContains(caseSensitive: false, envVarValue);
            }
        }

        [Fact]
        public void TestCredScanValidCredGetsFlagged()
        {
            TestCredScan(isPassThrough: false, envVarValue: GetExampleCredentialForTest(), envVarKey: "id", expectCredentialDetected: true);
        }

        /// <summary>
        /// Retrieves an example PAT for testing
        /// </summary>
        private static string GetExampleCredentialForTest()
        {
            return new Microsoft.Security.Utilities.AzureDevOpsLegacyCommonAnnotatedSecurityKeyPat().GenerateTruePositiveExamples().First();
        }

        [Fact]
        public void TestCredScanValidCredGetsFlaggedEvenAsPassthrough()
        {
            // Somewhat counterintuitively, variables are still flagged even when they are set as "PassThrough". There
            // two notions of pass through:
            // 1. the variables do not participate in caching
            // 2. The variables do not participate in cache and they are not specified in the pip graph itself and are only
            //    available if set externally in the environment. 
            // Environment variables with secrets must fall into bucket 2, otherwise they would exist in the binary serialized pip graph files.
            TestCredScan(isPassThrough: true, envVarValue: GetExampleCredentialForTest(), envVarKey: "id", expectCredentialDetected: true);
        }

        [Fact]
        public void TestCredScanValidCredsAllowedWhenUnspecifiedInPip()
        {
            TestCredScan(isPassThrough: true, envVarValue: GetExampleCredentialForTest(), envVarKey: "id", expectCredentialDetected: false, setValueOnPip: false);
        }

        /// <summary>
        /// /credScanEnvironmentVariablesAllowList flag allows the user to pass envVars which needs to be skipped by the CredScan library.
        /// This test used to test if that functionality is working or not.
        /// The test is disabled due to a couple of DFA's related to Office builds
        /// </summary>
        [Fact]
        public void TestCredScanWithAllowListEnvVars()
        {
            string envVarKey = "password";
            Configuration.FrontEnd.CredScanEnvironmentVariablesAllowList = new List<string>() { envVarKey };
            Configuration.FrontEnd.EnableCredScan = true;
            ResetPipGraphBuilder();

            var ops = new Operation[]
            {
              Operation.WriteFile(CreateOutputFileArtifact()),
            };
            var builder = CreatePipBuilderWithEnvironment(ops, environmentVariables: new Dictionary<string, (string, bool)>() { [envVarKey] = (GetExampleCredentialForTest(), false) });
            SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            // This event should not be logged when environment variable is passed via /credScanEnvironmentVariablesAllowList.
            AssertErrorEventLogged(PipsTracingLogEventId.CredentialsDetectedInEnvVar, 0);
        }
#endif
        /// <summary>
        /// This tests fails on the first retry with a DFA and then succeeds on the second retry.
        /// The test aims to record all the DFA's from all the reties.
        /// </summary>
        [Fact]
        public void ReportDFAWhenRetry()
        {
            Configuration.Schedule.ProcessRetries = 1;

            // The stateFile here is used to keep a track of number of retries left.
            // While the file that is to be read is not give the required readAccess, hence it is expected to create DFA.
            AbsolutePath stateFilePath = ObjectRootPath.Combine(Context.PathTable, "stateFile.txt");
            FileArtifact stateFile = FileArtifact.CreateOutputFile(stateFilePath);

            // ReadFileIfInputEqual operations tries to read a file with no read access.
            // Using SucceedOnRetry operation we ensure that the source file "read.txt" is read on the first retry, but is not read during the last attempt.
            var ops = new Operation[]
            {
                Operation.WriteFile(FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "out.txt")), content: "Hello"),
                Operation.ReadFileIfInputEqual(CreateSourceFile(ObjectRootPath.Combine(Context.PathTable, "read.txt")), stateFilePath.ToString(Context.PathTable), "1", doNotInfer: true),
                Operation.SucceedOnRetry(untrackedStateFilePath: stateFile, failExitCode: 42)
            };

            var builder = CreatePipBuilder(ops);
            builder.RetryExitCodes = global::BuildXL.Utilities.Collections.ReadOnlyArray<int>.From(new int[] { 42 });
            builder.AddUntrackedFile(stateFile.Path);
            SchedulePipBuilder(builder);

            var result = RunScheduler();
            result.AssertFailure();
            IgnoreWarnings();
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        /// <summary>
        /// Validates behavior with a process being retried
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetryExitCodes(bool succeedOnRetry)
        {
            Configuration.Schedule.ProcessRetries = 1;
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            var ops = new Operation[]
            {
                Operation.WriteFile(FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "out.txt")), content: "Hello"),
                succeedOnRetry ?
                    Operation.SucceedOnRetry(untrackedStateFilePath: stateFile, failExitCode: 42) :
                    Operation.Fail(-2),
            };

            var builder = CreatePipBuilder(ops);
            builder.RetryExitCodes = global::BuildXL.Utilities.Collections.ReadOnlyArray<int>.From(new int[] { 42 });
            builder.AddUntrackedFile(stateFile.Path);
            SchedulePipBuilder(builder);

            var result = RunScheduler();
            if (succeedOnRetry)
            {
                result.AssertSuccess();
            }
            else
            {
                result.AssertFailure();
                SetExpectedFailures(1, 0);
            }
        }

        [Fact]
        public void PipProcessRetriesOverridesGlobalConfiguration()
        {
            // Schedule a pip that only succeeds on retry
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            var ops = new Operation[]
            {
                Operation.WriteFile(FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "out.txt")), content: "Hello"),
                Operation.SucceedOnRetry(untrackedStateFilePath: stateFile, failExitCode: 42)
            };

            var builder = CreatePipBuilder(ops);
            builder.RetryExitCodes = global::BuildXL.Utilities.Collections.ReadOnlyArray<int>.From(new int[] { 42 });
            builder.AddUntrackedFile(stateFile.Path);

            // The global configuration is set so no retries happen
            Configuration.Schedule.ProcessRetries = 0;
            // However, this specific pip is set to have retry number = 1
            builder.SetProcessRetries(1);

            SchedulePipBuilder(builder);

            // The pip should be retried, and therefore it should be successful
            RunScheduler().AssertSuccess();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void RetryAttemptEnvironmentVariableIsHonored(int processRetry)
        {
            // Schedule a pip that only succeeds after processRetry times and sends to stdout the retry attempt number
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            var ops = new Operation[]
            {
                // Reads RETRY_NUMBER and echoes it to stdout
                Operation.ReadEnvVar("RETRY_NUMBER"),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedOnRetry(untrackedStateFilePath: stateFile, failExitCode: 42, numberOfRetriesToSucceed: processRetry)
            };

            FileArtifact standardOutput = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(ops);
            builder.RetryExitCodes = global::BuildXL.Utilities.Collections.ReadOnlyArray<int>.From(new int[] { 42 });
            builder.AddUntrackedFile(stateFile.Path);

            builder.SetRetryAttemptEnvironmentVariable(StringId.Create(Context.StringTable, "RETRY_NUMBER"));
            builder.SetProcessRetries(processRetry);
            builder.SetStandardOutputFile(standardOutput);

            SchedulePipBuilder(builder);

            // The pip should be retried, and therefore it should be successful
            RunScheduler().AssertSuccess();

            // Let's validate the env var value matches the retry number
            var output = File.ReadAllText(standardOutput.Path.ToString(Context.PathTable));
            XAssert.AreEqual(processRetry, int.Parse(output));
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(2)]
        [InlineData(7)]
        public void RetryPipForProcessExitingWithAzWatsonDeadExitCode(int numOfRetriesBeforeSucceed)
        {
            Configuration.Sandbox.RetryOnAzureWatsonExitCode = true;

            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));

            var ops = new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedOnRetry(stateFile,(int)SandboxedProcessPipExecutor.AzureWatsonExitCode, numOfRetriesBeforeSucceed)
            };
            var builder = CreatePipBuilder(ops);
            builder.AddUntrackedFile(stateFile.Path);

            SchedulePipBuilder(builder);

            int retryLogCount = numOfRetriesBeforeSucceed;
            bool shouldFail = numOfRetriesBeforeSucceed > PipExecutor.InternalSandboxedProcessExecutionFailureRetryCountMax;
            var result = RunScheduler();

            if (shouldFail)
            {
                result.AssertFailure();
                retryLogCount = PipExecutor.InternalSandboxedProcessExecutionFailureRetryCountMax + 1;
            }
            else
            {
                result.AssertSuccess();
            }

            AssertVerboseEventLogged(ProcessesLogEventId.PipRetryDueToExitedWithAzureWatsonExitCode, retryLogCount);
            AssertWarningEventLogged(ProcessesLogEventId.PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode, retryLogCount);

            if (shouldFail)
            {
                AssertErrorEventLogged(SchedulerLogEventId.PipExitedWithAzureWatsonExitCode, 1);
            }
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(0)]
        [InlineData(1)]
        public void RetryPipForChildExitingWithAzWatsonDeadExitCode(int mainExitCode)
        {
            Configuration.Sandbox.RetryOnAzureWatsonExitCode = true;
            bool mainProcessFailed = mainExitCode != 0;

            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));

            var ops = new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Spawn(Context.PathTable, true, Operation.SucceedOnRetry(stateFile,(int)SandboxedProcessPipExecutor.AzureWatsonExitCode, 2)),
                Operation.SucceedWithExitCode(mainExitCode)
            };
            var builder = CreatePipBuilder(ops);
            builder.AddUntrackedFile(stateFile.Path);

            SchedulePipBuilder(builder);

            var result = RunScheduler();

            if (mainProcessFailed)
            {
                result.AssertFailure();
            }
            else
            {
                result.AssertSuccess();
            }
            AssertWarningEventLogged(ProcessesLogEventId.PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode, mainProcessFailed ? 2 : 1);

            if (mainProcessFailed)
            {
                AssertVerboseEventLogged(ProcessesLogEventId.PipRetryDueToExitedWithAzureWatsonExitCode, 2);
                AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
            }
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void PipWithChildExitingWithAzWatsonDeadExitCodeIsNotCached(bool treatWarningsAsErrors)
        {
            Configuration.Sandbox.RetryOnAzureWatsonExitCode = true;
            Configuration.Logging.TreatWarningsAsErrors = treatWarningsAsErrors;

            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));

            var ops = new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Spawn(Context.PathTable, true, Operation.SucceedWithExitCode((int)SandboxedProcessPipExecutor.AzureWatsonExitCode)),
                Operation.SucceedWithExitCode(0)
            };
            var builder = CreatePipBuilder(ops);
            builder.AddUntrackedFile(stateFile.Path);

            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertCacheMiss(pip.Process.PipId);

            AssertWarningEventLogged(ProcessesLogEventId.PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode, 1);
            AssertVerboseEventLogged(ProcessesLogEventId.PipRetryDueToExitedWithAzureWatsonExitCode, 0);

            if (treatWarningsAsErrors)
            {
                RunScheduler().AssertCacheMiss(pip.Process.PipId);

                AssertWarningEventLogged(ProcessesLogEventId.PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode, 1);
                AssertVerboseEventLogged(ProcessesLogEventId.PipRetryDueToExitedWithAzureWatsonExitCode, 0);
            }
            else
            {
                RunScheduler().AssertCacheHit(pip.Process.PipId);
            }
        }

        // TODO (#BUG 1995797)
        // [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TimeoutPipWithChildExitingWithAzWatsonDeadExitCodeIsNotRetried()
        {
            Configuration.Sandbox.RetryOnAzureWatsonExitCode = true;

            var dirToCreate = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "sentinel"));
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            FileArtifact stateFile2 = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile2.txt"));

            ProcessBuilder builder = CreatePipBuilder(new[] {
                Operation.ReadFile(CreateSourceFile()),
                Operation.Spawn(Context.PathTable, true, Operation.SucceedOnRetry(stateFile,(int)SandboxedProcessPipExecutor.AzureWatsonExitCode, 1)),
                Operation.Spawn(Context.PathTable, true, Operation.CreateDirOnRetry(stateFile2,dirToCreate)),
                Operation.WaitUntilPathExists(dirToCreate, doNotInfer: true),    // Would block only the first time, causing a timeout
                Operation.WriteFile(CreateOutputFileArtifact()) });

            builder.Timeout = TimeSpan.FromSeconds(1);  // Will timeout on WaitUntilPathExists

            builder.AddUntrackedFile(stateFile.Path);
            builder.AddUntrackedFile(stateFile2.Path);

            SchedulePipBuilder(builder);
            RunScheduler().AssertFailure(); // Should fail due to timeout - no retry

            AllowWarningEventMaybeLogged(ProcessesLogEventId.PipFailedToCreateDumpFile);    // We don't care
            Assert.False(Directory.Exists(dirToCreate.Path.ToString(Context.PathTable)));   // No retry means the directory won't exist            
            AssertWarningEventLogged(ProcessesLogEventId.PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode, count: 1);
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessTookTooLongError, count: 1);
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError, count: 1);
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void GentleTimeoutIsUsedForEBPFPips()
        {
            FileArtifact dummyFile = CreateSourceFile();
            ProcessBuilder builder = CreatePipBuilder(new[] {
                // This file will never exist, so the pip will wait forever
                Operation.WaitUntilFileExists(dummyFile),
                // Dummy output
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            // Let's timeout the pip at 10ms
            builder.Timeout = TimeSpan.FromMilliseconds(10);
            builder.AddUntrackedFile(dummyFile);

            SchedulePipBuilder(builder);
            // We should fail because of the timeout
            RunScheduler().AssertFailure();

            // These are the usual errors/warnings on timeout
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessTookTooLongError, count: 1);
            AllowWarningEventMaybeLogged(ProcessesLogEventId.PipFailedToCreateDumpFile);    // We don't care

            // Assert we are not getting a child process surviving error. For EBPF pips, this is the 
            // indicator the ebpf runner was gently killed and the pip had a chance to emit an exit event
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessChildrenSurvivedError, 0);
        }

        /// <summary>
        /// This is a test for custom file-access logic for Windows-based tools that was hardcoded into the engine.
        /// See <see cref="SandboxedProcessPipExecutor.GetSpecialCaseRulesForSpecialTools(AbsolutePath, AbsolutePath)"/>
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestSpecialTempOutputFile()
        {
            FileArtifact input = CreateSourceFile();
            FileArtifact output = CreateFileArtifactWithName("rdms.lce", ObjectRoot).CreateNextWrittenVersion();
            FileArtifact tempOutput = CreateFileArtifactWithName("RDa03968", ObjectRoot).CreateNextWrittenVersion();

            AbsolutePath oldExeDirectory = TestProcessExecutable.Path.GetParent(Context.PathTable);
            AbsolutePath newExeDirectory = CreateUniqueDirectory(SourceRoot, "newExe");
            PathAtom oldExeName = TestProcessExecutable.Path.GetName(Context.PathTable);
            PathAtom newExeName = PathAtom.Create(Context.PathTable.StringTable, "rc.exe");
            AbsolutePath oldExePath = newExeDirectory.Combine(Context.PathTable, oldExeName);
            AbsolutePath newExePath = newExeDirectory.Combine(Context.PathTable, newExeName);

            DirectoryCopy(oldExeDirectory.ToString(Context.PathTable), newExeDirectory.ToString(Context.PathTable), true);
            File.Copy(oldExePath.ToString(Context.PathTable), newExePath.ToString(Context.PathTable));

            FileArtifact oldTestProcessExecutable = TestProcessExecutable;
            TestProcessExecutable = FileArtifact.CreateSourceFile(newExePath);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(output),
                Operation.WriteFile(tempOutput, doNotInfer: true)
            });

            // This is needed to avoid unrelated DFAs (the test process touches several dlls that are next to the executable)
            builder.AddUntrackedDirectoryScope(newExeDirectory);

            SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();
        }

        /// <summary>
        /// Tests the logic for the CacheOnly mode which only performs cache lookups and skips executing pips upon misses
        /// </summary>
        [Fact]
        public void CacheOnlyMode()
        {
            // Create a build graph with pip dependency ordering of:
            // pipA -> pipB -> pipC
            var pipAInput = CreateSourceFile();
            var pipAOutput = CreateOutputFileArtifact();
            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(pipAInput),
                Operation.WriteFile(pipAOutput),
            });

            var pipBInput = CreateSourceFile();
            var pipBOutput = CreateOutputFileArtifact();
            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(pipBInput),
                Operation.ReadFile(pipAOutput),
                Operation.WriteFile(pipBOutput),
            });

            var pipC = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(CreateSourceFile()),
                Operation.ReadFile(pipBOutput),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            // Run the build to get all pips in the cache
            RunScheduler().AssertSuccess();

            // Ensure they're all cached
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId);

            // Modify the input to pipB and rerun with CacheOnly mode
            File.AppendAllText(ToString(pipBInput.Path), "ChangedFileContent");
            Configuration.Schedule.CacheOnly = true;
            ScheduleRunResult cacheOnlyRun = RunScheduler();

            // We expect the build to succeed, PipA to be cached
            cacheOnlyRun.AssertSuccess();
            cacheOnlyRun.AssertCacheHit(pipA.Process.PipId);
            // PipB should be skipped because its input changed
            XAssert.AreEqual(PipResultStatus.Skipped, cacheOnlyRun.PipResults[pipB.Process.PipId]);
            // PipB should also be skipped because its upstream dependency was skipped
            XAssert.AreEqual(PipResultStatus.Skipped, cacheOnlyRun.PipResults[pipC.Process.PipId]);
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetryPipOnHighMemoryUsage(bool allowLowMemoryRetry)
        {
            Configuration.Schedule.MaximumRamUtilizationPercentage = 95;
            Configuration.Schedule.ManageMemoryMode = ManageMemoryMode.CancellationRam;
            Configuration.Schedule.MaxRetriesDueToLowMemory = allowLowMemoryRetry ? 100 : 0;

            var waitFile = ScheduleWaitingForFilePips(numberOfPips: 2);

            var highPressureCycles = 5;
            var released = false;
            var testHook = new SchedulerTestHooks()
            {
                SimulateHighMemoryPressure = true,
                GenerateSyntheticMachinePerfInfo = (loggingContext, scheduler) =>
                {
                    if (scheduler.MaxExternalProcessesRan == 2)
                    {
                        // After the two processes are launched
                        if (highPressureCycles > 0)
                        {
                            // Simulate high memory pressure for a while.
                            // This will kick off cancellation of one of the pips
                            --highPressureCycles;
                            return s_highMemoryPressurePerf;
                        }
                        else
                        {
                            if (!released)
                            {
                                // Ensure that the ongoing processes will now finish
                                WriteSourceFile(waitFile);
                                released = true;
                            }

                            // Go back to normal so scheduler can indeed retry (and succeed),
                            // or fail anyways if we don't allow retries
                            return s_normalPerf;
                        }
                    }
                    else
                    {
                        return s_normalPerf;
                    }
                }
            };

            var schedulerResult = RunScheduler(testHooks: testHook, updateStatusTimerEnabled: true);

            if (allowLowMemoryRetry)
            {
                schedulerResult.AssertSuccess();
                AssertVerboseEventLogged(LogEventId.PipRetryDueToLowMemory, count: 1);
            }
            else
            {
                schedulerResult.AssertFailure();
                AssertErrorEventLogged(LogEventId.ExcessivePipRetriesDueToLowMemory);
            }

            AssertVerboseEventLogged(LogEventId.StoppingProcessExecutionDueToMemory);
            AssertVerboseEventLogged(LogEventId.CancellingProcessPipExecutionDueToResourceExhaustion);
            AssertVerboseEventLogged(LogEventId.StartCancellingProcessPipExecutionDueToResourceExhaustion);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // suspend/resume is not available on macOS
        public void SuspendResumePipOnHighMemoryUsage()
        {
            Configuration.Schedule.MaximumRamUtilizationPercentage = 95;
            Configuration.Schedule.ManageMemoryMode = ManageMemoryMode.Suspend;

            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Block(),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Block(),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            bool triggeredResume = false;
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            var testHook = new SchedulerTestHooks()
            {
                GenerateSyntheticMachinePerfInfo = (loggingContext, scheduler) =>
                {
                    if (triggeredResume)
                    {
                        global::BuildXL.App.Tracing.Logger.Log.CancellationRequested(loggingContext, immediateTerminationKeystroke: "ctrl-c");
                        tokenSource.Cancel();
                    }

                    if (scheduler.State.ResourceManager.NumSuspended > 0)
                    {
                        triggeredResume = true;
                        return s_normalPerf;
                    }

                    if (scheduler.MaxExternalProcessesRan == 2)
                    {
                        return s_highMemoryPressurePerf;
                    }

                    return s_normalPerf;
                }
            };

            RunScheduler(testHooks: testHook, updateStatusTimerEnabled: true, cancellationToken: tokenSource.Token).AssertFailure();

            AssertErrorEventLogged(global::BuildXL.App.Tracing.LogEventId.CancellationRequested);
            AssertVerboseEventLogged(LogEventId.EmptyWorkingSet);
            AssertVerboseEventLogged(LogEventId.ResumeProcess);
        }

        // TODO: Investigate why this times out on Linux, work item#1984802
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void SingleSuspendedPipIsCancelledUnderContinuousMemoryPressure()
        {
            Configuration.Schedule.MaximumRamUtilizationPercentage = 95;
            Configuration.Schedule.ManageMemoryMode = ManageMemoryMode.Suspend;

            // Schedule two pips that will wait for waitFile to be written
            var waitFile = ScheduleWaitingForFilePips(numberOfPips: 2);

            bool fileWritten = false;
            var simulatedPerf = s_normalPerf;
            var testHook = new SchedulerTestHooks()
            {
                SimulateHighMemoryPressure = true,
                GenerateSyntheticMachinePerfInfo = (loggingContext, scheduler) =>
                {
                    if (!fileWritten && scheduler.MaxExternalProcessesRan == 2)
                    {
                        // After the two pips are scheduled, simulate high pressure
                        simulatedPerf = s_highMemoryPressurePerf;
                    }

                    if (!fileWritten && scheduler.State.ResourceManager.NumSuspended > 0)
                    {
                        // Allow the non-suspended process to finish
                        WriteSourceFile(waitFile);
                        fileWritten = true;
                    }

                    if (fileWritten && scheduler.State.ResourceManager.NumSuspended == 0)
                    {
                        // The process was cancelled and will be run again
                        simulatedPerf = s_normalPerf;
                    }

                    return simulatedPerf;
                }
            };

            var schedulerResult = RunScheduler(testHooks: testHook, updateStatusTimerEnabled: true);


            // One of the pips was suspended, never resumed, cancelled, and ran to completion when retried
            schedulerResult.AssertSuccess();
            AssertVerboseEventLogged(LogEventId.PipRetryDueToLowMemory, count: 1);
            AssertVerboseEventLogged(LogEventId.EmptyWorkingSet, count: 1);
            AssertVerboseEventLogged(LogEventId.ResumeProcess, count: 0);
            AssertVerboseEventLogged(LogEventId.StartCancellingProcessPipExecutionDueToResourceExhaustion);
            AssertVerboseEventLogged(LogEventId.CancellingProcessPipExecutionDueToResourceExhaustion, count: 1);
            AssertVerboseEventLogged(LogEventId.StoppingProcessExecutionDueToMemory);
        }

        [Fact]
        public void SurvivingChildProcessesNotReportedOnCancelation()
        {
            var processA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Spawn(Context.PathTable, waitToFinish: true, Operation.Block()),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            var testHook = new SchedulerTestHooks()
            {
                GenerateSyntheticMachinePerfInfo = (loggingContext, scheduler) =>
                {
                    if (scheduler.RetrieveExecutingProcessPips().Count() > 0)
                    {
                        global::BuildXL.App.Tracing.Logger.Log.CancellationRequested(loggingContext, immediateTerminationKeystroke: "ctrl-c");
                        tokenSource.Cancel();
                    }

                    return new PerformanceCollector.MachinePerfInfo();
                },
            };

            var result = RunScheduler(testHooks: testHook, updateStatusTimerEnabled: true, cancellationToken: tokenSource.Token).AssertFailure();
            AllowErrorEventLoggedAtLeastOnce(global::BuildXL.App.Tracing.LogEventId.CancellationRequested);
            result.AssertPipResultStatus((processA.Process.PipId, PipResultStatus.Canceled));
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void TestEnumeratePattern(bool useDotNetEnumerateOnWindows)
        {
            AbsolutePath directory = CreateUniqueDirectory();
            string dirString = directory.ToString(Context.PathTable);
            FileArtifact aCFile = CreateFileArtifactWithName("a.c", dirString);
            WriteSourceFile(aCFile);
            FileArtifact aHFile = CreateFileArtifactWithName("a.h", dirString);
            WriteSourceFile(aHFile);

            DirectoryArtifact sealedDir = CreateAndScheduleSealDirectoryArtifact(directory, SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new Operation[]
            {
                // Enumerate directory with different Enumerate methods, and set the enumeratePattern to *.h.
                Operation.EnumerateDir(sealedDir, useDotNetEnumerateOnWindows, doNotInfer: true, enumeratePattern: "*.h"),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builder.AddInputDirectory(sealedDir);

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify .c file, pip should cache hit.
            WriteSourceFile(aCFile);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify .h file, pip should cache hit.
            WriteSourceFile(aHFile);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Add a .c file, pip should cache hit.
            FileArtifact bCFile = CreateFileArtifactWithName("b.c", dirString);
            WriteSourceFile(bCFile);

            // If useDotNetEnumerateOnWindows for dotnet is true, EnumerateDir calls NtQueryDirectoryFile with no pattern,
            // which BuildXL treats as "*" pattern (.NET does the filtering in the managed layer, which is not visible by Detours).
            // Thus, when useDotNetEnumerateOnWindows is true, the pip will have a cache miss even though EnumerateDir specified "*.h" as a pattern.
            // When useDotNetEnumerateOnWindows is false, EnumerateDir calls FindFirstFile/FindNextFile to enumerate the directory.
            // For such calls, Detours is aware of the enumeration pattern and can report it accurately to BuildXL.
            // Thus, when useDotNetEnumerateOnWindows is false, the pip will have a cache hit.
            if (useDotNetEnumerateOnWindows)
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
            }

            RunScheduler().AssertCacheHit(pip.PipId);

            // Add a .h file, pip should cache miss.
            FileArtifact bHFile = CreateFileArtifactWithName("b.h", dirString);
            WriteSourceFile(bHFile);
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [TheoryIfSupported(Skip = "[pgunasekara] Failing on rolling")]
        [InlineData(true)]
        [InlineData(false)]
        public void LinuxSandboxDetectsStaticallyLinkedBinary(bool rootProcessStaticallyLinked)
        {
            XAssert.IsTrue(UnixObjectFileDumpUtils.IsObjDumpInstalled.Value, "Binutils is not installed on this machine. Please install it with 'apt-get install binutils'");

            var staticProcessName = "TestProcessStaticallyLinked";
            var dynamicProcessName = "TestProcessDynamicallyLinked";
            var staticProcessPath = Path.Combine(TestBinRoot, "LinuxTestProcesses", staticProcessName);
            var dynamicProcessPath = Path.Combine(TestBinRoot, "LinuxTestProcesses", dynamicProcessName);

            var workingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            var staticProcess = CreateFileArtifactWithName(staticProcessName, workingDirectory.Path.ToString(Context.PathTable));
            var dynamicProcess = CreateFileArtifactWithName(dynamicProcessName, workingDirectory.Path.ToString(Context.PathTable));

            // Copy test executables to new working directory
            File.Copy(staticProcessPath, Path.Combine(workingDirectory.Path.ToString(Context.PathTable), staticProcessName));
            File.Copy(dynamicProcessPath, Path.Combine(workingDirectory.Path.ToString(Context.PathTable), dynamicProcessName));

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.SpawnExe(Context.PathTable, rootProcessStaticallyLinked ? staticProcess : dynamicProcess, arguments: rootProcessStaticallyLinked ? "0" : "1"),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            builder.WorkingDirectory = workingDirectory;
            builder.AddInputFile(staticProcess.Path);
            builder.AddInputFile(dynamicProcess.Path);
            if (!rootProcessStaticallyLinked)
            {
                // This file is written by the actual test process to test whether it gets detected by the sandbox
                builder.AddOutputFile(AbsolutePath.Create(Context.PathTable, Path.Combine(workingDirectory.Path.ToString(Context.PathTable), "testFile.txt")));
            }

            SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();
            AssertWarningEventLogged(ProcessesLogEventId.LinuxSandboxReportedBinaryRequiringPTrace, count: 1);
        }

        /// <summary>
        /// Ensures that PipSepcificPropetiesConfig object is wired accordingly.
        /// Ensure that the GetPipIdsForProerty retrieve set of ids as required.
        /// </summary>
        /// <param name="pipSpecificProperty"></param>
        [Theory]
        [InlineData(PipSpecificPropertiesConfig.PipSpecificProperty.ForcedCacheMiss)]
        [InlineData(PipSpecificPropertiesConfig.PipSpecificProperty.EnableVerboseProcessLogging)]
        [InlineData(PipSpecificPropertiesConfig.PipSpecificProperty.PipFingerprintSalt)]
        public void ValidateGetPipIdsForProperty(PipSpecificPropertiesConfig.PipSpecificProperty pipSpecificProperty)
        {
            var propertiesAndValues = new List<PipSpecificPropertyAndValue>
            {
               new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.ForcedCacheMiss, 24 ,null),
               new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.EnableVerboseProcessLogging, 24, null),
               new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.EnableVerboseProcessLogging, 22, null),
               new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.PipFingerprintSalt, 24 ,"salty"),
               new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.PipFingerprintSalt, 24, "TooSalty"),
               new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.PipFingerprintSalt, 22, "saltLess")
            };

            Configuration.Engine.PipSpecificPropertyAndValues.AddRange(propertiesAndValues);
            var builder = CreatePipBuilder(new Operation[]
            {
                // dummy file
                Operation.WriteFile(CreateOutputFileArtifact()),
            });

            SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();

            // Retrieve semistableHashes for a specific property
            var retrievedSemistableHashes = PipSpecificPropertiesConfig?.GetPipIdsForProperty(pipSpecificProperty);

            // Retrieve list of semistableHashes which are specific to the property
            var expectedSemistableHashes = propertiesAndValues.Where(x => x.PropertyName == pipSpecificProperty)
                                                      .Select(pipSpecificProperty => pipSpecificProperty.PipSemiStableHash)
                                                      .ToReadOnlySet();

            // Ensure that both the lists have the same semistablehashes.
            XAssert.IsTrue(retrievedSemistableHashes.Any());
            XAssert.IsTrue(expectedSemistableHashes.SequenceEqual(retrievedSemistableHashes));

            // Checks if the given pipId has a property or not.
            foreach (var expectedSemistableHash in expectedSemistableHashes)
            {
                XAssert.IsTrue(PipSpecificPropertiesConfig.PipHasProperty(pipSpecificProperty, expectedSemistableHash));
            }

            // Obtain pip property value for a given property and semistablehash.
            if (pipSpecificProperty.ToString().Equals("PipFingerprintSalt"))
            {
                XAssert.AreEqual(PipSpecificPropertiesConfig.GetPipSpecificPropertyValue(pipSpecificProperty, 24), "TooSalty");
            }
        }

        /// <summary>
        /// Ensure that a pip is salted when the salt value is passed via PipFingerprintSalt flag.
        /// This test also runs with IS feature enabled as well. So it covers the IS feature
        /// </summary>
        [Fact]
        public void CheckPipSpecificFingerprintSaltingForConstantSaltValue()
        {
            var pOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) };

            // Schedule pip1 but without passing the pipSpecificFingerprintSalt
            Configuration.Engine.PipSpecificPropertyAndValues = new List<PipSpecificPropertyAndValue>();
            Process pip = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Set the pipFingerprintSalt property value for this pip in the config object.
            // Once this is passed we expect the pip to have a cache miss.
            var propertiesAndValues = new List<PipSpecificPropertyAndValue>
             {
                new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.PipFingerprintSalt, pip.SemiStableHash, "TooSalted"),
             };
            Configuration.Engine.PipSpecificPropertyAndValues.AddRange(propertiesAndValues);
            ResetPipGraphBuilder();
            // Build pip1 again and we should get an initial cache miss as we have passed the pipSpecificSalt
            pip = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Do not pass the pipSpecificFingerprintSalt
            // Now when we reconstruct the graph we expect a cache hit with the first constructed graph
            Configuration.Engine.PipSpecificPropertyAndValues = new List<PipSpecificPropertyAndValue>();
            ResetPipGraphBuilder();
            pip = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// Ensure that a pip is salted when the salt value of "*" is passed via PipFingerprintSalt flag.
        /// This test also runs with IS feature enabled as well. So it covers the IS feature.
        /// </summary>
        [Fact]
        public void CheckPipSpecificFingerprintSaltingWhenSaltValueIsNotConstant()
        {
            var pOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) };

            // Schedule pip1 but without passing the pipSpecificFingerprintSalt
            Configuration.Engine.PipSpecificPropertyAndValues = new List<PipSpecificPropertyAndValue>();
            Process pip = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Set the pipFingerprintSalt property value for this pip in the config object.
            // Once this is passed we expect the pip to have a cache miss.
            SchedulePipsForSaltValue(pOperations, pip);
            SchedulePipsForSaltValue(pOperations, pip);

            // Do not pass the pipSpecificFingerprintSalt
            // Now when we reconstruct the graph we expect a cache hit with the first constructed graph
            Configuration.Engine.PipSpecificPropertyAndValues = new List<PipSpecificPropertyAndValue>();
            ResetPipGraphBuilder();
            pip = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void ProbeOnOutputOfNonOrderedPip(bool isDirectoryProbe, bool invertExecutionOrder)
        {
            // A pip probes a path that is produced by another pip that is not ordered w/r to the prober.
            // We should flag this as a violation. Note that we treat directory accesses differently,
            // so in that case a violation is not flagged.
            string dir = Path.Combine(SourceRoot, "dir");
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, dir);
            DirectoryArtifact dirToProbe = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            var outputUnderDirectory = CreateOutputFileArtifact(dirPath, prefix: "probed_");

            var operationsA = new List<Operation>
            {
                isDirectoryProbe ? Operation.DirProbe(dirToProbe) : Operation.Probe(outputUnderDirectory, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var operationsB = new List<Operation>
            {
                Operation.WriteFile(outputUnderDirectory)
            };

            var builderA = CreatePipBuilder(operationsA);
            var pipA = SchedulePipBuilder(builderA);
            var builderB = CreatePipBuilder(operationsB);
            var pipB = SchedulePipBuilder(builderB);

            var runResult = RunScheduler(constraintExecutionOrder:
                                new List<(Pip, Pip)>() { invertExecutionOrder ? (pipB.Process, pipA.Process) : (pipA.Process, pipB.Process) });

            if (isDirectoryProbe)
            {
                // When the probe is on a directory, we don't detect violations
                runResult.AssertSuccess();
            }
            else
            {
                runResult.AssertFailure();
                AllowWarningEventMaybeLogged(global::BuildXL.Scheduler.Tracing.LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.FileMonitoringError);
            }
        }

        /// <summary>
        /// A special use case where the salt value is "*" in this case it generates a random salt value everytime we pass this value.
        /// </summary>
        /// <remarks>
        /// If this method is invoked consecutively then it should make the pip have a cache miss as the salt value is random everytime.
        /// </remarks>
        private void SchedulePipsForSaltValue(Operation[] pOperations, Process pip)
        {
            var propertiesAndValues = new List<PipSpecificPropertyAndValue>
             {
                new PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty.PipFingerprintSalt, pip.SemiStableHash, "*"),
             };
            Configuration.Engine.PipSpecificPropertyAndValues.AddRange(propertiesAndValues);
            ResetPipGraphBuilder();
            // Build pip1 again we should get a cache miss everytime as the salt value is "*", means the salt value is not constant.
            pip = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void VerifySandboxMessageCount()
        {
            Configuration.Sandbox.CheckDetoursMessageCount = true;

            var tempFile = $"{TemporaryDirectory}/testfile.txt";
            var builder = CreatePipBuilder(new Operation[]
            {
                // dummy file
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SpawnExe(
                    Context.PathTable,
                    GetCmdExecutable(),
                    OperatingSystemHelper.IsWindowsOS
                    ? $"/d /c \"echo hello world > {tempFile}\""
                    : $"-c \"echo hello world > {tempFile}\"")
            });
            builder.AddOutputFile(ToPath(tempFile));

            SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();

            AssertVerboseEventLogged(ProcessesLogEventId.LogMismatchedDetoursCountLostMessages, count: 0);
            
            // The Linux sandbox will also log additional messages if the semaphore was not signalled when a message arrives
            if (OperatingSystemHelper.IsLinuxOS)
            {
                AssertLogNotContains(caseSensitive: false, requiredLogMessages: "Message counting semaphore mismatch");
            }
        }

        /// <summary>
        /// If a pip exits with an uncacheable exit code then BuildXL does not cache the pip.
        /// </summary>
        [Fact]
        public void TestUncacheableExitCodes()
        {
            var outFile = CreateOutputFileArtifact();
            var ops = new Operation[]
            {
                 Operation.WriteFile(outFile),
                 Operation.SucceedWithExitCode(42)
            };

            var builder = CreatePipBuilder(ops);
            builder.SuccessExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 42 });
            var pip = SchedulePipBuilder(builder);
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);

            ResetPipGraphBuilder();

            // If the pip exits with an uncacheable exit code then we expect the pip to have a cache miss.
            builder.UncacheableExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 42 });     
            pip = SchedulePipBuilder(builder);
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
        }

        /// <summary>
        /// Validates that a user can specify the process timeout exit code as a retryable exit code.
        /// </summary>
        /// <remarks>
        /// This functionality is useful for when a process hangs due to a race condition and may succeed on retry
        /// </remarks>
        [Fact]
        public void RetryTimeoutExitCode()
        {
            // Create a process tree with a parent and child. The child watches for the parent to write a file and then exits
            // The first time the parent is run, it does not exit and thus also has a surviving child process.
            // Upon retry, the parent writes the output and does not hang
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            var retryMarker = CreateOutputDirectoryArtifact();
            var outputFile = CreateOutputFileArtifact();

            ProcessBuilder builder = CreatePipBuilder([
                Operation.ReadFile(CreateSourceFile()),
                Operation.CreateDirOnRetry(stateFile, retryMarker),
                // Add a dangling child process for good measure
                Operation.Spawn(Context.PathTable, waitToFinish: false, new [] { Operation.WaitUntilFileExists(outputFile) }),
                Operation.WaitUntilPathExists(FileArtifact.CreateOutputFile(retryMarker.Path)),
                Operation.WriteFile(outputFile)
                ]);
            builder.AddUntrackedFile(stateFile.Path);
            builder.Timeout = TimeSpan.FromSeconds(1);
            builder.RetryExitCodes = ReadOnlyArray<int>.From(new[] { global::BuildXL.Processes.ExitCodes.Timeout });
            builder.SetProcessRetries(1);
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            // Expect success since retries have been configured
            RunScheduler().AssertSuccess();
            AssertVerboseEventLogged(SchedulerLogEventId.PipWillBeRetriedDueToExitCode, count: 1);

            Assert.True(global::BuildXL.Processes.ExitCodes.Timeout == 27021977, 
                "This exit code should never change as end users may have a dependency by specifying it as a retry exit code");

            if (OperatingSystemHelper.IsUnixOS)
            {
                // Creating dump is not supported on non-Windows.
                AllowWarningEventMaybeLogged(ProcessesLogEventId.PipFailedToCreateDumpFile);
            }
        }

        private Operation ProbeOp(string root, string relativePath = "")
        {
            return Operation.Probe(CreateFileArtifactWithName(root: root, name: relativePath), doNotInfer: true);
        }

        private void CreateDir(string root, string relativePath)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(path);
        }

        private void CreateFile(string root, string relativePath)
        {
            WriteSourceFile(CreateFileArtifactWithName(root: root, name: relativePath));
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, false);
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }
    }
}
