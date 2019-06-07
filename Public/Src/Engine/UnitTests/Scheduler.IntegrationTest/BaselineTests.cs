// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
            AssertErrorEventLogged(EventId.PipProcessError);
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
            AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.AbortObservedInputProcessorBecauseFileUntracked);
            if (!shouldPipSucceed)
            {
                AssertErrorEventLogged(EventId.PipProcessError);
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
            AssertErrorEventLogged(EventId.FileMonitoringError);
            if (!shouldPipSucceed)
            {
                AssertErrorEventLogged(EventId.PipProcessError);
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
                AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory);
            }
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
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

            using (var tempCleaner = new TempCleaner())
            {
                if (shouldPipFail)
                {
                    RunScheduler(tempCleaner : tempCleaner).AssertFailure();
                    AssertErrorEventLogged(EventId.PipProcessError);
                    tempCleaner.WaitPendingTasksForCompletion();
                    XAssert.IsTrue(Directory.Exists(tempdirStr), $"TEMP directory deleted but wasn't supposed to: {tempdirStr}");
                    XAssert.IsTrue(File.Exists(fileStr), $"Temp file deleted but wasn't supposed to: {fileStr}");
                }
                else
                {
                    RunScheduler(tempCleaner : tempCleaner).AssertSuccess();
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
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 1, allowMore: OperatingSystemHelper.IsUnixOS);
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
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
            AssertVerboseEventLogged(EventId.PipProcessMissingExpectedOutputOnCleanExit);
            AssertErrorEventLogged(EventId.PipProcessExpectedMissingOutputs);
            AssertErrorEventLogged(EventId.PipProcessError);
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

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Theory]
        [InlineData(true, "")]
        [InlineData(false, "")]
        [InlineData(true, "*")]
        [InlineData(false, "*")]
        public void ValidateCachingDirectoryEnumerationReadOnlyMount(bool topLevelTest, string enumeratePattern)
        {
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
        [Fact]
        public void ValidateCachingDirectoryEnumerationWithFilterReadOnlyMount()
        {
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
            // Case does not matter
            RunScheduler().AssertCacheHit(pip.PipId);

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
        [Fact]
        [Trait("Category", "WindowsOSOnly")] // we currently cannot detect enumerate pattern with macOS sandbox
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
        [InlineData("*")]
        [InlineData("*.txt")]
        public void ValidateCachingDirectoryEnumerationReadWriteMount(string enumeratePattern)
        {
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
            });
            Process pipA = paoA.Process;

            var srcB = CreateSourceFile();
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcB),
                Operation.WriteFile(rewrittenFile, doNotInfer: true), /* unique output on every pip run */
                Operation.WriteFile(rewrittenFile, System.Environment.NewLine, doNotInfer: true)
            });
            pipBuilderB.AddRewrittenFileInPlace(paoA.ProcessOutputs.GetOutputFile(rewrittenFile.Path));
            var paoB = SchedulePipBuilder(pipBuilderB);
            Process pipB = paoB.Process;

            var srcC = CreateSourceFile();
            var pipBuilderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcC),
                Operation.WriteFile(rewrittenFile, doNotInfer: true), /* unique output on every pip run */
                Operation.WriteFile(rewrittenFile, System.Environment.NewLine, doNotInfer: true)
            });
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

            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);
            AssertWarningEventLogged(EventId.FileMonitoringWarning, count: 1);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // WriteFile operation failed on MacOS; need further investigation.
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
        [Fact]
        [Trait("Category", "WindowsOSOnly")] // WriteFile operation failed on MacOS; need further investigation.
        public void MoveDirectoryFailed()
        {
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


        /// <summary>
        /// Validates behavior with a process being retried
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetryExitCodes(bool succeedOnRetry)
        {
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            var ops = new Operation[]
            {
                Operation.WriteFile(FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "out.txt")), content: "Hello"),
                succeedOnRetry ? 
                    Operation.SucceedOnRetry(untrackedStateFilePath: stateFile, firstFailExitCode: 42) :
                    Operation.Fail(-2),
            };

            var builder = CreatePipBuilder(ops);
            builder.RetryExitCodes = global::BuildXL.Utilities.Collections.ReadOnlyArray<int>.From(new int[] { 42 });
            builder.AddUntrackedFile(stateFile.Path);
            SchedulePipBuilder(builder);

            Configuration.Schedule.ProcessRetries = 1;

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
    }
}
