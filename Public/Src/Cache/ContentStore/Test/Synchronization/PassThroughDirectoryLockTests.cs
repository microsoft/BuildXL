// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Synchronization
{
    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    public class PassThroughDirectoryLockTests : DirectoryLockTests
    {
        public PassThroughDirectoryLockTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), output)
        {
        }
    }
}
