// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using StackExchange.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    public static class MockRedisDatabaseFactory
    {
        public static IConnectionMultiplexer CreateConnection<T>(T testDb, T testBatch = null)
            where T : class, ITestRedisDatabase
        {
            var mockDb = CreateRedisDatabase(testDb, testBatch);
            var mockConn = new TestConnectionMultiplexer(mockDb);

            return mockConn;
        }

        private static IDatabase CreateRedisDatabase<T>(T testDb, T testBatch = null)
            where T : class, ITestRedisDatabase
        {
            testBatch = testBatch ?? testDb;
            var mockBatch = new TestDatabase(testBatch, null);

            var mockDb = new TestDatabase(testDb, mockBatch);

            return mockDb;
        }
    }
}
