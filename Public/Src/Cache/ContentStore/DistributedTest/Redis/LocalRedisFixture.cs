// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

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

        public ObjectPool<AzuriteStorageProcess> EmulatorPool { get; }
            = new(
            () => new AzuriteStorageProcess(),
            i => i);

        /// <summary>
        /// Cleans up all the resources used by the group of tests: i.e. closes all local redis instances.
        /// </summary>
        public void Dispose()
        {
            Console.WriteLine("LocalRedisFixture.Dispose");

            while (true)
            {
                var database = EmulatorPool.GetInstance();
                if (!database.Instance.Initialized)
                {
                    break;
                }

                database.Instance.Close();
            }
        }
    }
}
