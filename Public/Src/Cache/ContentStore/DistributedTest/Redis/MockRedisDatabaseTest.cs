// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Redis
{
    public class MockRedisDatabaseTest
    {
        [Fact]
        public async Task MultipleStringSetBit()
        {
            const string key = "TestKey";

            var db = new MockRedisDatabase(SystemClock.Instance);

            db.StringSetBitAsync(key, 0, true).GetAwaiter().GetResult();
            await ReturnsExpected(db, key, new byte[] { 0x80 });

            db.StringSetBitAsync(key, 1, true).GetAwaiter().GetResult();
            await ReturnsExpected(db, key, new byte[] { 0xC0 });

            db.StringSetBitAsync(key, 0, false).GetAwaiter().GetResult();
            await ReturnsExpected(db, key, new byte[] { 0x40 });
        }

        [Fact]
        public async Task StringSetBitMultipleByteResponse()
        {
            const string key = "TestKey";

            var db = new MockRedisDatabase(SystemClock.Instance);
            await db.StringSetBitAsync(key, 8, true);

            await ReturnsExpected(db, key, new byte[] { 0, 0x80 });
        }

        private async Task ReturnsExpected(MockRedisDatabase db, string key, byte[] expected)
        {
            byte[] locations = await db.StringGetAsync(key);
            locations.Should().Equal(expected);
        }
    }
}
