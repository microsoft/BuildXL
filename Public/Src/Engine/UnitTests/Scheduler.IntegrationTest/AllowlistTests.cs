// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Processes;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Configuration = BuildXL.Utilities.Configuration;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "AllowlistTests")]
    [Feature(Features.Allowlist)]
    public class AllowlistTests : SchedulerIntegrationTestBase
    {
        /// <summary>
        /// What type of mount the files and directories in the test are placed under
        /// </summary>
        protected readonly struct MountType
        {
            public const int Readonly = 0;
            public const int NonHashable = 1;
            public const int Undefined = 2;
        }

        private System.Collections.Generic.Dictionary<int, string> m_mounts;

        public AllowlistTests(ITestOutputHelper output) : base(output)
        {
            m_mounts = new System.Collections.Generic.Dictionary<int, string>();
            m_mounts.Add(MountType.Readonly, ReadonlyRoot);
            m_mounts.Add(MountType.NonHashable, NonHashableRoot);
            m_mounts.Add(MountType.Undefined, TemporaryDirectory);
        }

        [Feature(Features.Mount)]
        [Theory]
        [InlineData(MountType.Readonly)]
        [InlineData(MountType.NonHashable)]
        [InlineData(MountType.Undefined)]
        public void ValidateCachingCacheableAllowlistFileInput(int mountType)
        {
            // start with absent /allowlistFile
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(m_mounts[mountType]));
            FileArtifact allowlistFile = new FileArtifact(CreateUniquePath(SourceRootPrefix, ArtifactToString(dir)));
            Process pip = CreateAndScheduleProcessUsingAllowlistFile(allowlistFile, fileIsInput: true, cacheableAllowlist: true);

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create absent /allowlistFile
            File.WriteAllText(ArtifactToString(allowlistFile), "abc");
            switch (mountType)
            {
                case MountType.Readonly:
                    // Existence of allowlisted files is still tracked for absent file probes
                    // This is consistent with including allowlisted files in directory fingerprints
                    RunScheduler().AssertCacheMiss(pip.PipId);
                    AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedCacheable);
                    break;
                case MountType.NonHashable:
                    // Absent file probes are not tracked for nonhashable mounts
                    break;
                case MountType.Undefined:
                    // Bug #1134297: This is inconsistent with our tracking of directory enumerations for allowlisted
                    // files under undefined mounts
                    RunScheduler().AssertCacheMiss(pip.PipId);
                    break;
                default:
                    return;
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /allowlistFile
            File.WriteAllText(ArtifactToString(allowlistFile), "xyz");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /allowlistFile
            File.Delete(ArtifactToString(allowlistFile));
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingCacheableAllowlistFileOutput(bool testCompatibility)
        {
            FileArtifact allowlistFile = CreateOutputFileArtifact();
            Process pip = CreateAndScheduleProcessUsingAllowlistFile(allowlistFile, fileIsInput: false, cacheableAllowlist: true, testCompatibility: testCompatibility);

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /allowlistFile
            File.Delete(ArtifactToString(allowlistFile));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Cache only replays declared outputs, so /allowlistFile remains deleted
            XAssert.IsFalse(File.Exists(ArtifactToString(allowlistFile)));
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Theory]
        [InlineData(true, MountType.Readonly)]
        [InlineData(true, MountType.NonHashable)]
        [InlineData(true, MountType.Undefined)]
        [InlineData(false, MountType.Readonly)]
        [InlineData(false, MountType.NonHashable)]
        [InlineData(false, MountType.Undefined)]
        public void ValidateCachingDirectoryEnumerationReadonlyMountAllowlistFile(bool cacheableAllowlist, int mountType)
        {
            // Read-only mounts are fingerprinted based on actual filesystem state

            // Start with absent /allowlistFile under /dir
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(m_mounts[mountType]));
            FileArtifact allowlistFile = new FileArtifact(CreateUniquePath(SourceRootPrefix, ArtifactToString(dir)));
            AddAllowlistEntry(allowlistFile, cacheableAllowlist: cacheableAllowlist);

            FileArtifact outFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outFile)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);
            string outputWithAbsentAllowlistFile = File.ReadAllText(ArtifactToString(outFile));

            // Create /allowlistFile
            File.WriteAllText(ArtifactToString(allowlistFile), "abc");
            switch (mountType)
            {
                case MountType.Readonly:
                    // allowlisted files still make it into directory fingerprints
                    // This is consistent with tracking existence of allowlisted files for absent file probes
                    RunScheduler().AssertCacheMiss(pip.PipId);
                    break;
                case MountType.NonHashable:
                    // Files in nonhashable mounts never make it into directory fingerprints
                    break;
                case MountType.Undefined:
                    // Files in undefined mounts never make it into directory fingerprints
                    // Bug 1134297: This is inconsistent with our tracking of absent file probes for allowlisted
                    // files under undefined mounts
                    break;
                default:
                    return;
            }
            RunScheduler().AssertCacheHit(pip.PipId);
            string outputWithExistingAllowlistFile = File.ReadAllText(ArtifactToString(outFile));

            // Delete /allowlistFile
            File.Delete(ArtifactToString(allowlistFile));
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(File.ReadAllText(ArtifactToString(outFile)), outputWithAbsentAllowlistFile);

            // Re-create /allowlistFile
            File.WriteAllText(ArtifactToString(allowlistFile), "xyz");
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(File.ReadAllText(ArtifactToString(outFile)), outputWithExistingAllowlistFile);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Fact]
        public void ValidateCachingDirectoryEnumerationReadWriteMountCacheableAllowlistFile()
        {
            // Writable mounts are fingeprinted based on the static build graph. Examining the filesystem state under these mounts is not reliable since
            // it is expected to change concurrently with directory fingerprinting.
            
            // Start with absent /allowlistFile under /dir
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ObjectRoot));
            FileArtifact allowlistFile = new FileArtifact(CreateUniquePath(SourceRootPrefix, ArtifactToString(dir)));
            AddAllowlistEntry(allowlistFile: allowlistFile, cacheableAllowlist: true);

            // PipA enumerates directory /dir, creates file /dir/outA and /dir/allowlistFile
            // /outA is used to enforce pipA runs before pipB
            FileArtifact outA = CreateOutputFileArtifact();
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outA),
                Operation.WriteFile(allowlistFile, doNotInfer: true),
            }).Process;

            // PipB consumes /dir/outA and /dir/allowlistFile, creates file /dir/outB
            FileArtifact outB = CreateOutputFileArtifact(ArtifactToString(dir));
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(allowlistFile, doNotInfer: true),
                Operation.ReadFile(outA),
                Operation.WriteFile(outB)
            }).Process;

            RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);
            RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Delete files in enumerated directory
            File.Delete(ArtifactToString(allowlistFile));
            File.Delete(ArtifactToString(outA));
            File.Delete(ArtifactToString(outB));

            RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Cache only replays declared outputs, so /allowlistFile remains deleted
            XAssert.IsFalse(File.Exists(ArtifactToString(allowlistFile)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outB)));
        }

        /// <summary>
        /// Checks that allowlist files are not cached by default
        /// </summary>
        /// <param name="fileIsInput">
        /// When true, the pip consumes a cacheable allowlisted file. 
        /// When false, the pip outputs a cacheable allowlisted file.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateNonCacheableAllowlist(bool fileIsInput)
        {
            FileArtifact allowlistFile = fileIsInput ? CreateSourceFile() : CreateOutputFileArtifact();
            Process pip = CreateAndScheduleProcessUsingAllowlistFile(allowlistFile, fileIsInput: fileIsInput);

            // Pip should succeed despite undeclared file accesses
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Default allowlist files cannot be cached
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Each event logged once per scheduler run
            AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable, count: 2, allowMore: OperatingSystemHelper.IsUnixOS);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 2);
        }

        /// <summary>
        /// Checks that caching policies for declared dependencies and outputs take priority
        /// over caching policies for files that are also on a non-cacheable allowlist
        /// </summary>
        /// <param name="fileIsInput">
        /// When true, the pip consumes a non-cacheable allowlisted file. 
        /// When false, the pip outputs a non-cacheable allowlisted file.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IgnoreNonCacheableAllowlistIfFileDeclared(bool fileIsInput)
        {
            FileArtifact allowlistFile = fileIsInput ? CreateSourceFile() : CreateOutputFileArtifact();
            Process pip = CreateAndScheduleProcessUsingAllowlistFile(allowlistFile, fileIsInput: fileIsInput, declareAllowlistFile: true);

            RunScheduler().AssertCacheMiss(pip.PipId);

            // Cache declared dependencies/outputs even if they are on non-cacheable allowlist
            RunScheduler().AssertCacheHit(pip.PipId);

            XAssert.IsTrue(File.Exists(ArtifactToString(allowlistFile)));
        }

        /// <summary>
        /// Consuming a allowlisted file specified in a partially sealed directory should be allowed with no warning
        /// </summary>
        [Feature(Features.SealedDirectory)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowConsumingAllowlistFilePartialSealedDirectory(bool cacheableAllowlist)
        {
            // Create src/allowlistFile
            FileArtifact allowlistFile = CreateSourceFile(SourceRoot);
            AddAllowlistEntry(allowlistFile, cacheableAllowlist: cacheableAllowlist);

            // Seal all of /src
            DirectoryArtifact sealedDir = SealDirectory(SourceRootPath, SealDirectoryKind.Partial, allowlistFile);

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(allowlistFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDir);
            Process pip = SchedulePipBuilder(pipBuilder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // No allowlist file access warnings logged
            AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable, 0);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 0);
        }

        /// <summary>
        /// Checks that allowlist file access policies take priority over sealed directory file access policies
        /// (i.e. a allowlist file can be produced as output even in a sealed directory)
        /// </summary>
        [Feature(Features.SealedSourceDirectory)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowAllowlistFileOutputInSealedSourceDirectory(bool cacheableAllowlist)
        {
            DirectoryArtifact sealedDir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);
            FileArtifact allowlistFile = CreateOutputFileArtifact(ArtifactToString(sealedDir));
            AddAllowlistEntry(allowlistFile, cacheableAllowlist: cacheableAllowlist);

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(allowlistFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDir);
            SchedulePipBuilder(pipBuilder);

            // allowlist file access policy takes priority over sealed source directory file access policy
            RunScheduler().AssertSuccess();

            if (!cacheableAllowlist)
            {
                // allowlist file access warnings
                AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable, allowMore: OperatingSystemHelper.IsUnixOS);
                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            }
        }

        [Fact]
        public void ValidateCachingCacheableToNonCacheableAllowlist()
        {
            FileArtifact allowlistFile = CreateSourceFile();
            Process pip = CreateAndScheduleProcessUsingAllowlistFile(allowlistFile, fileIsInput: true, cacheableAllowlist: true);

            // \allowlistFile starts on a cacheable allowlist
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // remove from cacheable allowlist
            Configuration.CacheableFileAccessAllowlist.Clear();
            // add to non-cacheable allowlist
            AddAllowlistEntry(allowlistFile, cacheableAllowlist: false);

            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowlistOnSpawnProcess(bool includeExecutableLink)
        {
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, CmdHelper.OsShellExe));
            FileArtifact exeLink = FileArtifact.CreateSourceFile(CreateUniqueSourcePath());
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(exeLink.Path.ToString(Context.PathTable), exe.Path.ToString(Context.PathTable), true).Succeeded);

            Configuration.CacheableFileAccessAllowlist.Add(new Configuration.Mutable.FileAccessAllowlistEntry() { ToolPath = exeLink, PathRegex = ".*" });

            FileArtifact output = CreateOutputFileArtifact();

            var builder = CreatePipBuilder(new[]
            {
                Operation.SpawnExe(
                    Context.PathTable,
                    exeLink,
                    string.Format(OperatingSystemHelper.IsUnixOS ? "-c \"echo 'hi' > {0}\"" : "/d /c echo 'hi' > {0}", output.Path.ToString(Context.PathTable))),
            });
            builder.AddOutputFile(output.Path);

            if (includeExecutableLink)
            {
                builder.AddInputFile(exeLink);
            }

            foreach (var dep in CmdHelper.GetCmdDependencies(Context.PathTable))
            {
                builder.AddUntrackedFile(dep);
            }

            foreach (var dep in CmdHelper.GetCmdDependencyScopes(Context.PathTable))
            {
                builder.AddUntrackedDirectoryScope(dep);
            }

            SchedulePipBuilder(builder);

            if (includeExecutableLink)
            {
                RunScheduler().AssertSuccess();
                XAssert.AreEqual("hi", File.ReadAllText(ArtifactToString(output)).Trim().Trim('\''));
            }
            else
            {
                RunScheduler().AssertFailure();

                // DFA on exeLink because it is not specified as input.
                // Although there's a cacheable allowlist entry for exeLink, that entry only holds for file accessed by exeLink.
                // In this case, exeLink is accessed by the test process, so there's a read operation by the test process on exeLink, hence DFA.
                AssertErrorEventLogged(LogEventId.FileMonitoringError, 1);
                AssertLogContains(false, $"R  {exeLink.Path.ToString(Context.PathTable)}");

                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 1);
            }
        }



        /// <summary>
        /// Creates and schedules a pip that either consumes or outputs a (default) non-cacheable allowlist file
        /// </summary>
        /// <param name="fileIsInput">
        /// When true, the pip consumes a allowlisted file. 
        /// When false, the pip outputs a allowlisted file.
        /// </param>
        /// <param name="cacheableAllowlist">
        /// When true, the file is added to the cacheable allowlist.
        /// When false, the file is added to the default non-cacheable allowlist.
        /// </param>
        /// <param name="declareAllowlistFile">
        /// When true, the pip declares the allowlisted file as a dependency or output. 
        /// When false, the pip does not declare the allowlisted file.
        /// </param>
        protected Process CreateAndScheduleProcessUsingAllowlistFile(FileArtifact allowlistFile, bool fileIsInput, bool cacheableAllowlist = false, bool declareAllowlistFile = false, bool testCompatibility = false)
        {
            Operation allowlistOp = fileIsInput ? 
                Operation.ReadFile(allowlistFile, doNotInfer: !declareAllowlistFile) : 
                Operation.WriteFile(allowlistFile, doNotInfer: !declareAllowlistFile);

            AddAllowlistEntry(allowlistFile, cacheableAllowlist: cacheableAllowlist, testCompatibility);

            return CreateAndSchedulePipBuilder(new Operation[]
            {
                allowlistOp,
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;
        }

        protected void AddAllowlistEntry(FileArtifact allowlistFile, bool cacheableAllowlist = false, bool testCompatibility = false)
        {
            var entry = new Configuration.Mutable.FileAccessAllowlistEntry()
            {
                Value = "testValue",
                PathFragment = ArtifactToString(allowlistFile),
            };

            if (testCompatibility)
            {
                if (cacheableAllowlist)
                {
                    Configuration.CacheableFileAccessWhitelist.Add(entry);
                }
                else
                {
                    Configuration.FileAccessWhiteList.Add(entry);
                }
            }
            else
            {
                if (cacheableAllowlist)
                {
                    Configuration.CacheableFileAccessAllowlist.Add(entry);
                }
                else
                {
                    Configuration.FileAccessAllowList.Add(entry);
                }
            }
        }
    }
}
