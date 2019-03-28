// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public class CacheDeterminismTests
    {
        private const int DeterminismNone = 0;
        private const int DeterminismCache1 = 1;
        private const int DeterminismCache1Expired = 2;
        private const int DeterminismCache2 = 3;
        private const int DeterminismCache2Expired = 4;
        private const int DeterminismTool = 5;
        private const int DeterminismSinglePhaseNon = 6;

        private const string NoneString = "00000000-0000-0000-0000-000000000000";
        private const string CacheString1 = "2ABD62E7-6117-4D8B-ABB2-D3346E5AF102";
        private const string CacheString2 = "78559E55-E0C3-4C77-A908-8AE9E6590764";
        private const string ToolString = "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF";
        private const string SinglePhaseNonDeterministicString = "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFE";

        private static readonly CacheDeterminism[] Determinism =
        {
            CacheDeterminism.None,
            CacheDeterminism.ViaCache(new Guid(CacheString1), DateTime.UtcNow + TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid(CacheString1), DateTime.UtcNow - TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid(CacheString2), DateTime.UtcNow + TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid(CacheString2), DateTime.UtcNow - TimeSpan.FromDays(7)),
            CacheDeterminism.Tool,
            CacheDeterminism.SinglePhaseNonDeterministic
        };

        [Fact]
        public void NewCacheGuidCreatesNonSpecialValue()
        {
            var guid = CacheDeterminism.NewCacheGuid();
            Assert.NotEqual(CacheDeterminism.None.EffectiveGuid, guid);
            Assert.NotEqual(CacheDeterminism.Tool.EffectiveGuid, guid);
            Assert.NotEqual(CacheDeterminism.SinglePhaseNonDeterministic.EffectiveGuid, guid);
        }

        [Fact]
        public void ViaCacheRoundtrip()
        {
            var guid = CacheDeterminism.NewCacheGuid();
            var determinism = CacheDeterminism.ViaCache(guid, DateTime.MaxValue);
            Assert.Equal(guid, determinism.EffectiveGuid);
        }

        [Theory]
        [InlineData(DeterminismNone, false, false, false, NoneString)]
        [InlineData(DeterminismCache1, false, true, false, CacheString1)]
        [InlineData(DeterminismCache1Expired, false, false, false, NoneString)]
        [InlineData(DeterminismTool, true, true, false, ToolString)]
        [InlineData(DeterminismSinglePhaseNon, false, false, true, SinglePhaseNonDeterministicString)]
        public void DeterminismProperties(
            int determinismIndex, bool isDeterministicTool, bool isDeterministic, bool isSinglePhaseNonDeterministic, string effectiveGuid)
        {
            var determinism = Determinism[determinismIndex];
            Assert.Equal(isDeterministicTool, determinism.IsDeterministicTool);
            Assert.Equal(isDeterministic, determinism.IsDeterministic);
            Assert.Equal(isSinglePhaseNonDeterministic, determinism.IsSinglePhaseNonDeterministic);
            Assert.Equal(new Guid(effectiveGuid), determinism.EffectiveGuid);
        }

        [Theory]
        [InlineData(DeterminismNone, DeterminismTool, true)] // Tool overwrites None
        [InlineData(DeterminismNone, DeterminismCache1, true)] // ViaCache overwrites None
        [InlineData(DeterminismCache1, DeterminismCache2, true)] // ViaCache overwrites other ViaCache...
        [InlineData(DeterminismCache2, DeterminismCache1, true)] // ...in either direction
        [InlineData(DeterminismCache1, DeterminismTool, true)] // Tool overwrites ViaCache
        [InlineData(DeterminismTool, DeterminismNone, false)] // None does not overwrite Tool
        [InlineData(DeterminismTool, DeterminismCache1, false)] // ViaCache does not overwrite Tool
        [InlineData(DeterminismCache1, DeterminismNone, false)] // None does not overwrite ViaCache
        [InlineData(DeterminismNone, DeterminismNone, false)] // None does not overwrite None
        [InlineData(DeterminismCache1, DeterminismCache1, false)] // ViaCache does not overwrite same ViaCache
        [InlineData(DeterminismTool, DeterminismTool, false)] // Tool does not overwrite Tool
        [InlineData(DeterminismSinglePhaseNon, DeterminismSinglePhaseNon, true)] // SinglePhaseNonDeterministic overwrites itself
        [InlineData(DeterminismCache1Expired, DeterminismTool, true)] // Expired behaves like None in all cases
        [InlineData(DeterminismCache1Expired, DeterminismCache1, true)]
        [InlineData(DeterminismTool, DeterminismCache1Expired, false)]
        [InlineData(DeterminismCache1, DeterminismCache1Expired, false)]
        [InlineData(DeterminismCache1Expired, DeterminismCache1Expired, false)]
        [InlineData(DeterminismCache1Expired, DeterminismCache2Expired, false)]
        public void DeterminismUpgrade(int fromDeterminism, int toDeterminism, bool shouldReplace)
        {
            Assert.Equal(shouldReplace, Determinism[fromDeterminism].ShouldBeReplacedWith(Determinism[toDeterminism]));
        }
    }
}
