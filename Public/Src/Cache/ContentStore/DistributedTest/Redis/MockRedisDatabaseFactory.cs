// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using StackExchange.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    public static class MockRedisDatabaseFactory
    {
        public static TestConnectionMultiplexer CreateConnection<T>(T testDb, T testBatch = null, Func<bool> throwConnectionExceptionOnGet = null)
            where T : class, ITestRedisDatabase
        {
            var mockDb = CreateRedisDatabase(testDb, testBatch);
            var mockConn = new TestConnectionMultiplexer(mockDb, throwConnectionExceptionOnGet);

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
