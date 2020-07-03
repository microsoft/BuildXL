// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace ContentStoreTest.Synchronization
{
    public abstract class SameSingleInstanceTests : SingleInstanceTests
    {
        protected SameSingleInstanceTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
            : base(createFileSystemFunc, logger)
        {
        }

        protected abstract IStartupShutdown CreateInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds);

        protected override IStartupShutdown CreateFirstInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            return CreateInstance(testDirectory, singleInstanceTimeoutSeconds);
        }

        protected override IStartupShutdown CreateSecondInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            return CreateInstance(testDirectory, singleInstanceTimeoutSeconds);
        }
    }
}
