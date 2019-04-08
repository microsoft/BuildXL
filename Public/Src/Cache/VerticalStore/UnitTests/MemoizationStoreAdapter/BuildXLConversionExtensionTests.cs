// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.Interfaces.Test;
using Xunit;
using BuildXLCacheDeterminism = BuildXL.Cache.Interfaces.CacheDeterminism;
using BuildXLStrongFingerprint = BuildXL.Cache.Interfaces.StrongFingerprint;
using MemoizationStrongFingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint;
using MemoizationCacheDeterminism = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.CacheDeterminism;

namespace Test.Cache.MemoizationStoreAdapter
{
    public class BuildXLConversionExtensionTests
    {
        private const string CacheId = "TestCacheId";
        private const string ErrorMessage = "Test error message";

        private const int Determinism_None = 0;
        private const int Determinism_Cache_Expired = 1;
        private const int Determinism_Cache_Expiring = 2;
        private const int Determinism_Cache_NeverExpires = 3;
        private const int Determinism_Tool = 4;

        private HashType HashingType => ContentHashingUtilities.HashInfo.HashType;

        private static BuildXLCacheDeterminism[] m_buildXLDeterminism = new[]
        {
            BuildXLCacheDeterminism.None,
            BuildXLCacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow - TimeSpan.FromDays(7)),
            BuildXLCacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow + TimeSpan.FromDays(7)),
            BuildXLCacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), BuildXLCacheDeterminism.NeverExpires),
            BuildXLCacheDeterminism.Tool
        };

        private static MemoizationCacheDeterminism[] m_memoizationDeterminism = new[]
        {
            MemoizationCacheDeterminism.None,
            MemoizationCacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow - TimeSpan.FromDays(7)),
            MemoizationCacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow + TimeSpan.FromDays(7)),
            MemoizationCacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), MemoizationCacheDeterminism.NeverExpires),
            MemoizationCacheDeterminism.Tool
        };

        [Fact]
        public void CasHashToMemoization()
        {
            CasHash hash = RandomHelpers.CreateRandomCasHash();
            ContentHash contentHash = hash.ToMemoization();
            Assert.Equal(hash.BaseHash.RawHash.ToByteArray(), contentHash.ToHashByteArray());
        }

        [Fact]
        public void ContentHashFromMemoization()
        {
            ContentHash contentHash = ContentHashingUtilities.CreateRandom();
            CasHash hash = contentHash.FromMemoization();
            Assert.Equal(contentHash.ToHashByteArray(), hash.BaseHash.RawHash.ToByteArray());
        }

        [Fact]
        public void CasHashRoundTrip()
        {
            CasHash hash = RandomHelpers.CreateRandomCasHash();
            CasHash roundTrip = hash.ToMemoization().FromMemoization();
            Assert.Equal(hash, roundTrip);
        }

        [Fact]
        public void ContentHashRoundTrip()
        {
            ContentHash contentHash = ContentHashingUtilities.CreateRandom();
            ContentHash roundTrip = contentHash.FromMemoization().ToMemoization();
            Assert.Equal(contentHash, roundTrip);
        }

        [Fact]
        public void WeakFingerprintHashToMemoization()
        {
            WeakFingerprintHash weak = RandomHelpers.CreateRandomWeakFingerprintHash();
            Fingerprint fingerprint = weak.ToMemoization();
            Assert.Equal(weak.ToArray(), fingerprint.ToByteArray());
        }

        [Theory]
        [InlineData(Determinism_None)]
        [InlineData(Determinism_Cache_Expired)]
        [InlineData(Determinism_Cache_Expiring)]
        [InlineData(Determinism_Cache_NeverExpires)]
        [InlineData(Determinism_Tool)]
        public void BuildXLCacheDeterminismToMemoization(int determinism)
        {
            BuildXLCacheDeterminism buildXLCacheDeterminism = m_buildXLDeterminism[determinism];
            MemoizationCacheDeterminism memoizationCacheDeterminism = buildXLCacheDeterminism.ToMemoization();
            AssertDeterminismEqualEnough(buildXLCacheDeterminism, memoizationCacheDeterminism);
        }

        [Theory]
        [InlineData(Determinism_None)]
        [InlineData(Determinism_Cache_Expired)]
        [InlineData(Determinism_Cache_Expiring)]
        [InlineData(Determinism_Cache_NeverExpires)]
        [InlineData(Determinism_Tool)]
        public void MemoizationCacheDeterminismFromMemoization(int determinism)
        {
            MemoizationCacheDeterminism memoizationCacheDeterminism = m_memoizationDeterminism[determinism];
            BuildXLCacheDeterminism buildXLCacheDeterminism = memoizationCacheDeterminism.FromMemoization();
            AssertDeterminismEqualEnough(buildXLCacheDeterminism, memoizationCacheDeterminism);
        }

        [Theory]
        [InlineData(Determinism_None)]
        [InlineData(Determinism_Cache_Expired)]
        [InlineData(Determinism_Cache_Expiring)]
        [InlineData(Determinism_Cache_NeverExpires)]
        [InlineData(Determinism_Tool)]
        public void BuildXLCacheDeterminismRoundTrip(int determinism)
        {
            BuildXLCacheDeterminism buildXLCacheDeterminism = m_buildXLDeterminism[determinism];
            BuildXLCacheDeterminism roundTrip = buildXLCacheDeterminism.ToMemoization().FromMemoization();
            AssertDeterminismEqualEnough(buildXLCacheDeterminism, roundTrip);
        }

        [Theory]
        [InlineData(Determinism_None)]
        [InlineData(Determinism_Cache_Expired)]
        [InlineData(Determinism_Cache_Expiring)]
        [InlineData(Determinism_Cache_NeverExpires)]
        [InlineData(Determinism_Tool)]
        public void MemoizationCacheDeterminismRoundTrip(int determinism)
        {
            MemoizationCacheDeterminism memoizationCacheDeterminism = m_memoizationDeterminism[determinism];
            MemoizationCacheDeterminism roundTrip = memoizationCacheDeterminism.FromMemoization().ToMemoization();
            AssertDeterminismEqualEnough(memoizationCacheDeterminism, roundTrip);
        }

        [Theory]
        [InlineData(0, Determinism_None)]
        [InlineData(0, Determinism_Cache_Expired)]
        [InlineData(0, Determinism_Cache_Expiring)]
        [InlineData(0, Determinism_Cache_NeverExpires)]
        [InlineData(0, Determinism_Tool)]
        [InlineData(2, Determinism_None)]
        [InlineData(2, Determinism_Cache_Expired)]
        [InlineData(2, Determinism_Cache_Expiring)]
        [InlineData(2, Determinism_Cache_NeverExpires)]
        [InlineData(2, Determinism_Tool)]
        public void CasEntriesToMemoization(int casEntryCount, int determinism)
        {
            CasEntries casEntries = RandomHelpers.CreateRandomCasEntries(casEntryCount, m_buildXLDeterminism[determinism]);
            ContentHashListWithDeterminism contentHashListWithDeterminism = casEntries.ToMemoization();

            Assert.Equal(casEntries.Count, contentHashListWithDeterminism.ContentHashList.Hashes.Count);
            for (int i = 0; i < casEntries.Count; i++)
            {
                Assert.Equal(casEntries[i].ToMemoization(), contentHashListWithDeterminism.ContentHashList.Hashes[i]);
            }

            AssertDeterminismEqualEnough(casEntries.Determinism, contentHashListWithDeterminism.Determinism);
        }

        [Theory]
        [InlineData(0, Determinism_None)]
        [InlineData(0, Determinism_Cache_Expired)]
        [InlineData(0, Determinism_Cache_Expiring)]
        [InlineData(0, Determinism_Cache_NeverExpires)]
        [InlineData(0, Determinism_Tool)]
        [InlineData(2, Determinism_None)]
        [InlineData(2, Determinism_Cache_Expired)]
        [InlineData(2, Determinism_Cache_Expiring)]
        [InlineData(2, Determinism_Cache_NeverExpires)]
        [InlineData(2, Determinism_Tool)]
        public void ContentHashListWithDeterminismFromMemoization(int contentHashCount, int determinism)
        {
            ContentHashListWithDeterminism contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(HashingType, contentHashCount),
                m_memoizationDeterminism[determinism]);
            CasEntries casEntries = contentHashListWithDeterminism.FromMemoization();

            Assert.Equal(contentHashListWithDeterminism.ContentHashList.Hashes.Count, casEntries.Count);
            for (int i = 0; i < casEntries.Count; i++)
            {
                Assert.Equal(contentHashListWithDeterminism.ContentHashList.Hashes[i], casEntries[i].ToMemoization());
            }

            AssertDeterminismEqualEnough(casEntries.Determinism, contentHashListWithDeterminism.Determinism);
        }

        [Theory]
        [InlineData(0, Determinism_None)]
        [InlineData(0, Determinism_Cache_Expired)]
        [InlineData(0, Determinism_Cache_Expiring)]
        [InlineData(0, Determinism_Cache_NeverExpires)]
        [InlineData(0, Determinism_Tool)]
        [InlineData(2, Determinism_None)]
        [InlineData(2, Determinism_Cache_Expired)]
        [InlineData(2, Determinism_Cache_Expiring)]
        [InlineData(2, Determinism_Cache_NeverExpires)]
        [InlineData(2, Determinism_Tool)]
        public void CasEntriesRoundTrip(int casEntryCount, int determinism)
        {
            CasEntries casEntries = RandomHelpers.CreateRandomCasEntries(casEntryCount, m_buildXLDeterminism[determinism]);
            CasEntries roundTrip = casEntries.ToMemoization().FromMemoization();
            Assert.Equal(casEntries, roundTrip);
        }

        [Theory]
        [InlineData(0, Determinism_None)]
        [InlineData(0, Determinism_Cache_Expired)]
        [InlineData(0, Determinism_Cache_Expiring)]
        [InlineData(0, Determinism_Cache_NeverExpires)]
        [InlineData(0, Determinism_Tool)]
        [InlineData(2, Determinism_None)]
        [InlineData(2, Determinism_Cache_Expired)]
        [InlineData(2, Determinism_Cache_Expiring)]
        [InlineData(2, Determinism_Cache_NeverExpires)]
        [InlineData(2, Determinism_Tool)]
        public void ContentHashListWithDeterminismRoundTrip(int contentHashCount, int determinism)
        {
            ContentHashListWithDeterminism contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(HashingType, contentHashCount),
                m_memoizationDeterminism[determinism]);
            ContentHashListWithDeterminism roundTrip = contentHashListWithDeterminism.FromMemoization().ToMemoization();
        }

        [Fact]
        public void PinResultSuccessFromMemoization()
        {
            PinResult pinResult = PinResult.Success;
            Possible<string, Failure> maybe = pinResult.FromMemoization(RandomHelpers.CreateRandomCasHash(), CacheId);

            Assert.True(maybe.Succeeded);
            Assert.Equal(CacheId, maybe.Result);
        }

        [Fact]
        public void PinResultMissFromMemoization()
        {
            PinResult pinResult = PinResult.ContentNotFound;
            CasHash hash = RandomHelpers.CreateRandomCasHash();
            Possible<string, Failure> maybe = pinResult.FromMemoization(hash, CacheId);

            Assert.False(maybe.Succeeded);
            NoCasEntryFailure failure = maybe.Failure as NoCasEntryFailure;
            Assert.False(failure == null);

            string failureMessage = failure.Describe();
            Assert.Contains(hash.ToString(), failureMessage);
            Assert.Contains(CacheId, failureMessage);
        }

        [Fact]
        public void PinResultErrorFromMemoization()
        {
            PinResult pinResult = new PinResult(ErrorMessage);
            Possible<string, Failure> maybe = pinResult.FromMemoization(RandomHelpers.CreateRandomCasHash(), CacheId);

            Assert.False(maybe.Succeeded);
            Assert.Contains(ErrorMessage, maybe.Failure.Describe());
        }

        [Fact]
        public void FileStateToMemoization()
        {
            foreach (FileState fileState in Enum.GetValues(typeof(FileState)))
            {
                FileRealizationMode fileRealizationMode = fileState.ToMemoization();
                switch (fileState)
                {
                    case FileState.Writeable:
                        Assert.Equal(FileRealizationMode.CopyNoVerify, fileRealizationMode);
                        break;
                    case FileState.ReadOnly:
                        Assert.Equal(FileRealizationMode.Any, fileRealizationMode);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        [Fact]
        public void SelectorResultSuccessFromMemoization()
        {
            WeakFingerprintHash weak = RandomHelpers.CreateRandomWeakFingerprintHash();
            GetSelectorResult selectorResult = new GetSelectorResult(Selector.Random(HashType.Vso0, FingerprintUtilities.FingerprintLength));
            Possible<BuildXLStrongFingerprint, Failure> maybeStrongFingerprint = selectorResult.FromMemoization(weak, CacheId);

            Assert.True(maybeStrongFingerprint.Succeeded);
            Assert.Equal(weak, maybeStrongFingerprint.Result.WeakFingerprint);
            Assert.Equal(selectorResult.Selector.ContentHash.FromMemoization(), maybeStrongFingerprint.Result.CasElement);
            Assert.Equal(selectorResult.Selector.Output, maybeStrongFingerprint.Result.HashElement.ToArray());
            Assert.Equal(CacheId, maybeStrongFingerprint.Result.CacheId);
        }

        [Fact]
        public void SelectorResultErrorFromMemoization()
        {
            GetSelectorResult selectorResult = new GetSelectorResult(ErrorMessage);
            Possible<BuildXLStrongFingerprint, Failure> maybeStrongFingerprint = selectorResult.FromMemoization(RandomHelpers.CreateRandomWeakFingerprintHash(), CacheId);

            Assert.False(maybeStrongFingerprint.Succeeded);
            Assert.Contains(ErrorMessage, maybeStrongFingerprint.Failure.Describe());
        }

        [Fact]
        public void StrongFingerprintFromMemoization()
        {
            MemoizationStrongFingerprint memoizationStrongFingerprint = new MemoizationStrongFingerprint(
                Fingerprint.Random(WeakFingerprintHash.Length),
                Selector.Random(HashingType, WeakFingerprintHash.Length));
            BuildXLStrongFingerprint buildXLStrongFingerprint = memoizationStrongFingerprint.FromMemoization(CacheId);

            Assert.Equal(memoizationStrongFingerprint.WeakFingerprint.ToByteArray(), buildXLStrongFingerprint.WeakFingerprint.ToArray());
            Assert.Equal(memoizationStrongFingerprint.Selector.ContentHash.FromMemoization(), buildXLStrongFingerprint.CasElement);
            Assert.Equal(memoizationStrongFingerprint.Selector.Output, buildXLStrongFingerprint.HashElement.ToArray());
            Assert.Equal(CacheId, buildXLStrongFingerprint.CacheId);
        }

        private static void AssertDeterminismEqualEnough(BuildXLCacheDeterminism expected, BuildXLCacheDeterminism actual)
        {
            if (!expected.IsDeterministic)
            {
                Assert.Equal(expected.EffectiveGuid, actual.EffectiveGuid);
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }

        private static void AssertDeterminismEqualEnough(MemoizationCacheDeterminism expected, MemoizationCacheDeterminism actual)
        {
            if (!expected.IsDeterministic)
            {
                Assert.Equal(expected.EffectiveGuid, actual.EffectiveGuid);
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }

        private static void AssertDeterminismEqualEnough(BuildXLCacheDeterminism buildXL, MemoizationCacheDeterminism memoization)
        {
            if (!buildXL.IsDeterministic)
            {
                Assert.Equal(buildXL.EffectiveGuid, memoization.EffectiveGuid);
            }
            else
            {
                Assert.Equal(buildXL.Guid, memoization.Guid);
                Assert.Equal(buildXL.ExpirationUtc, memoization.ExpirationUtc);
            }
        }
    }
}
