// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using System.IO;
using Test.BuildXL.Scheduler;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Configuration = BuildXL.Utilities.Configuration;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "WhitelistTests")]
    [Feature(Features.Whitelist)]
    public class WhitelistTests : SchedulerIntegrationTestBase
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

        public WhitelistTests(ITestOutputHelper output) : base(output)
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
        public void ValidateCachingCacheableWhitelistFileInput(int mountType)
        {
            // start with absent /whitelistFile
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(m_mounts[mountType]));
            FileArtifact whitelistFile = new FileArtifact(CreateUniquePath(SourceRootPrefix, ArtifactToString(dir)));
            Process pip = CreateAndScheduleProcessUsingWhitelistFile(whitelistFile, fileIsInput: true, cacheableWhitelist: true);

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create absent /whitelistFile
            File.WriteAllText(ArtifactToString(whitelistFile), "abc");
            switch (mountType)
            {
                case MountType.Readonly:
                    // Existence of whitelisted files is still tracked for absent file probes
                    // This is consistent with including whitelisted files in directory fingerprints
                    RunScheduler().AssertCacheMiss(pip.PipId);
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedCacheable);
                    break;
                case MountType.NonHashable:
                    // Absent file probes are not tracked for nonhashable mounts
                    break;
                case MountType.Undefined:
                    // Bug #1134297: This is inconsistent with our tracking of directory enumerations for whitelisted
                    // files under undefined mounts
                    RunScheduler().AssertCacheMiss(pip.PipId);
                    break;
                default:
                    return;
            }
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /whitelistFile
            File.WriteAllText(ArtifactToString(whitelistFile), "xyz");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /whitelistFile
            File.Delete(ArtifactToString(whitelistFile));
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Fact]
        public void ValidateCachingCacheableWhitelistFileOutput()
        {
            FileArtifact whitelistFile = CreateOutputFileArtifact();
            Process pip = CreateAndScheduleProcessUsingWhitelistFile(whitelistFile, fileIsInput: false, cacheableWhitelist: true);

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete /whitelistFile
            File.Delete(ArtifactToString(whitelistFile));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Cache only replays declared outputs, so /whitelistFile remains deleted
            XAssert.IsFalse(File.Exists(ArtifactToString(whitelistFile)));
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
        public void ValidateCachingDirectoryEnumerationReadonlyMountWhitelistFile(bool cacheableWhitelist, int mountType)
        {
            // Read-only mounts are fingerprinted based on actual filesystem state

            // Start with absent /whitelistFile under /dir
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(m_mounts[mountType]));
            FileArtifact whitelistFile = new FileArtifact(CreateUniquePath(SourceRootPrefix, ArtifactToString(dir)));
            AddWhitelistEntry(whitelistFile, cacheableWhitelist: cacheableWhitelist);

            FileArtifact outFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outFile)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);
            string outputWithAbsentWhitelistFile = File.ReadAllText(ArtifactToString(outFile));

            // Create /whitelistFile
            File.WriteAllText(ArtifactToString(whitelistFile), "abc");
            switch (mountType)
            {
                case MountType.Readonly:
                    // Whitelisted files still make it into directory fingerprints
                    // This is consistent with tracking existence of whitelisted files for absent file probes
                    RunScheduler().AssertCacheMiss(pip.PipId);
                    break;
                case MountType.NonHashable:
                    // Files in nonhashable mounts never make it into directory fingerprints
                    break;
                case MountType.Undefined:
                    // Files in undefined mounts never make it into directory fingerprints
                    // Bug 1134297: This is inconsistent with our tracking of absent file probes for whitelisted
                    // files under undefined mounts
                    break;
                default:
                    return;
            }
            RunScheduler().AssertCacheHit(pip.PipId);
            string outputWithExistingWhitelistFile = File.ReadAllText(ArtifactToString(outFile));

            // Delete /whitelistFile
            File.Delete(ArtifactToString(whitelistFile));
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(File.ReadAllText(ArtifactToString(outFile)), outputWithAbsentWhitelistFile);

            // Re-create /whitelistFile
            File.WriteAllText(ArtifactToString(whitelistFile), "xyz");
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(File.ReadAllText(ArtifactToString(outFile)), outputWithExistingWhitelistFile);
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.Mount)]
        [Fact]
        public void ValidateCachingDirectoryEnumerationReadWriteMountCacheableWhitelistFile()
        {
            // Writable mounts are fingeprinted based on the static build graph. Examining the filesystem state under these mounts is not reliable since
            // it is expected to change concurrently with directory fingerprinting.
            
            // Start with absent /whitelistFile under /dir
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ObjectRoot));
            FileArtifact whitelistFile = new FileArtifact(CreateUniquePath(SourceRootPrefix, ArtifactToString(dir)));
            AddWhitelistEntry(whitelistFile: whitelistFile, cacheableWhitelist: true);

            // PipA enumerates directory /dir, creates file /dir/outA and /dir/whitelistFile
            // /outA is used to enforce pipA runs before pipB
            FileArtifact outA = CreateOutputFileArtifact();
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outA),
                Operation.WriteFile(whitelistFile, doNotInfer: true),
            }).Process;

            // PipB consumes /dir/outA and /dir/whitelistFile, creates file /dir/outB
            FileArtifact outB = CreateOutputFileArtifact(ArtifactToString(dir));
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(whitelistFile, doNotInfer: true),
                Operation.ReadFile(outA),
                Operation.WriteFile(outB)
            }).Process;

            RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);
            RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Delete files in enumerated directory
            File.Delete(ArtifactToString(whitelistFile));
            File.Delete(ArtifactToString(outA));
            File.Delete(ArtifactToString(outB));

            RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Cache only replays declared outputs, so /whitelistFile remains deleted
            XAssert.IsFalse(File.Exists(ArtifactToString(whitelistFile)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outB)));
        }

        /// <summary>
        /// Checks that whitelist files are not cached by default
        /// </summary>
        /// <param name="fileIsInput">
        /// When true, the pip consumes a cacheable whitelisted file. 
        /// When false, the pip outputs a cacheable whitelisted file.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateNonCacheableWhitelist(bool fileIsInput)
        {
            FileArtifact whitelistFile = fileIsInput ? CreateSourceFile() : CreateOutputFileArtifact();
            Process pip = CreateAndScheduleProcessUsingWhitelistFile(whitelistFile, fileIsInput: fileIsInput);

            // Pip should succeed despite undeclared file accesses
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Default whitelist files cannot be cached
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Each event logged once per scheduler run
            AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable, count: 2, allowMore: OperatingSystemHelper.IsUnixOS);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 2);
        }

        /// <summary>
        /// Checks that caching policies for declared dependencies and outputs take priority
        /// over caching policies for files that are also on a non-cacheable whitelist
        /// </summary>
        /// <param name="fileIsInput">
        /// When true, the pip consumes a non-cacheable whitelisted file. 
        /// When false, the pip outputs a non-cacheable whitelisted file.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IgnoreNonCacheableWhitelistIfFileDeclared(bool fileIsInput)
        {
            FileArtifact whitelistFile = fileIsInput ? CreateSourceFile() : CreateOutputFileArtifact();
            Process pip = CreateAndScheduleProcessUsingWhitelistFile(whitelistFile, fileIsInput: fileIsInput, declareWhitelistFile: true);

            RunScheduler().AssertCacheMiss(pip.PipId);

            // Cache declared dependencies/outputs even if they are on non-cacheable whitelist
            RunScheduler().AssertCacheHit(pip.PipId);

            XAssert.IsTrue(File.Exists(ArtifactToString(whitelistFile)));
        }

        /// <summary>
        /// Consuming a whitelisted file specified in a partially sealed directory should be allowed with no warning
        /// </summary>
        [Feature(Features.SealedDirectory)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowConsumingWhitelistFilePartialSealedDirectory(bool cacheableWhitelist)
        {
            // Create src/whitelistFile
            FileArtifact whitelistFile = CreateSourceFile(SourceRoot);
            AddWhitelistEntry(whitelistFile, cacheableWhitelist: cacheableWhitelist);

            // Seal all of /src
            DirectoryArtifact sealedDir = SealDirectory(SourceRootPath, SealDirectoryKind.Partial, whitelistFile);

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(whitelistFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDir);
            Process pip = SchedulePipBuilder(pipBuilder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // No whitelist file access warnings logged
            AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable, 0);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 0);
        }

        /// <summary>
        /// Checks that whitelist file access policies take priority over sealed directory file access policies
        /// (i.e. a whitelist file can be produced as output even in a sealed directory)
        /// </summary>
        [Feature(Features.SealedSourceDirectory)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowWhitelistFileOutputInSealedSourceDirectory(bool cacheableWhitelist)
        {
            DirectoryArtifact sealedDir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);
            FileArtifact whitelistFile = CreateOutputFileArtifact(ArtifactToString(sealedDir));
            AddWhitelistEntry(whitelistFile, cacheableWhitelist: cacheableWhitelist);

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(whitelistFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDir);
            SchedulePipBuilder(pipBuilder);

            // Whitelist file access policy takes priority over sealed source directory file access policy
            RunScheduler().AssertSuccess();

            if (!cacheableWhitelist)
            {
                // Whitelist file access warnings
                AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable, allowMore: OperatingSystemHelper.IsUnixOS);
                AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            }
        }

        [Fact]
        public void ValidateCachingCacheableToNonCacheableWhitelist()
        {
            FileArtifact whitelistFile = CreateSourceFile();
            Process pip = CreateAndScheduleProcessUsingWhitelistFile(whitelistFile, fileIsInput: true, cacheableWhitelist: true);

            // \whitelistFile starts on a cacheable whitelist
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // remove from cacheable whitelist
            Configuration.CacheableFileAccessWhitelist.Clear();
            // add to non-cacheable whitelist
            AddWhitelistEntry(whitelistFile, cacheableWhitelist: false);

            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// Creates and schedules a pip that either consumes or outputs a (default) non-cacheable whitelist file
        /// </summary>
        /// <param name="fileIsInput">
        /// When true, the pip consumes a whitelisted file. 
        /// When false, the pip outputs a whitelisted file.
        /// </param>
        /// <param name="cacheableWhitelist">
        /// When true, the file is added to the cacheable whitelist.
        /// When false, the file is added to the default non-cacheable whitelist.
        /// </param>
        /// <param name="declareWhitelistFile">
        /// When true, the pip declares the whitelisted file as a dependency or output. 
        /// When false, the pip does not declare the whitelisted file.
        /// </param>
        protected Process CreateAndScheduleProcessUsingWhitelistFile(FileArtifact whitelistFile, bool fileIsInput, bool cacheableWhitelist = false, bool declareWhitelistFile = false)
        {
            Operation whitelistOp = fileIsInput ? 
                Operation.ReadFile(whitelistFile, doNotInfer: !declareWhitelistFile) : 
                Operation.WriteFile(whitelistFile, doNotInfer: !declareWhitelistFile);

            AddWhitelistEntry(whitelistFile, cacheableWhitelist: cacheableWhitelist);

            return CreateAndSchedulePipBuilder(new Operation[]
            {
                whitelistOp,
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;
        }

        protected void AddWhitelistEntry(FileArtifact whitelistFile, bool cacheableWhitelist = false)
        {
            Configuration.Mutable.FileAccessWhitelistEntry entry = new Configuration.Mutable.FileAccessWhitelistEntry()
            {
                Value = "testValue",
                PathFragment = ArtifactToString(whitelistFile),
            };

            if (cacheableWhitelist)
            {
                Configuration.CacheableFileAccessWhitelist.Add(entry);
            }
            else
            {
                Configuration.FileAccessWhiteList.Add(entry);
            }
        }
    }
}
