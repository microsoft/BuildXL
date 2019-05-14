// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.StorageTestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.Cache
{
    /// <summary>
    /// Tests for <see cref="LocalDiskContentStore"/> with a mocked content cache and change tracker (runs non-admin and without BuildCache).
    /// </summary>
    public class LocalDiskContentStoreTests : TemporaryStorageTestBase
    {
        public LocalDiskContentStoreTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Engine.Cache.ETWLogger.Log);
        }

        [Fact]
        public async Task DiscoveryTracksFileWithUnknownHash()
        {
            var harness = CreateHarness();

            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, "Fancy contents");

            Possible<ContentDiscoveryResult> possiblyDiscovered = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(tempPath));
            if (!possiblyDiscovered.Succeeded)
            {
                XAssert.Fail("Failed to discover content after initial write: {0}", possiblyDiscovered.Failure.DescribeIncludingInnerFailures());
            }

            ContentDiscoveryResult result = possiblyDiscovered.Result;
            XAssert.AreEqual(info, result.TrackedFileContentInfo.FileContentInfo, "Wrong file content info (incorrect hashing on discovery?)");
            XAssert.IsTrue(result.TrackedFileContentInfo.IsTracked, "Should have registered the file for tracking");
            XAssert.AreEqual(DiscoveredContentHashOrigin.NewlyHashed, result.Origin);

            harness.AssertPathIsTracked(tempPath);
            harness.AssertContentIsAbsentInLocalCache(info.Hash);
        }

        [Fact]
        public async Task DiscoveryTracksFileWithKnownHash()
        {
            var harness = CreateHarness();

            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, "Fancy contents");

            harness.AddPathToFileContentTable(tempPath, info.Hash);

            Possible<ContentDiscoveryResult> possiblyDiscovered = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(tempPath));
            if (!possiblyDiscovered.Succeeded)
            {
                XAssert.Fail("Failed to discover content after initial write: {0}", possiblyDiscovered.Failure.DescribeIncludingInnerFailures());
            }

            ContentDiscoveryResult result = possiblyDiscovered.Result;
            XAssert.AreEqual(info, result.TrackedFileContentInfo.FileContentInfo, "Wrong file content info (incorrect hashing on discovery?)");
            XAssert.IsTrue(result.TrackedFileContentInfo.IsTracked, "Should have registered the file for tracking");
            XAssert.AreEqual(DiscoveredContentHashOrigin.Cached, result.Origin);

            harness.AssertPathIsTracked(tempPath);
            harness.AssertContentIsAbsentInLocalCache(info.Hash);
        }

        [Fact]
        public async Task StoreWithUnknownHashTracksFileAndUpdatesCache()
        {
            var harness = CreateHarness();

            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, "Fancy contents");

            Possible<TrackedFileContentInfo> possiblyStored =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPath, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStored.Succeeded)
            {
                XAssert.Fail("Failed to store content after initial write: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
            }

            TrackedFileContentInfo storedInfo = possiblyStored.Result;
            XAssert.AreEqual(info, storedInfo.FileContentInfo, "Wrong file content info (incorrect hashing on store?)");
            XAssert.IsTrue(storedInfo.IsTracked, "Should have registered the file for tracking");

            harness.AssertPathIsTracked(tempPath);
            harness.AssertContentIsInLocalCache(info.Hash);
        }

        [Fact]
        public async Task StoreWithKnownHashTracksFileAndUpdatesCache()
        {
            var harness = CreateHarness();

            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, "Fancy contents");

            harness.AddPathToFileContentTable(tempPath, info.Hash);

            Possible<TrackedFileContentInfo> possiblyStored =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPath, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStored.Succeeded)
            {
                XAssert.Fail("Failed to store content after initial write: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
            }

            TrackedFileContentInfo storedInfo = possiblyStored.Result;
            XAssert.AreEqual(info, storedInfo.FileContentInfo, "Wrong file content info (incorrect hashing on store?)");
            XAssert.IsTrue(storedInfo.IsTracked, "Should have registered the file for tracking");

            harness.AssertPathIsTracked(tempPath);
            harness.AssertContentIsInLocalCache(info.Hash);
        }

        [Fact]
        public async Task StoreWorksWithReadOnlyAccess()
        {
            var harness = CreateHarness();

            AbsolutePath storedPath = harness.GetFullPath("STOREDFILE");
            AbsolutePath linkedPath = harness.GetFullPath("linkedFile");
            FileContentInfo info = harness.WriteFileAndHashContents(storedPath, "Fancy contents");
            var storedFilePath = storedPath.Expand(harness.Context.PathTable);
            var linkedFilePath = linkedPath.Expand(harness.Context.PathTable);

            // Create a file with the same content and
            // indicate to the harness to hardlink from the linked path
            File.Copy(storedFilePath.ExpandedPath, linkedFilePath.ExpandedPath);
            harness.ContentPaths[info.Hash] = linkedFilePath;

            // Change the file name casing from the name in the path table
            File.Move(storedFilePath.ExpandedPath, storedFilePath.ExpandedPath.ToLowerInvariant());

            // Only allow read sharing via the hardlink
            using (File.Open(linkedFilePath.ExpandedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Possible<TrackedFileContentInfo> possiblyStored =
                    await harness.Store.TryStoreAsync(harness, FileRealizationMode.HardLink, storedPath,
                        tryFlushPageCacheToFileSystem: true,
                        knownContentHash: info.Hash);
                if (!possiblyStored.Succeeded)
                {
                    XAssert.Fail("Failed to store content after initial write: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
                }

                TrackedFileContentInfo storedInfo = possiblyStored.Result;
                XAssert.AreEqual(info, storedInfo.FileContentInfo, "Wrong file content info (incorrect hashing on store?)");
                XAssert.IsTrue(storedInfo.IsTracked, "Should have registered the file for tracking");

                harness.AssertPathIsTracked(storedPath);
                harness.AssertContentIsInLocalCache(info.Hash);
            }
        }

        [Fact]
        public async Task MaterializeWorksWithReadOnlyFileShare()
        {
            var contentCache = new MockArtifactContentCache(Path.Combine(TemporaryDirectory, "Cache"));
            var harness = CreateHarness(useDummyFileContentTable: true, contentCacheForTest: contentCache);
            var pathTable = harness.Context.PathTable;

            AbsolutePath storedA = harness.GetFullPath("StoredA");
            FileContentInfo infoA = harness.WriteFileAndHashContents(storedA, "A");
            Possible<TrackedFileContentInfo> possiblyStored =
                    await harness.Store.TryStoreAsync(
                        harness,
                        FileRealizationMode.HardLink,
                        storedA,
                        tryFlushPageCacheToFileSystem: true,
                        knownContentHash: infoA.Hash);
            XAssert.IsTrue(
                possiblyStored.Succeeded, 
                "Failed to store content of '{0}' after initial write: {1}", 
                storedA.ToString(pathTable),
                possiblyStored.Succeeded ? string.Empty : possiblyStored.Failure.DescribeIncludingInnerFailures());

            AbsolutePath storedB = harness.GetFullPath("StoredB");
            FileContentInfo infoB = harness.WriteFileAndHashContents(storedB, "B");
            possiblyStored =
                    await harness.Store.TryStoreAsync(
                        harness,
                        FileRealizationMode.HardLink,
                        storedB,
                        tryFlushPageCacheToFileSystem: true,
                        knownContentHash: infoB.Hash);
            XAssert.IsTrue(
                possiblyStored.Succeeded, 
                "Failed to store content of '{0}' after initial write: {1}", 
                storedB.ToString(pathTable),
                possiblyStored.Succeeded ? string.Empty : possiblyStored.Failure.DescribeIncludingInnerFailures());

            string cachePathA = contentCache.GetCachePathFromContentHash(infoA.Hash);

            // Open cache path with read sharing only.
            using (File.Open(cachePathA, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                Possible<Unit, Failure> possiblyMaterialized = 
                    await harness.TryMaterializeAsync(
                        FileRealizationMode.HardLink, 
                        storedA.Expand(pathTable), 
                        infoB.Hash);

                XAssert.IsTrue(
                    possiblyMaterialized.Succeeded,
                    "Failed to materialized content to '{0}' after initial write: {1}",
                    storedA.ToString(pathTable),
                    possiblyMaterialized.Succeeded ? string.Empty : possiblyMaterialized.Failure.DescribeIncludingInnerFailures());
            }
        }

        [Fact]
        public async Task StoreForHashAlreadyInCacheStillTracksAdditionalFiles()
        {
            var harness = CreateHarness();

            AbsolutePath tempPathA = harness.GetFullPath("SomeFileA");
            AbsolutePath tempPathB = harness.GetFullPath("SomeFileB");

            const string Contents = "Fancy contents";
            FileContentInfo info = harness.WriteFileAndHashContents(tempPathA, Contents);
            harness.WriteFileAndHashContents(tempPathB, Contents);

            Possible<TrackedFileContentInfo> possiblyStoredA =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPathA, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStoredA.Succeeded)
            {
                XAssert.Fail("Failed to store content after initial write (file A): {0}", possiblyStoredA.Failure.DescribeIncludingInnerFailures());
            }

            TrackedFileContentInfo storedInfoA = possiblyStoredA.Result;
            XAssert.AreEqual(info, storedInfoA.FileContentInfo, "Wrong file content info (incorrect hashing on store?)");
            XAssert.IsTrue(storedInfoA.IsTracked, "Should have registered the file for tracking");

            harness.AssertPathIsTracked(tempPathA);
            harness.AssertPathIsNotTracked(tempPathB);
            harness.AssertContentIsInLocalCache(info.Hash);

            Possible<TrackedFileContentInfo> possiblyStoredB =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPathB, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStoredA.Succeeded)
            {
                XAssert.Fail("Failed to store content after initial write (file B): {0}", possiblyStoredB.Failure.DescribeIncludingInnerFailures());
            }

            TrackedFileContentInfo storedInfoB = possiblyStoredB.Result;
            XAssert.AreEqual(info, storedInfoB.FileContentInfo, "Wrong file content info (incorrect hashing on store?)");
            XAssert.IsTrue(storedInfoB.IsTracked, "Should have registered the file for tracking");

            harness.AssertPathIsTracked(tempPathA);
            harness.AssertPathIsTracked(tempPathB);
            harness.AssertContentIsInLocalCache(info.Hash);
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task MaterializeSucceedsForStoredContentAfterOriginDeletion()
        {
            var harness = CreateHarness();

            const string Contents = "Fancy contents";
            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, Contents);

            Possible<TrackedFileContentInfo> possiblyStored =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPath, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStored.Succeeded)
            {
                XAssert.Fail("Failed to store content after initial write: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
            }

            harness.AssertPathIsTracked(tempPath);
            FileUtilities.DeleteFile(tempPath.ToString(harness.Context.PathTable));

            harness.AssertContentIsInLocalCache(info.Hash);

            Possible<ContentMaterializationResult> possiblyMaterialized =
                await harness.Store.TryMaterializeAsync(
                    harness.ContentCache,
                    FileRealizationMode.Copy,
                    tempPath,
                    info.Hash);
            if (!possiblyMaterialized.Succeeded)
            {
                XAssert.Fail("Failed to materialize content after store: {0}", possiblyMaterialized.Failure.DescribeIncludingInnerFailures());
            }

            ContentMaterializationResult materializationResult = possiblyMaterialized.Result;

            XAssert.AreEqual(ContentMaterializationOrigin.DeployedFromCache, materializationResult.Origin);
            XAssert.IsTrue(materializationResult.TrackedFileContentInfo.IsTracked, "Materialized file should have a valid tracking subscription");
            XAssert.AreEqual(info.Hash, materializationResult.TrackedFileContentInfo.Hash, "Wrong hash reported by TryMaterializeAsync");

            harness.AssertPathIsTracked(tempPath);
            harness.AssertContentIsInLocalCache(info.Hash);
            XAssert.AreEqual(Contents, File.ReadAllText(tempPath.ToString(harness.Context.PathTable)));
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task MaterializingTransientWritableCopyReplacesFileIfUpToDate()
        {
            var harness = CreateHarness();

            const string Contents = "Fancy contents";
            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, Contents);

            Possible<TrackedFileContentInfo> possiblyStored =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPath, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStored.Succeeded)
            {
                XAssert.Fail("Failed to store content after initial write: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
            }

            VersionedFileIdentityAndContentInfo identityAndContentInfoBeforeReplacement = harness.AddPathToFileContentTable(tempPath, info.Hash);

            Possible<Unit> possiblyMaterialized =
                await harness.Store.TryMaterializeTransientWritableCopyAsync(
                    harness.ContentCache,
                    tempPath,
                    info.Hash);
            if (!possiblyMaterialized.Succeeded)
            {
                XAssert.Fail("Failed to materialize content (transient) after initial write and store: {0}", possiblyMaterialized.Failure.DescribeIncludingInnerFailures());
            }

            XAssert.AreEqual(Contents, File.ReadAllText(tempPath.ToString(harness.Context.PathTable)));

            // Transient materialization should replace the file always, and should never track it.
            harness.AssertPathIsNotTracked(tempPath);
            VersionedFileIdentityAndContentInfo newIdentityAndContentInfo = harness.AddPathToFileContentTable(tempPath, info.Hash);
            XAssert.AreNotEqual(newIdentityAndContentInfo.Identity.FileId, identityAndContentInfoBeforeReplacement.Identity.FileId, "Expected a new file");
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task MaterializationReplacesFileIfHashMismatched()
        {
            var harness = CreateHarness();

            const string ContentsA = "Fancy contents";
            const string ContentsB = "Even fancier contents";
            AbsolutePath tempPathA = harness.GetFullPath("SomeFileA");
            AbsolutePath tempPathB = harness.GetFullPath("SomeFileB");
            FileContentInfo infoA = harness.WriteFileAndHashContents(tempPathA, ContentsA);
            FileContentInfo infoB = harness.WriteFileAndHashContents(tempPathB, ContentsB);

            Possible<TrackedFileContentInfo> possiblyStoredA =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPathA, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStoredA.Succeeded)
            {
                XAssert.Fail("Failed to store content A after initial write: {0}", possiblyStoredA.Failure.DescribeIncludingInnerFailures());
            }

            XAssert.AreEqual(infoA.Hash, possiblyStoredA.Result.FileContentInfo.Hash);

            Possible<TrackedFileContentInfo> possiblyStoredB =
                await harness.Store.TryStoreAsync(harness.ContentCache, FileRealizationMode.Copy, tempPathB, tryFlushPageCacheToFileSystem: true);
            if (!possiblyStoredB.Succeeded)
            {
                XAssert.Fail("Failed to store content B after initial write: {0}", possiblyStoredB.Failure.DescribeIncludingInnerFailures());
            }

            XAssert.AreEqual(infoB.Hash, possiblyStoredB.Result.FileContentInfo.Hash);

            // Replace B with the content stored for A
            Possible<ContentMaterializationResult> possiblyMaterialized =
                await harness.Store.TryMaterializeAsync(
                    harness.ContentCache,
                    FileRealizationMode.Copy,
                    tempPathB,
                    infoA.Hash);
            if (!possiblyMaterialized.Succeeded)
            {
                XAssert.Fail("Failed to materialize content: {0}", possiblyMaterialized.Failure.DescribeIncludingInnerFailures());
            }

            ContentMaterializationResult materializationResult = possiblyMaterialized.Result;

            XAssert.AreEqual(ContentMaterializationOrigin.DeployedFromCache, materializationResult.Origin, "Should have replaced the mismatched file (not up to date)");
            XAssert.IsTrue(materializationResult.TrackedFileContentInfo.IsTracked, "Materialized file should have a valid tracking subscription");
            XAssert.AreEqual(infoA.Hash, materializationResult.TrackedFileContentInfo.Hash, "Wrong hash reported by TryMaterializeAsync (should be the new hash, not the old hash)");

            harness.AssertPathIsTracked(tempPathB);
            harness.AssertContentIsInLocalCache(infoA.Hash);
            harness.AssertContentIsInLocalCache(infoB.Hash);
            XAssert.AreEqual(ContentsA, File.ReadAllText(tempPathB.ToString(harness.Context.PathTable)));
        }

        [Fact]
        public async Task MaterializeFixpoint()
        {
            var harness = CreateHarness();

            const string ContentsA = "Fancy contents";
            const string ContentsB = "These are not the right contents";
            AbsolutePath tempPathIngress = harness.GetFullPath("SomeFileA");
            AbsolutePath tempPathEgress = harness.GetFullPath("SomeFileB");

            FileContentInfo infoIngress = harness.WriteFileAndHashContents(tempPathIngress, ContentsA);
            FileContentInfo infoEgress = harness.WriteFileAndHashContents(tempPathEgress, ContentsB);

            await harness.VerifyStore(tempPathEgress, infoEgress.Hash);
            await harness.VerifyStore(tempPathIngress, infoIngress.Hash);

            await harness.VerifyMaterialization(tempPathEgress, infoIngress.Hash, ContentMaterializationOrigin.DeployedFromCache);
            await harness.VerifyMaterialization(tempPathEgress, infoIngress.Hash, ContentMaterializationOrigin.UpToDate);
            await harness.VerifyMaterialization(tempPathEgress, infoIngress.Hash, ContentMaterializationOrigin.UpToDate);

            await harness.VerifyMaterialization(tempPathIngress, infoEgress.Hash, ContentMaterializationOrigin.DeployedFromCache);
            await harness.VerifyMaterialization(tempPathIngress, infoEgress.Hash, ContentMaterializationOrigin.UpToDate);
            await harness.VerifyMaterialization(tempPathIngress, infoEgress.Hash, ContentMaterializationOrigin.UpToDate);
        }

        /// <summary>
        /// Sometimes, file version info is not available, in which case we shouldn't be able to elide copies.
        /// </summary>
        [Fact]
        public async Task MaterializeLacksFixpointIfFileVersionsUnavailable()
        {
            // We pretend file versions are unavailable by using a dummy FCT. If the LocalDiskContentStore finds its own versions, this has to change.
            var harness = CreateHarness(useDummyFileContentTable: true);

            const string Contents = "Fancy contents";
            AbsolutePath tempPath = harness.GetFullPath("SomeFileA");

            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, Contents);

            await harness.VerifyStore(tempPath, info.Hash);

            await harness.VerifyMaterialization(tempPath, info.Hash, ContentMaterializationOrigin.DeployedFromCache);
            await harness.VerifyMaterialization(tempPath, info.Hash, ContentMaterializationOrigin.DeployedFromCache);
        }

        /// <summary>
        /// Sometimes, file version info is not available, in which case we shouldn't be able to track changes.
        /// </summary>
        [Fact]
        public async Task DiscoveryDoesNotTrackFileIfFileVersionsUnavailable()
        {
            // We pretend file versions are unavailable by using a dummy FCT. If the LocalDiskContentStore finds its own versions, this has to change.
            var harness = CreateHarness(useDummyFileContentTable: true);

            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, "Fancy contents");

            Possible<ContentDiscoveryResult> possiblyDiscovered = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(tempPath));
            if (!possiblyDiscovered.Succeeded)
            {
                XAssert.Fail("Failed to discover content after initial write: {0}", possiblyDiscovered.Failure.DescribeIncludingInnerFailures());
            }

            ContentDiscoveryResult result = possiblyDiscovered.Result;
            XAssert.AreEqual(info, result.TrackedFileContentInfo.FileContentInfo, "Wrong file content info (incorrect hashing on discovery?)");
            XAssert.IsFalse(result.TrackedFileContentInfo.IsTracked, "Should not have registered the file for tracking, since the FCT should return an anonymous version");
            XAssert.AreEqual(DiscoveredContentHashOrigin.NewlyHashed, result.Origin);

            harness.AssertPathIsNotTracked(tempPath);
            harness.AssertContentIsAbsentInLocalCache(info.Hash);
        }

        [Fact]
        public void ProbeExistentFile()
        {
            var harness = CreateHarness();

            const string Contents = "Fancy contents";
            AbsolutePath tempPath = harness.GetFullPath("SomeFileA");

            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, Contents);

            harness.AssertExistenceOfPathIsNotTracked(tempPath);

            var probeResult = harness.Store.TryProbeAndTrackPathForExistence(tempPath);
            XAssert.IsTrue(probeResult.Succeeded);
            XAssert.AreEqual(PathExistence.ExistsAsFile, probeResult.Result);

            harness.AssertExistenceOfPathIsTracked(tempPath);
        }

        [Fact]
        public void ProbeNonexistentPath()
        {
            var harness = CreateHarness();

            AbsolutePath tempPath = harness.GetFullPath("SomeFileA");

            harness.AssertExistenceOfPathIsNotTracked(tempPath);

            var probeResult = harness.Store.TryProbeAndTrackPathForExistence(tempPath);
            XAssert.IsTrue(probeResult.Succeeded);
            XAssert.AreEqual(PathExistence.Nonexistent, probeResult.Result);

            harness.AssertExistenceOfPathIsTracked(tempPath);
        }

        [Fact]
        public void VerifyChangeTrackingSelection()
        {
            var harness = CreateHarness();
            const string Contents = "Fancy contents";

            harness.FileChangeTrackingInclusionRoots.Add(harness.GetFullPath("inc"));
            harness.FileChangeTrackingExclusionRoots.Add(harness.GetFullPath("inc", "sub", "untracked"));
            harness.FileChangeTrackingExclusionRoots.Add(harness.GetFullPath("exc"));
            harness.FileChangeTrackingInclusionRoots.Add(harness.GetFullPath("exc", "dir", "tracked"));

            var trackedFilePaths = new AbsolutePath[]
            {
                harness.GetFullPath("inc", "a.txt"),
                harness.GetFullPath("inc", "b.txt"),
                harness.GetFullPath("inc", "sub", "1.cs"),
                harness.GetFullPath("exc", "dir", "tracked", "2.txt"),
            };

            var untrackedFilePaths = new AbsolutePath[]
            {
                harness.GetFullPath("exc", "a.txt"),
                harness.GetFullPath("exc", "b.txt"),
                harness.GetFullPath("exc", "dir", "1.cs"),
                harness.GetFullPath("inc", "sub", "untracked", "2.txt"),
            };

            // Verify tracking of paths is routed to appropriate tracker
            VerifyPaths(harness, Contents, trackedFilePaths, expectedTracker: harness.Tracker, unexpectedTracker: harness.DisabledTracker);
            VerifyPaths(harness, Contents, untrackedFilePaths, expectedTracker: harness.DisabledTracker, unexpectedTracker: harness.Tracker);
        }

        private static void VerifyPaths(Harness harness, string Contents, AbsolutePath[] filePaths, FileChangeTrackingRecorder expectedTracker, FileChangeTrackingRecorder unexpectedTracker)
        {
            var absentPathExtension = PathAtom.Create(harness.Context.StringTable, "absent");
            var pathTable = harness.Context.PathTable;

            foreach (var filePath in filePaths)
            {
                // First try probing the path
                harness.WriteFileAndHashContents(filePath, Contents);
                var probeResult = harness.Store.TryProbeAndTrackPathForExistence(filePath);
                XAssert.IsTrue(probeResult.Succeeded);
                XAssert.AreEqual(PathExistence.ExistsAsFile, probeResult.Result);

                var expandedPath = filePath.ToString(pathTable);
                expectedTracker.AssertExistenceOfPathIsTracked(expandedPath);
                unexpectedTracker.AssertExistenceOfPathIsNotTracked(expandedPath);

                // Now try discovering content hash and assert path is tracked in expected tracker only
                var discoveryResult = harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(filePath)).GetAwaiter().GetResult();
                XAssert.IsTrue(discoveryResult.Succeeded);
                expectedTracker.AssertPathIsTracked(expandedPath);
                unexpectedTracker.AssertPathIsNotTracked(expandedPath);

                // Not make an absent path an try probing that path
                var absentPath = filePath.ChangeExtension(pathTable, absentPathExtension);
                probeResult = harness.Store.TryProbeAndTrackPathForExistence(absentPath);
                XAssert.IsTrue(probeResult.Succeeded);
                XAssert.AreEqual(PathExistence.Nonexistent, probeResult.Result);

                var expandedAbsentPath = absentPath.ToString(pathTable);
                expectedTracker.AssertExistenceOfPathIsTracked(expandedAbsentPath);
                unexpectedTracker.AssertExistenceOfPathIsNotTracked(expandedAbsentPath);

                // Not try directory operations against the parent path. The assumption is that the directory is at or under the inclusion/exclusion root as well
                var directoryPath = filePath.GetParent(pathTable);
                var enumerationResult = harness.Store.TryEnumerateDirectoryAndTrackMembership(directoryPath, (name, attr) => { });
                XAssert.IsTrue(enumerationResult.Succeeded);
                XAssert.AreEqual(PathExistence.ExistsAsDirectory, enumerationResult.Result);

                var expandedDirectoryPath = directoryPath.ToString(pathTable);
                expectedTracker.AssertExistenceOfPathIsTracked(expandedDirectoryPath);
                expectedTracker.AssertMembershipOfPathIsTracked(expandedDirectoryPath);

                unexpectedTracker.AssertExistenceOfPathIsNotTracked(expandedDirectoryPath);
                unexpectedTracker.AssertMembershipOfPathIsNotTracked(expandedDirectoryPath);
            }
        }

        /// <summary>
        /// Sometimes, file version info is not available, in which case we shouldn't be able to track changes.
        /// </summary>
        [FactIfSupported(requiresSymlinkPermission: true)]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task DiscoverSymlink()
        {
            var harness = CreateHarness(useDummyFileContentTable: true);

            AbsolutePath targetPath = harness.GetFullPath("SomeFile");
            AbsolutePath symlink = harness.GetFullPath("symlink");

            string symlinkPath = symlink.ToString(harness.Context.PathTable);

            // First create a symlink pointing to a nonexisting target & validate that we correctly see it as a reparse point
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlink.ToString(harness.Context.PathTable), targetPath.ToString(harness.Context.PathTable), isTargetFile: true));
            Possible<ReparsePointType> reparsePointType = FileUtilities.TryGetReparsePointType(symlinkPath);
            XAssert.IsTrue(reparsePointType.Succeeded);
            bool actReparsePoint = FileUtilities.IsReparsePointActionable(reparsePointType.Result);
            XAssert.IsTrue(actReparsePoint);
            XAssert.IsTrue(reparsePointType.Result == ReparsePointType.SymLink);

            // Next make sure we correctly get the target path even when it doesn't exist
            Microsoft.Win32.SafeHandles.SafeFileHandle symlinkHandle;
            var openResult = FileUtilities.TryCreateOrOpenFile(
               symlinkPath,
               FileDesiredAccess.GenericRead,
               FileShare.Read | FileShare.Delete,
               FileMode.Open,
               FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
               out symlinkHandle);
            XAssert.IsTrue(openResult.Succeeded);

            using (symlinkHandle)
            {
                var possibleSymlinkTarget = FileUtilities.TryGetReparsePointTarget(symlinkHandle, symlinkPath);
                XAssert.IsTrue(possibleSymlinkTarget.Succeeded);
                XAssert.AreEqual(targetPath.ToString(harness.Context.PathTable), possibleSymlinkTarget.Result);
            }

            // Now retrieve the hash of the symlink when the target doesn't exist
            Possible<ContentDiscoveryResult> symlinkAsSourceTargetDoesNotExist = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(symlink));
            XAssert.IsTrue(symlinkAsSourceTargetDoesNotExist.Succeeded);

            // Create a file at the target for use in later test scenarios
            File.WriteAllText(targetPath.ToString(harness.Context.PathTable), "Test file content");

            Possible<ContentDiscoveryResult> targetResult = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(targetPath));
            XAssert.IsTrue(targetResult.Succeeded);
            Possible<ContentDiscoveryResult> symlinkAsSourceTargetDoesExist = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(symlink));
            XAssert.IsTrue(symlinkAsSourceTargetDoesExist.Succeeded);

            // The symlink that is a source file should have the same hash whether the target file exists or not
            XAssert.AreEqual(symlinkAsSourceTargetDoesExist.Result.TrackedFileContentInfo.Hash, symlinkAsSourceTargetDoesNotExist.Result.TrackedFileContentInfo.Hash);

            // The symlink that is a source file hashing its target path should not have the same hash as the target
            XAssert.AreNotEqual(targetResult.Result.TrackedFileContentInfo.Hash, symlinkAsSourceTargetDoesExist.Result.TrackedFileContentInfo.Hash);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task DiscoverSymlinkWithSubstTranslator()
        {
            var directoryTranslator = new DirectoryTranslator();
            directoryTranslator.AddTranslation(TemporaryDirectory, X("/Z"));
            directoryTranslator.Seal();

            var harness = CreateHarness(useDummyFileContentTable: true, directoryTranslator: directoryTranslator);

            AbsolutePath targetPath = harness.GetFullPath("SomeFile");
            AbsolutePath symlink = harness.GetFullPath("symlink");

            string symlinkPath = symlink.ToString(harness.Context.PathTable);

            // First create a symlink pointing to a nonexisting target & validate that we correctly see it as a reparse point
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlink.ToString(harness.Context.PathTable), targetPath.ToString(harness.Context.PathTable), isTargetFile: true));
            Possible<ReparsePointType> reparsePointType = FileUtilities.TryGetReparsePointType(symlinkPath);
            XAssert.IsTrue(reparsePointType.Succeeded);
            bool actReparsePoint = FileUtilities.IsReparsePointActionable(reparsePointType.Result);
            XAssert.IsTrue(actReparsePoint);
            XAssert.IsTrue(reparsePointType.Result == ReparsePointType.SymLink);

            // Next make sure we correctly get the target path even when it doesn't exist
            Microsoft.Win32.SafeHandles.SafeFileHandle symlinkHandle;
            var openResult = FileUtilities.TryCreateOrOpenFile(
               symlinkPath,
               FileDesiredAccess.GenericRead,
               FileShare.Read | FileShare.Delete,
               FileMode.Open,
               FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
               out symlinkHandle);
            XAssert.IsTrue(openResult.Succeeded);

            using (symlinkHandle)
            {
                var possibleSymlinkTarget = FileUtilities.TryGetReparsePointTarget(symlinkHandle, symlinkPath);
                XAssert.IsTrue(possibleSymlinkTarget.Succeeded);
                XAssert.AreEqual(targetPath.ToString(harness.Context.PathTable), possibleSymlinkTarget.Result);
            }

            Possible<ContentDiscoveryResult> contentDiscoveryResult = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(symlink));
            XAssert.IsTrue(contentDiscoveryResult.Succeeded);

            var substTargetPath = directoryTranslator.Translate(targetPath, harness.Context.PathTable);
            var contentHash = ContentHashingUtilities.HashString(substTargetPath.ToString(harness.Context.PathTable).ToUpperInvariant());

            XAssert.AreEqual(contentHash, contentDiscoveryResult.Result.TrackedFileContentInfo.Hash);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task RelativeSymlink()
        {
            var harness = CreateHarness(useDummyFileContentTable: true);

            AbsolutePath targetPath = harness.GetFullPath("NonExistent", "SomeFile");
            AbsolutePath symlink = harness.GetFullPath("Existent", "Nested", "Symlink.link");
            AbsolutePath symlinkDir = symlink.GetParent(harness.Context.PathTable);
            string expandedSymlinkDir = symlinkDir.ToString(harness.Context.PathTable);
            if (Directory.Exists(expandedSymlinkDir))
            {
                Directory.Delete(expandedSymlinkDir, true);
            }

            // Force creation of symlink with relative path.
            Directory.CreateDirectory(expandedSymlinkDir);
            string currentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(expandedSymlinkDir);

            string symLinkName = "Symlink.link";
            string symLinkTarget = R("..", "..", "NonExistent", "SomeFile");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symLinkName, symLinkTarget, isTargetFile: true));

            Possible<ReparsePointType> reparsePointType = FileUtilities.TryGetReparsePointType(symLinkName);
            XAssert.IsTrue(reparsePointType.Succeeded);
            bool actReparsePoint = FileUtilities.IsReparsePointActionable(reparsePointType.Result);
            XAssert.IsTrue(actReparsePoint);
            XAssert.IsTrue(reparsePointType.Result == ReparsePointType.SymLink);

            // Next make sure we correctly get the target path even when it doesn't exist
            Microsoft.Win32.SafeHandles.SafeFileHandle symlinkHandle;
            var openResult = FileUtilities.TryCreateOrOpenFile(
               symLinkName,
               FileDesiredAccess.GenericRead,
               FileShare.Read | FileShare.Delete,
               FileMode.Open,
               FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
               out symlinkHandle);
            XAssert.IsTrue(openResult.Succeeded);

            using (symlinkHandle)
            {
                var possibleRecoveredSymlinkTarget = FileUtilities.TryGetReparsePointTarget(symlinkHandle, symlink.ToString(harness.Context.PathTable));
                XAssert.IsTrue(possibleRecoveredSymlinkTarget.Succeeded);
                XAssert.AreEqual(symLinkTarget, possibleRecoveredSymlinkTarget.Result);
            }

            Possible<ContentDiscoveryResult> contentDiscoveryResult = await harness.Store.TryDiscoverAsync(FileArtifact.CreateSourceFile(symlink));
            XAssert.IsTrue(contentDiscoveryResult.Succeeded);

            var contentHash = ContentHashingUtilities.HashString(symLinkTarget.ToUpperInvariant());
            XAssert.AreEqual(contentHash, contentDiscoveryResult.Result.TrackedFileContentInfo.Hash);

            Directory.SetCurrentDirectory(currentDirectory);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task TrackFileWithoutIgnoringKnownContentHashTest()
        {
            var harness = CreateHarness(verifyKnownIdentityOnTrackingFile: false);
            AbsolutePath tempPath = harness.GetFullPath("SomeFile");
            FileContentInfo info = harness.WriteFileAndHashContents(tempPath, "Fancy contents");
            FileArtifact tempFile = FileArtifact.CreateSourceFile(tempPath);

            var maybeTrackedFileContentInfo = await harness.Store.TryTrackAsync(tempFile, true, ignoreKnownContentHashOnDiscoveringContent: false);
            XAssert.IsTrue(maybeTrackedFileContentInfo.Succeeded);
            XAssert.AreEqual(info.Hash, maybeTrackedFileContentInfo.Result.Hash);

            FileContentInfo newInfo = harness.WriteFileAndHashContents(tempPath, "New fancy contents");
            maybeTrackedFileContentInfo = await harness.Store.TryTrackAsync(tempFile, true, ignoreKnownContentHashOnDiscoveringContent: false);

            XAssert.IsTrue(maybeTrackedFileContentInfo.Succeeded);
            XAssert.AreEqual(newInfo.Hash, maybeTrackedFileContentInfo.Result.Hash);

            maybeTrackedFileContentInfo = await harness.Store.TryTrackAsync(tempFile, true, ignoreKnownContentHashOnDiscoveringContent: true);
            XAssert.IsTrue(maybeTrackedFileContentInfo.Succeeded);
            XAssert.AreEqual(newInfo.Hash, maybeTrackedFileContentInfo.Result.Hash);
        }

        private Harness CreateHarness(
            bool useDummyFileContentTable = false, 
            DirectoryTranslator directoryTranslator = null,
            bool verifyKnownIdentityOnTrackingFile = true,
            IArtifactContentCacheForTest contentCacheForTest = null)
        {
            return new Harness(
                TemporaryDirectory, 
                useDummyFileContentTable: useDummyFileContentTable, 
                directoryTranslator: directoryTranslator,
                verifyKnownIdentityOnTrackingFile: verifyKnownIdentityOnTrackingFile,
                contentCacheForTest: contentCacheForTest);
        }

        private class Harness : IArtifactContentCache
        {
            private readonly AbsolutePath m_outputRoot;
            private readonly DirectoryTranslator m_directoryTranslator;
            private LocalDiskContentStore m_store;
            public readonly BuildXLContext Context = BuildXLContext.CreateInstanceForTesting();
            public readonly List<AbsolutePath> FileChangeTrackingExclusionRoots = new List<AbsolutePath>();
            public readonly List<AbsolutePath> FileChangeTrackingInclusionRoots = new List<AbsolutePath>();
            public LocalDiskContentStore Store
            {
                get
                {
                    m_store = m_store ?? new LocalDiskContentStore(
                        LoggingContext,
                        Context.PathTable,
                        FileContentTable, 
                        Tracker, 
                        m_directoryTranslator,
                        new TestFileChangeTrackingSelector(this));
                    return m_store;
                }
            }
            public readonly IArtifactContentCacheForTest ContentCache;
            public readonly FileChangeTrackingRecorder Tracker;
            public readonly FileChangeTrackingRecorder DisabledTracker;
            public readonly FileContentTable FileContentTable;
            public readonly bool FilesShouldBeTracked;
            public readonly LoggingContext LoggingContext;

            public readonly ConcurrentDictionary<ContentHash, ExpandedAbsolutePath> ContentPaths = new ConcurrentDictionary<ContentHash, ExpandedAbsolutePath>();

            public Harness(
                string outputRoot, 
                bool useDummyFileContentTable, 
                DirectoryTranslator directoryTranslator,
                bool verifyKnownIdentityOnTrackingFile, 
                IArtifactContentCacheForTest contentCacheForTest)
            {
                m_directoryTranslator = directoryTranslator;

                // Dummy FCT should always prevent tracking from succeeding.
                FilesShouldBeTracked = !useDummyFileContentTable;

                FileContentTable = useDummyFileContentTable ? FileContentTable.CreateStub() : FileContentTable.CreateNew();
                Tracker = new FileChangeTrackingRecorder(verifyKnownIdentityOnTrackingFile);
                DisabledTracker = new FileChangeTrackingRecorder(verifyKnownIdentityOnTrackingFile);
                ContentCache = contentCacheForTest ?? new InMemoryArtifactContentCache();
                m_outputRoot = AbsolutePath.Create(Context.PathTable, outputRoot);
                LoggingContext = new LoggingContext(nameof(Harness));
            }

            public AbsolutePath GetFullPath(params string[] relativePathSegments)
            {
                string relativePath = R(relativePathSegments);
                return m_outputRoot.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, relativePath));
            }

            public FileContentInfo WriteFileAndHashContents(AbsolutePath path, string contents)
            {
                Directory.CreateDirectory(path.GetParent(Context.PathTable).ToString(Context.PathTable));

                byte[] bytes = Encoding.UTF8.GetBytes(contents);
                ContentHash hash = ContentHashingUtilities.HashBytes(bytes);
                string expandedPath = path.ToString(Context.PathTable);

                Analysis.IgnoreResult(FileUtilities.WriteAllBytesAsync(expandedPath, bytes).GetAwaiter().GetResult());

                return new FileContentInfo(hash, bytes.LongLength);
            }

            public VersionedFileIdentityAndContentInfo AddPathToFileContentTable(AbsolutePath path, ContentHash hash)
            {
                using (var tempFileStream = File.OpenRead(path.ToString(Context.PathTable)))
                {
                    return FileContentTable.RecordContentHash(tempFileStream, hash);
                }
            }

            public void AssertPathIsTracked(AbsolutePath path)
            {
                string expandedPath = path.ToString(Context.PathTable);
                Tracker.AssertPathIsTracked(expandedPath);
                XAssert.IsTrue(FilesShouldBeTracked, "Unexpectedly succeeded at tracking a path (FCT is a stub)");
            }

            public void AssertPathIsNotTracked(AbsolutePath path)
            {
                string expandedPath = path.ToString(Context.PathTable);
                Tracker.AssertPathIsNotTracked(expandedPath);
            }

            public void AssertContentIsInLocalCache(ContentHash hash)
            {
                CacheSites sites = ContentCache.FindContainingSites(hash);
                if ((sites & CacheSites.Local) == 0)
                {
                    XAssert.Fail("Expected hash {0} in local cache site (actual sites: {1})", hash, sites);
                }
            }

            public void AssertContentIsAbsentInLocalCache(ContentHash hash)
            {
                CacheSites sites = ContentCache.FindContainingSites(hash);
                if ((sites & CacheSites.Local) != 0)
                {
                    XAssert.Fail("Expected hash {0} to be absent from local cache site (actual sites: {1})", hash, sites);
                }
            }

            public void AssertExistenceOfPathIsTracked(AbsolutePath path)
            {
                string expandedPath = path.ToString(Context.PathTable);
                Tracker.AssertExistenceOfPathIsTracked(expandedPath);
            }

            public void AssertExistenceOfPathIsNotTracked(AbsolutePath path)
            {
                string expandedPath = path.ToString(Context.PathTable);
                Tracker.AssertExistenceOfPathIsNotTracked(expandedPath);
            }

            public async Task VerifyMaterialization(AbsolutePath path, ContentHash hash, ContentMaterializationOrigin expectedOrigin)
            {
                Possible<ContentMaterializationResult> possiblyMaterialized =
                    await Store.TryMaterializeAsync(
                        ContentCache,
                        FileRealizationMode.Copy,
                        path,
                        hash);
                if (!possiblyMaterialized.Succeeded)
                {
                    XAssert.Fail("Failed to materialize content: {0}", possiblyMaterialized.Failure.DescribeIncludingInnerFailures());
                }

                ContentMaterializationResult materializationResult = possiblyMaterialized.Result;

                XAssert.AreEqual(expectedOrigin, materializationResult.Origin);

                if (FilesShouldBeTracked)
                {
                    AssertPathIsTracked(path);
                    XAssert.IsTrue(
                        materializationResult.TrackedFileContentInfo.IsTracked,
                        "Materialized file should have a valid tracking subscription");
                }
                else
                {
                    AssertPathIsNotTracked(path);
                    XAssert.IsFalse(
                        materializationResult.TrackedFileContentInfo.IsTracked,
                        "Materialized file should NOT have a valid tracking subscription");
                }

                XAssert.AreEqual(hash, materializationResult.TrackedFileContentInfo.Hash, "Wrong hash reported by TryMaterializeAsync");
            }

            public async Task VerifyStore(AbsolutePath path, ContentHash expectedHash)
            {
                Possible<TrackedFileContentInfo> possiblyStored =
                    await Store.TryStoreAsync(ContentCache, FileRealizationMode.Copy, path, tryFlushPageCacheToFileSystem: true);
                if (!possiblyStored.Succeeded)
                {
                    XAssert.Fail("Failed to store content after initial write: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
                }

                XAssert.AreEqual(expectedHash, possiblyStored.Result.FileContentInfo.Hash, "Wrong hash reported by TryStoreAsync");
                AssertContentIsInLocalCache(expectedHash);

                if (FilesShouldBeTracked)
                {
                    AssertPathIsTracked(path);
                    XAssert.IsTrue(
                        possiblyStored.Result.IsTracked,
                        "Stored file should have a valid tracking subscription");
                }
                else
                {
                    AssertPathIsNotTracked(path);
                    XAssert.IsFalse(
                        possiblyStored.Result.IsTracked,
                        "Stored file should NOT have a valid tracking subscription");
                }
            }

            public Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes)
            {
                return ContentCache.TryLoadAvailableContentAsync(hashes);
            }

            public Task<Possible<Stream, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
            {
                return ContentCache.TryOpenContentStreamAsync(contentHash);
            }

            public Task<Possible<Unit, Failure>> TryMaterializeAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash)
            {
                return ContentCache.TryMaterializeAsync(fileRealizationModes, path, contentHash);
            }

            public async Task<Possible<Unit, Failure>> TryStoreAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash)
            {
                var result = await ContentCache.TryStoreAsync(fileRealizationModes, path, contentHash);

                ExpandedAbsolutePath originalContentPath;
                if (fileRealizationModes == FileRealizationMode.HardLink && ContentPaths.TryGetValue(contentHash, out originalContentPath))
                {
                    FileUtilities.DeleteFile(path.ExpandedPath);
                    var status = FileUtilities.TryCreateHardLink(path.ExpandedPath, originalContentPath.ExpandedPath);
                    Assert.Equal(CreateHardLinkStatus.Success, status);
                }

                return result;
            }

            public Task<Possible<ContentHash, Failure>> TryStoreAsync(FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path)
            {
                return ContentCache.TryStoreAsync(fileRealizationModes, path);
            }

            public Task<Possible<Unit, Failure>> TryStoreAsync(Stream content, ContentHash contentHash)
            {
                return ContentCache.TryStoreAsync(content, contentHash);
            }

            private class TestFileChangeTrackingSelector : FileChangeTrackingSelector
            {
                private Harness m_harness;

                public TestFileChangeTrackingSelector(Harness harness) 
                    : base(harness.Context.PathTable, harness.LoggingContext, harness.Tracker, harness.FileChangeTrackingInclusionRoots, harness.FileChangeTrackingExclusionRoots)
                {
                    m_harness = harness;
                    SetDisabledTrackerTestOnly(harness.DisabledTracker);
                }
            }
        }
    }
}
