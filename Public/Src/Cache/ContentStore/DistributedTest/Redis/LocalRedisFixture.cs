// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Xunit;

namespace ContentStoreTest.Distributed.Redis
{
    /// <summary>
    /// Fixture (i.e. resource bag) associated with a group of tests (with xunit test collection).
    /// </summary>
    /// <remarks>
    /// There is no simple way to free up resources when the assembly execution is over.
    /// Test fixture is one way of achieving it: xunit creates a fixture and passes it for every test that is associated with a given collection.
    /// When all the tests are finished, xunit cleans up the fixture.
    /// This fixture is used for keeping an object pool with local databases and cleaning them up when all redis-related tests are finished.
    /// </remarks>
    public sealed class LocalRedisFixture : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public ObjectPool<LocalRedisProcessDatabase> DatabasePool { get; }
            = new ObjectPool<LocalRedisProcessDatabase>(
            () => new LocalRedisProcessDatabase(),
            i => i);

        /// <summary>
        /// Cleans up all the resources used by the group of tests: i.e. closes all local redis instances.
        /// </summary>
        public void Dispose()
        {
            while (true)
            {
                var database = DatabasePool.GetInstance();
                if (!database.Instance.Initialized)
                {
                    break;
                }

                database.Instance.Close();
            }
        }
    }

    /// <summary>
    /// Custom collection that uses <see cref="LocalRedisFixture"/>.
    /// </summary>
    [CollectionDefinition("Redis-based tests")]
    public class LocalRedisCollection : ICollectionFixture<LocalRedisFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
