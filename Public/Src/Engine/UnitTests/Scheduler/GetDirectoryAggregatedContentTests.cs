// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    [Trait("Category", "FileContentManagerTests")]
    public sealed class GetDirectoryAggregatedContentTests : SchedulerTestBase
    {
        public GetDirectoryAggregatedContentTests(ITestOutputHelper output) : base(output) { }

        private (DummyPipExecutionEnvironment env, FileContentManager fcm) CreateEnvironment()
        {
            var configFile = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "config.dsc"));
            var config = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, configFile);
            var env = new DummyPipExecutionEnvironment(CreateLoggingContextForTest(), Context, config, sandboxConnection: GetEBPFAwareSandboxConnection());
            var fcm = new FileContentManager(env, new NullOperationTracker());
            return (env, fcm);
        }

        private void SetupDirectoryWithFiles(
            DummyPipExecutionEnvironment env,
            FileContentManager fcm,
            DirectoryArtifact directory,
            params (FileArtifact file, string content)[] files)
        {
            var fileArtifactsWithAttributes = new List<FileArtifactWithAttributes>();
            for (int i = 0; i < files.Length; i++)
            {
                fileArtifactsWithAttributes.Add(FileArtifactWithAttributes.Create(files[i].file, FileExistence.Required));
                var bytes = Encoding.UTF8.GetBytes(files[i].content);
                var hash = ContentHashingUtilities.HashBytes(bytes);
                fcm.ReportInputContent(
                    files[i].file,
                    FileMaterializationInfo.CreateWithUnknownName(new FileContentInfo(hash, bytes.Length)));
            }

            env.RegisterDynamicOutputDirectory(directory);
            fcm.ReportDynamicDirectoryContents(directory, fileArtifactsWithAttributes, PipOutputOrigin.Produced);
        }

        [Fact]
        public void SameContentAtDifferentRootsProducesSameHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            // Create two directories at different roots, with same file names and content
            var root1 = AbsolutePath.Create(pathTable, A("t", "root1", "dir"));
            var root2 = AbsolutePath.Create(pathTable, A("t", "root2", "dir"));

            var dir1 = new DirectoryArtifact(root1, 1, isSharedOpaque: false);
            var dir2 = new DirectoryArtifact(root2, 1, isSharedOpaque: false);

            var file1InDir1 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root1", "dir", "a.txt")));
            var file2InDir1 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root1", "dir", "b.txt")));
            var file1InDir2 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root2", "dir", "a.txt")));
            var file2InDir2 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root2", "dir", "b.txt")));

            SetupDirectoryWithFiles(env, fcm, dir1,
                (file1InDir1, "hello"),
                (file2InDir1, "world"));

            SetupDirectoryWithFiles(env, fcm, dir2,
                (file1InDir2, "hello"),
                (file2InDir2, "world"));

            var hash1 = fcm.GetDirectoryAggregatedContent(dir1);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir2);

            XAssert.AreEqual(hash1.Hash, hash2.Hash, "Same content at different roots should produce the same aggregated hash");
        }

        [Fact]
        public void DifferentContentProducesDifferentHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            var root1 = AbsolutePath.Create(pathTable, A("t", "root1", "dir"));
            var root2 = AbsolutePath.Create(pathTable, A("t", "root2", "dir"));

            var dir1 = new DirectoryArtifact(root1, 1, isSharedOpaque: false);
            var dir2 = new DirectoryArtifact(root2, 1, isSharedOpaque: false);

            var file1InDir1 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root1", "dir", "a.txt")));
            var file1InDir2 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root2", "dir", "a.txt")));

            SetupDirectoryWithFiles(env, fcm, dir1,
                (file1InDir1, "content-v1"));

            SetupDirectoryWithFiles(env, fcm, dir2,
                (file1InDir2, "content-v2"));

            var hash1 = fcm.GetDirectoryAggregatedContent(dir1);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir2);

            XAssert.AreNotEqual(hash1.Hash, hash2.Hash, "Different content should produce different aggregated hashes");
        }

        [Fact]
        public void DifferentLayoutProducesDifferentHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            // Same content but different file names (different layout)
            var root1 = AbsolutePath.Create(pathTable, A("t", "root1", "dir"));
            var root2 = AbsolutePath.Create(pathTable, A("t", "root2", "dir"));

            var dir1 = new DirectoryArtifact(root1, 1, isSharedOpaque: false);
            var dir2 = new DirectoryArtifact(root2, 1, isSharedOpaque: false);

            var fileA = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root1", "dir", "a.txt")));
            var fileB = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root2", "dir", "b.txt")));

            SetupDirectoryWithFiles(env, fcm, dir1,
                (fileA, "same-content"));

            SetupDirectoryWithFiles(env, fcm, dir2,
                (fileB, "same-content"));

            var hash1 = fcm.GetDirectoryAggregatedContent(dir1);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir2);

            XAssert.AreNotEqual(hash1.Hash, hash2.Hash, "Different file layouts should produce different aggregated hashes");
        }

        [Fact]
        public void EmptyDirectoryProducesStableHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            var root1 = AbsolutePath.Create(pathTable, A("t", "root1", "emptyDir"));
            var root2 = AbsolutePath.Create(pathTable, A("t", "root2", "emptyDir"));

            var dir1 = new DirectoryArtifact(root1, 1, isSharedOpaque: false);
            var dir2 = new DirectoryArtifact(root2, 1, isSharedOpaque: false);

            env.RegisterDynamicOutputDirectory(dir1);
            fcm.ReportDynamicDirectoryContents(dir1, new List<FileArtifactWithAttributes>(), PipOutputOrigin.Produced);
            env.RegisterDynamicOutputDirectory(dir2);
            fcm.ReportDynamicDirectoryContents(dir2, new List<FileArtifactWithAttributes>(), PipOutputOrigin.Produced);

            var hash1 = fcm.GetDirectoryAggregatedContent(dir1);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir2);

            XAssert.AreEqual(hash1.Hash, hash2.Hash, "Empty directories should produce the same hash");
        }

        [Fact]
        public void SharedOpaqueDirectoryProducesCorrectHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            var root = AbsolutePath.Create(pathTable, A("t", "shared", "dir"));
            var sharedDir = new DirectoryArtifact(root, 1, isSharedOpaque: true);

            var file1 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "shared", "dir", "file1.txt")));

            SetupDirectoryWithFiles(env, fcm, sharedDir,
                (file1, "shared-content"));

            var hash = fcm.GetDirectoryAggregatedContent(sharedDir);

            // Hash should be non-zero
            XAssert.AreNotEqual(ContentHashingUtilities.ZeroHash, hash.Hash, "Shared opaque directory hash should be non-zero");
        }

        [Fact]
        public void HashIsStableAcrossMultipleCalls()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            var root = AbsolutePath.Create(pathTable, A("t", "stable", "dir"));
            var dir = new DirectoryArtifact(root, 1, isSharedOpaque: false);

            var file1 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "stable", "dir", "a.txt")));
            var file2 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "stable", "dir", "b.txt")));

            SetupDirectoryWithFiles(env, fcm, dir,
                (file1, "hello"),
                (file2, "world"));

            var hash1 = fcm.GetDirectoryAggregatedContent(dir);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir);

            XAssert.AreEqual(hash1.Hash, hash2.Hash, "Hash should be stable across multiple calls");
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void WindowsCaseInsensitivePathsProduceSameHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            // On Windows, paths are case-insensitive, so files differing only in casing
            // should produce the same aggregated hash
            var root1 = AbsolutePath.Create(pathTable, A("t", "root1", "dir"));
            var root2 = AbsolutePath.Create(pathTable, A("t", "root2", "dir"));

            var dir1 = new DirectoryArtifact(root1, 1, isSharedOpaque: false);
            var dir2 = new DirectoryArtifact(root2, 1, isSharedOpaque: false);

            // On Windows PathTable is case-insensitive, but the expansion through ExpandRelative
            // will produce paths in the original case stored in the path table.
            // However, ToCanonicalizedPath() will upper-case both, making them equal.
            var file1 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root1", "dir", "File.TXT")));
            var file2 = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root2", "dir", "file.txt")));

            SetupDirectoryWithFiles(env, fcm, dir1,
                (file1, "same-content"));

            SetupDirectoryWithFiles(env, fcm, dir2,
                (file2, "same-content"));

            var hash1 = fcm.GetDirectoryAggregatedContent(dir1);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir2);

            XAssert.AreEqual(hash1.Hash, hash2.Hash,
                "On Windows, paths differing only in casing should produce the same aggregated hash");
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void LinuxCaseSensitivePathsProduceDifferentHash()
        {
            var (env, fcm) = CreateEnvironment();
            var pathTable = Context.PathTable;

            // On Linux, paths are case-sensitive, so files differing in casing
            // should produce different aggregated hashes
            var root1 = AbsolutePath.Create(pathTable, A("t", "root1", "dir"));
            var root2 = AbsolutePath.Create(pathTable, A("t", "root2", "dir"));

            var dir1 = new DirectoryArtifact(root1, 1, isSharedOpaque: false);
            var dir2 = new DirectoryArtifact(root2, 1, isSharedOpaque: false);

            // On Linux PathTable is case-sensitive, so these create distinct paths
            var upperFile = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root1", "dir", "File.TXT")));
            var lowerFile = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, A("t", "root2", "dir", "file.txt")));

            SetupDirectoryWithFiles(env, fcm, dir1,
                (upperFile, "same-content"));

            SetupDirectoryWithFiles(env, fcm, dir2,
                (lowerFile, "same-content"));

            var hash1 = fcm.GetDirectoryAggregatedContent(dir1);
            var hash2 = fcm.GetDirectoryAggregatedContent(dir2);

            XAssert.AreNotEqual(hash1.Hash, hash2.Hash,
                "On Linux, paths differing in casing should produce different aggregated hashes");
        }
    }
}