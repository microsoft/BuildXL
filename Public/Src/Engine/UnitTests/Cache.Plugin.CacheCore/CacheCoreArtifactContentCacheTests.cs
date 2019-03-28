// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Plugin.CacheCore;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Test.BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Tests for the <see cref="CacheCoreArtifactContentCache"/> adapter.
    /// </summary>
    public sealed class CacheCoreArtifactContentCacheTests : MemCacheTest
    {
        public readonly PipExecutionContext Context;
        public readonly CacheCoreArtifactContentCache ContentCache;

        public CacheCoreArtifactContentCacheTests(ITestOutputHelper output)
            : base(output)
        {
            Context = BuildXLContext.CreateInstanceForTesting();
            ContentCache = new CacheCoreArtifactContentCache(Session, rootTranslator: null);
        }

        [Fact]
        public async Task TryLoadNonexistentContent()
        {
            ContentHash unrelatedHash = await AddContent("Unrelated data");
            ContentHash desiredHash = HashContent("Very useful data");
            XAssert.AreNotEqual(desiredHash, unrelatedHash);

            Possible<ContentAvailabilityBatchResult> possiblyLoaded =
                await ContentCache.TryLoadAvailableContentAsync(new List<ContentHash>() { desiredHash });
            XAssert.IsTrue(possiblyLoaded.Succeeded);
            ContentAvailabilityBatchResult result = possiblyLoaded.Result;
            XAssert.IsFalse(result.AllContentAvailable);
            XAssert.AreEqual(1, result.Results.Length);
            XAssert.IsFalse(result.Results[0].IsAvailable);
            XAssert.AreEqual(desiredHash, result.Results[0].Hash);
        }

        [Fact]
        public async Task TryLoadExistentContent()
        {
            ContentHash unrelatedHash = await AddContent("Unrelated data");
            ContentHash desiredHash = await AddContent("Very useful data");
            XAssert.AreNotEqual(desiredHash, unrelatedHash);

            Possible<ContentAvailabilityBatchResult> possiblyLoaded =
                await ContentCache.TryLoadAvailableContentAsync(new List<ContentHash>() { desiredHash });
            XAssert.IsTrue(possiblyLoaded.Succeeded);
            ContentAvailabilityBatchResult result = possiblyLoaded.Result;
            XAssert.IsTrue(result.AllContentAvailable);
            XAssert.AreEqual(1, result.Results.Length);
            XAssert.IsTrue(result.Results[0].IsAvailable);
            XAssert.AreEqual(desiredHash, result.Results[0].Hash);
        }

        [Fact]
        public async Task TryLoadContentWithSomeButNotAllAvailable()
        {
            ContentHash availableHash = await AddContent("Very useful data");
            ContentHash unavailableHash = HashContent("If only we had this");
            ContentHash alsoAvailableHash = await AddContent("Even more very useful data");

            XAssert.AreNotEqual(availableHash, unavailableHash);
            XAssert.AreNotEqual(availableHash, alsoAvailableHash);
            XAssert.AreNotEqual(alsoAvailableHash, unavailableHash);

            Possible<ContentAvailabilityBatchResult> possiblyLoaded =
                await ContentCache.TryLoadAvailableContentAsync(new List<ContentHash>()
                                                                {
                                                                    availableHash,
                                                                    unavailableHash,
                                                                    alsoAvailableHash
                                                                });
            XAssert.IsTrue(possiblyLoaded.Succeeded);
            ContentAvailabilityBatchResult result = possiblyLoaded.Result;
            XAssert.IsFalse(result.AllContentAvailable);
            XAssert.AreEqual(3, result.Results.Length);

            XAssert.IsTrue(result.Results[0].IsAvailable);
            XAssert.AreEqual(availableHash, result.Results[0].Hash);

            XAssert.IsFalse(result.Results[1].IsAvailable);
            XAssert.AreEqual(unavailableHash, result.Results[1].Hash);

            XAssert.IsTrue(result.Results[2].IsAvailable);
            XAssert.AreEqual(alsoAvailableHash, result.Results[2].Hash);
        }

        [Fact]
        public async Task OpenStreamToExistentContent()
        {
            ContentHash availableHash = await AddContent("Very useful data");
            await LoadContentAndExpectAvailable(ContentCache, availableHash);

            Possible<Stream> maybeStream = await ContentCache.TryOpenContentStreamAsync(availableHash);
            XAssert.IsTrue(maybeStream.Succeeded);

            using (Stream stream = maybeStream.Result)
            {
                XAssert.AreEqual(availableHash, await ContentHashingUtilities.HashContentStreamAsync(stream));
            }
        }

        [Fact]
        public async Task OpenStreamToNoneExistentContent()
        {
            ContentHash unavailableHash = HashContent("A stream made of wishes");

            Possible<Stream> maybeStream = await ContentCache.TryOpenContentStreamAsync(unavailableHash);
            XAssert.IsFalse(maybeStream.Succeeded);
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task MaterializeOverExistentFile()
        {
            const string TargetContent = "This should be the final content";

            string targetPath = GetFullPath("NiftyBits");
            WriteFile("NiftyBits", "Please replace this");

            ContentHash availableHash = await AddContent(TargetContent);
            await LoadContentAndExpectAvailable(ContentCache, availableHash);

            Possible<Unit> maybeMaterialized = await ContentCache.TryMaterializeAsync(
                FileRealizationMode.Copy,
                AbsolutePath.Create(Context.PathTable, targetPath).Expand(Context.PathTable),
                availableHash);
            XAssert.IsTrue(maybeMaterialized.Succeeded);

            XAssert.AreEqual(TargetContent, File.ReadAllText(targetPath));
        }

        [Fact]
        public async Task StoreExistentFile()
        {
            const string TargetContent = "This should be available after storing to the CAS";

            string targetPath = GetFullPath("NiftyBits");
            WriteFile("NiftyBits", TargetContent);

            ContentHash expectedHash = HashContent(TargetContent);

            Possible<ContentHash> maybeStored = await ContentCache.TryStoreAsync(
                FileRealizationMode.Copy,
                AbsolutePath.Create(Context.PathTable, targetPath).Expand(Context.PathTable));

            XAssert.IsTrue(maybeStored.Succeeded);
            XAssert.AreEqual(expectedHash, maybeStored.Result);
            await LoadContentAndExpectAvailable(ContentCache, expectedHash);
        }

        [Fact]
        public void RootTranslation()
        {
            var rootTranslator = new RootTranslator();
            var pathTable = Context.PathTable;

            // Source is shorter than target
            var shortCaseSourceTranslatedRootPath = GetFullPath(pathTable, "ShortSource").Expand(pathTable);
            var shortCaseTargetTranslatedRootPath = GetFullPath(pathTable, "Short___Target").Expand(pathTable);
            rootTranslator.AddTranslation(
                shortCaseSourceTranslatedRootPath.ExpandedPath, 
                shortCaseTargetTranslatedRootPath.ExpandedPath);

            // Source is longer than target
            var longCaseSourceTranslatedRootPath = GetFullPath(pathTable, "LongSourceSource").Expand(pathTable);
            var longCaseTargetTranslatedRootPath = GetFullPath(pathTable, "LongTarget").Expand(pathTable);
            rootTranslator.AddTranslation(
                longCaseSourceTranslatedRootPath.ExpandedPath,
                longCaseTargetTranslatedRootPath.ExpandedPath);

            rootTranslator.Seal();

            var cache = new CacheCoreArtifactContentCache(Session, rootTranslator);

            // SHORTER SOURCE, SAME ROOT: Path should NOT be translated
            VerifyExpandedPathForCacheEquals(cache, shortCaseSourceTranslatedRootPath, shortCaseSourceTranslatedRootPath);

            // LONGER SOURCE, SAME ROOT: Path SHOULD be translated
            VerifyExpandedPathForCacheEquals(cache, longCaseSourceTranslatedRootPath, longCaseTargetTranslatedRootPath);
        }

        // on unix systems all paths have the same root '/'
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RootTranslationDifferentRoots()
        {
            var rootTranslator = new RootTranslator();
            var pathTable = Context.PathTable;

            // Source is shorter than target but has different root
            var shortCaseChangeRootSourceTranslatedRootPath = ChangeRoot(GetFullPath(pathTable, "ShortChangeRootSrc"), pathTable, newRoot: 'A');
            var shortCaseChangeRootTargetTranslatedRootPath = ChangeRoot(GetFullPath(pathTable, "ShortChangeRootTarget"), pathTable, newRoot: 'B');
            rootTranslator.AddTranslation(
                shortCaseChangeRootSourceTranslatedRootPath.ExpandedPath,
                shortCaseChangeRootTargetTranslatedRootPath.ExpandedPath);

            rootTranslator.Seal();

            var cache = new CacheCoreArtifactContentCache(Session, rootTranslator);

            // SHORTER SOURCE, DIFFERENT ROOT: Path SHOULD be translated
            VerifyExpandedPathForCacheEquals(cache, shortCaseChangeRootSourceTranslatedRootPath, shortCaseChangeRootTargetTranslatedRootPath);
        }

        private void VerifyExpandedPathForCacheEquals(CacheCoreArtifactContentCache cache, 
            ExpandedAbsolutePath originalRoot, 
            ExpandedAbsolutePath expectedRoot)
        {
            var pathTable = Context.PathTable;
            string fileName = "bits.txt";
            var originalRootExpandedPath = AbsolutePath.Create(pathTable, R(originalRoot.ExpandedPath, fileName)).Expand(pathTable);
            var expectedRootExpandedPath = AbsolutePath.Create(pathTable, R(expectedRoot.ExpandedPath, fileName)).Expand(pathTable);

            Assert.Equal(
                expectedRootExpandedPath.ExpandedPath,
                cache.GetExpandedPathForCache(originalRootExpandedPath));
        }

        private ExpandedAbsolutePath ChangeRoot(AbsolutePath path, PathTable pathTable, char newRoot)
        {
            XAssert.IsFalse(OperatingSystemHelper.IsUnixOS, "Cannot change root on a unix system");
            return AbsolutePath.Create(pathTable, newRoot + path.ToString(pathTable).Substring(1)).Expand(pathTable);
        }

        private static async Task LoadContentAndExpectAvailable(CacheCoreArtifactContentCache cache, ContentHash hash)
        {
            Possible<ContentAvailabilityBatchResult> possiblyLoaded =
                await cache.TryLoadAvailableContentAsync(new List<ContentHash>() { hash });
            XAssert.IsTrue(possiblyLoaded.Succeeded);
            XAssert.IsTrue(possiblyLoaded.Result.AllContentAvailable);
        }
    }
}
