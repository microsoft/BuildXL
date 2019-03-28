// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
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
    /// Tests that validate file access policies and caching behavior
    /// </summary>
    [Trait("Category", "FileAccessPolicyTests")]
    public class FileAccessPolicyTests : SchedulerIntegrationTestBase
    {
        public FileAccessPolicyTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <param name="declareDependency">
        /// When true, the pip declares a dependency on the consumed, untracked input.
        /// When false, the pip does not declare a dependency on the consumed, untracked input.
        /// </param>
        ///
        /// <param name="inputUnderUntrackedScope">
        /// When true, the untracked input is declared as an untrackedPath. 
        /// When false, the untracked input's parent directory is declared as an untrackedScope.
        /// </param>
        [Feature(Features.UntrackedAccess)]
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void ValidateCachingUntrackedPathInput(bool declareDependency, bool inputUnderUntrackedScope)
        {
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(SourceRoot));
            FileArtifact nestedFile = CreateSourceFile(ArtifactToString(dir));

            // Take input from an untracked path /dir/nestedFile
            var ops = new Operation[]
            {
                Operation.ReadFile(nestedFile, doNotInfer: !declareDependency),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            if (inputUnderUntrackedScope)
            {
                builder.AddUntrackedDirectoryScope(dir);
            }
            else
            {
                builder.AddUntrackedFile(nestedFile);
            }

            Process pip = SchedulePipBuilder(builder).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify untracked input file /dir/nestedFile
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");
            if (declareDependency)
            {
                // Changes in declared dependencies always cause cache misses
                RunScheduler().AssertCacheMiss(pip.PipId);
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete untracked input file /dir/nestedFile
            File.Delete(ArtifactToString(nestedFile));
            if (declareDependency)
            {
                // Changes in declared dependencies always cause cache misses
                RunScheduler().AssertCacheMiss(pip.PipId);
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            ResetPipGraphBuilder();

            // Validate cache miss if the process removes untracked path declarations
            pip = CreateAndSchedulePipBuilder(ops).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.UntrackedAccess)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingDirectoryEnumerationReadOnlyMountUntrackedScope(bool maskUntrackedAccesses)
        {
            // When true (default), directory enumerations in untracked scopes are ignored
            // When false, directory enumerations in untracked scopes are tracked as normal
            Configuration.Sandbox.MaskUntrackedAccesses = maskUntrackedAccesses;

            AbsolutePath readonlyRootPath;
            AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out readonlyRootPath);
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));

            // Enumerate untracked scope /dir and its parent directory
            FileArtifact outFile = CreateOutputFileArtifact();
            var ops = new Operation[]
            {
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(readonlyRootPath)),
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outFile)
            };
            var builder = CreatePipBuilder(ops);
            builder.AddUntrackedDirectoryScope(dir);
            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint1 = File.ReadAllText(ArtifactToString(outFile));

            // Create /dir/nestedFile in untracked scope
            FileArtifact nestedFile = CreateSourceFile(ArtifactToString(dir));
            if (!maskUntrackedAccesses)
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /dir/nestedFile in untracked scope
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /dir/nestedFile in untracked scope
            File.Delete(ArtifactToString(nestedFile));
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint2 = File.ReadAllText(ArtifactToString(outFile));

            // Filesystem should match state from original run, so cache replays output from that run
            XAssert.AreEqual(checkpoint1, checkpoint2);

            // Create /dir/nestedDir in untracked scope
            DirectoryArtifact nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ArtifactToString(dir)));
            if (!maskUntrackedAccesses)
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /dir/nestedDir in untracked scope
            Directory.Delete(ArtifactToString(nestedDir));
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint3 = File.ReadAllText(ArtifactToString(outFile));

            // Filesystem should match state from original run, so cache replays output from that run
            XAssert.AreEqual(checkpoint1, checkpoint3);

            // Delete untracked scope /dir
            Directory.Delete(ArtifactToString(dir));
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Feature(Features.Mount)]
        [Fact]
        public void ValidateCachingNonhashableMount()
        {
            // Absent file and directory in nonhashable mount
            FileArtifact nonhashableFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix, NonHashableRoot));
            DirectoryArtifact nonhashableDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath(SourceRootPrefix, NonHashableRoot));

            ValidateCachingBehaviorUntrackedMount(nonhashableFile, nonhashableDir, undefinedMount: false);
        }

        [Feature(Features.Mount)]
        [Fact]
        public virtual void ValidateCachingUndefinedMount_Bug1087986()
        {
            // Absent file and directory in an undefined mount 
            FileArtifact undefinedFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix, TemporaryDirectory));
            DirectoryArtifact undefinedDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath(SourceRootPrefix, TemporaryDirectory));

            ValidateCachingBehaviorUntrackedMount(undefinedFile, undefinedDir, undefinedMount: true);
            AssertWarningEventLogged(EventId.IgnoringUntrackedSourceFileNotUnderMount, 7);
        }

        public void ValidateCachingBehaviorUntrackedMount(FileArtifact file, DirectoryArtifact dir, bool undefinedMount)
        {
            // Dynamically observed accesses on the file and directory
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(file),
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create previously absent /file
            File.WriteAllText(ArtifactToString(file), "hello");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create previously absent /dir
            Directory.CreateDirectory(ArtifactToString(dir));

            if (undefinedMount)
            {
                //// TODO: Bug #1087986 - This is a miss for undefined mounts 
                RunScheduler().AssertCacheMiss(pip.PipId);
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /file
            File.WriteAllText(ArtifactToString(file), "world");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /dir by creating /dir/undefinedNestedFile
            FileArtifact undefinedNestedFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix, ArtifactToString(dir)));
            File.Create(ArtifactToString(undefinedNestedFile));
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// Tests that pips should be able to consume input from other pips' optional outputs.
        /// </summary>
        [Feature(Features.OptionalOutput)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowConsumeOptionalOutput(bool shouldProduceOptionalOutput)
        {
            // Pip A declares an optional output, which may be produced or not dependending on the parameter flag
            FileArtifact maybeOutput = CreateSourceFile();

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                shouldProduceOptionalOutput?
                    Operation.WriteFile(maybeOutput, doNotInfer: true) : 
                    // produce a dummy file otherwise 
                    Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilderA.AddOutputFile(maybeOutput, FileExistence.Optional);
            var result = SchedulePipBuilder(pipBuilderA);

            // Pip B declares a dependency on the optional output and reads it
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                // Read operation will ignore an absent file
                Operation.ReadFile(result.ProcessOutputs.GetOutputFile(maybeOutput)),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            SchedulePipBuilder(pipBuilderB);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        [Feature(Features.OptionalOutput)]
        public void ValidateCachingWithPresentOptionalOutput()
        {
            FileArtifact optionalOutput = CreateSourceFile();

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(optionalOutput, doNotInfer: true),
            });
            pipBuilder.AddOutputFile(optionalOutput, FileExistence.Optional);
            var result = SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertCacheMiss(result.Process.PipId);

            // We remove the output, replay it from the cache and make sure it is there afterwards
            File.Delete(optionalOutput.Path.ToString(Context.PathTable));
            RunScheduler().AssertCacheHit(result.Process.PipId);
            Assert.True(File.Exists(optionalOutput.Path.ToString(Context.PathTable)));
        }

        [Fact]
        [Feature(Features.OptionalOutput)]
        public void ValidateCachingWithAbsentOptionalOutput()
        {
            FileArtifact optionalOutput = CreateSourceFile();

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                // Create dummy file, don't write the optional output
                Operation.WriteFile(CreateOutputFileArtifact()),
            });
            pipBuilder.AddOutputFile(optionalOutput, FileExistence.Optional);
            var result = SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertCacheMiss(result.Process.PipId);

            // We create a bogus file with the same name as the optional (absent) output 
            // to make sure it gets removed when replayed from the cache
            File.WriteAllText(optionalOutput.Path.ToString(Context.PathTable), "Bogus file");
            RunScheduler().AssertCacheHit(result.Process.PipId);
            Assert.False(File.Exists(optionalOutput.Path.ToString(Context.PathTable)));
        }

        public enum UntrackKind { UntrackFileAsFile, UntrackFileAsScope, UntrackParentDirAsFile, UntrackParentDirAsScope };

        [Theory]
        [InlineData(UntrackKind.UntrackFileAsFile, true)]
        [InlineData(UntrackKind.UntrackParentDirAsFile, false)]
        [InlineData(UntrackKind.UntrackParentDirAsScope, true)]
        public void ValidateUntracking(UntrackKind untrackWhat, bool expectSuccess)
        {
            DirectoryArtifact parentDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(SourceRoot));
            FileArtifact file = CreateSourceFile(ArtifactToString(parentDir));

            var ops = new Operation[]
            {
                Operation.ReadFile(file, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            switch (untrackWhat)
            {
                case UntrackKind.UntrackFileAsFile:
                    builder.AddUntrackedFile(file);
                    break;
                case UntrackKind.UntrackParentDirAsFile:
                    builder.AddUntrackedFile((AbsolutePath)parentDir);
                    break;
                case UntrackKind.UntrackParentDirAsScope:
                    builder.AddUntrackedDirectoryScope(parentDir);
                    break;
                default:
                    XAssert.Fail("Unknown kind: " + untrackWhat);
                    break;
            }

            SchedulePipBuilder(builder);

            if (expectSuccess)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                RunScheduler().AssertFailure();
                AssertErrorEventLogged(EventId.FileMonitoringError);
                AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            }
        }

        [Feature(Features.Mount)]
        [Fact]
        public void FailWhenCreatingDirectoryReadOnlyMount()
        {
            // Process creates directory in read/write mount ObjectRoot
            DirectoryArtifact dirReadWriteMount = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniquePath("rw-dir", ObjectRoot));
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateDir(dirReadWriteMount),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;
            RunScheduler().AssertSuccess();

            ResetPipGraphBuilder();

            // Process creates directory in read only mount ReadonlyRoot is disallowed when policies are 
            // enforced on directory creation (/EnforceAccessPoliciesOnDirectoryCreation+)
            Configuration.Sandbox.EnforceAccessPoliciesOnDirectoryCreation = true;
            DirectoryArtifact dirReadonlyMount = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniquePath("r-dir", ReadonlyRoot));
            pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateDir(dirReadonlyMount),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipProcessError);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }
    }
}
