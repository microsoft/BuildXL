// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Distributed.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.DistributedTest
{
    public class RedisSerializerTests
    {
        private readonly IRedisSerializer _serializer = new RedisSerializer();

        [Fact]
        public void FingerprintToRedisKey()
        {
            var fp = Fingerprint.Random();
            var key = _serializer.ToRedisKey(fp);
            Assert.Equal(fp.ToString(), key);
        }

        [Fact]
        public void StrongFingerprintToRedisKey()
        {
            var sfp = StrongFingerprint.Random();
            var key = _serializer.ToRedisKey(sfp);
            var expected =
                sfp.WeakFingerprint +
                RedisSerializer.RedisValueSeparator +
                sfp.Selector.ContentHash +
                RedisSerializer.RedisValueSeparator +
                HexUtilities.BytesToHex(sfp.Selector.Output);

            Assert.Equal(expected, key);
        }

        [Fact]
        public void SelectorsToRedisValues()
        {
            var selectors = Enumerable.Range(0, 3).Select(_ => Selector.Random()).ToList();
            var redisValues = _serializer.ToRedisValues(selectors);
            var expected = selectors.Select(selector =>
                selector.ContentHash.Serialize() + RedisSerializer.RedisValueSeparator + HexUtilities.BytesToHex(selector.Output)).ToArray();

            redisValues.Should().Equal(expected);
        }

        [Fact]
        public void ContentHashListWithDeterminismToRedisValue()
        {
            var chl = ContentHashList.Random();
            var d = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.UtcNow);
            var chlwd = new ContentHashListWithDeterminism(chl, d);
            var redisValue = _serializer.ToRedisValue(chlwd);
            var expected =
                HexUtilities.BytesToHex(d.Serialize()) +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueExistsSemaphore +
                RedisSerializer.RedisValueSeparator +
                chl.Serialize() +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueSeparator;

            Assert.Equal(expected, redisValue);
        }

        [Fact]
        public void NullContentHashListWithDeterminismToRedisValue()
        {
            var d = CacheDeterminism.None;
            var chlwd = new ContentHashListWithDeterminism(null, d);
            var redisValue = _serializer.ToRedisValue(chlwd);
            var expected =
                HexUtilities.BytesToHex(d.Serialize()) +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueSeparator;

            Assert.Equal(expected, redisValue);
        }

        [Fact]
        public void EmptyContentHashListWithDeterminismToRedisValue()
        {
            var chl = ContentHashList.Random(contentHashCount: 0);
            var d = CacheDeterminism.None;
            var chlwd = new ContentHashListWithDeterminism(chl, d);
            var redisValue = _serializer.ToRedisValue(chlwd);
            var expected =
                HexUtilities.BytesToHex(d.Serialize()) +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueExistsSemaphore +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueSeparator;

            Assert.Equal(expected, redisValue);
        }

        [Fact]
        public void ContentHashListWithDeterminismWithPayloadToRedisValue()
        {
            var chl = ContentHashList.Random(payload: ThreadSafeRandom.GetBytes(3));
            var d = CacheDeterminism.None;
            var chlwd = new ContentHashListWithDeterminism(chl, d);
            var redisValue = _serializer.ToRedisValue(chlwd);
            var expected =
                HexUtilities.BytesToHex(d.Serialize()) +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueExistsSemaphore +
                RedisSerializer.RedisValueSeparator +
                chl.Serialize() +
                RedisSerializer.RedisValueSeparator +
                RedisSerializer.RedisValueExistsSemaphore +
                RedisSerializer.RedisValueSeparator +
                HexUtilities.BytesToHex(chl.Payload.ToList());

            Assert.Equal(expected, redisValue);
        }

        [Fact]
        public void SelectorsRoundtrip()
        {
            var selectors = Enumerable.Range(0, 3).Select(_ => Selector.Random()).ToList();
            var redisValues = _serializer.ToRedisValues(selectors);
            var roundtrippedValues = _serializer.AsSelectors(redisValues);

            Assert.Equal(selectors, roundtrippedValues);
        }

        [Fact]
        public void ContentHashListWithDeterminismRoundtrip()
        {
            TestContentHashListWithDeterminismRoundtrip(new ContentHashListWithDeterminism(
                ContentHashList.Random(contentHashCount: 3),
                CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.UtcNow)));
        }

        [Fact]
        public void EmptyContentHashListWithDeterminismRoundtrip()
        {
            TestContentHashListWithDeterminismRoundtrip(new ContentHashListWithDeterminism(
                ContentHashList.Random(contentHashCount: 0),
                CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.UtcNow)));
        }

        [Fact]
        public void NullContentHashListWithDeterminismRoundtrip()
        {
            TestContentHashListWithDeterminismRoundtrip(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
        }

        private void TestContentHashListWithDeterminismRoundtrip(ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            var redisValue = _serializer.ToRedisValue(contentHashListWithDeterminism);
            var roundtrippedValue = _serializer.AsContentHashList(redisValue);

            Assert.Equal(contentHashListWithDeterminism, roundtrippedValue);
        }
    }
}
