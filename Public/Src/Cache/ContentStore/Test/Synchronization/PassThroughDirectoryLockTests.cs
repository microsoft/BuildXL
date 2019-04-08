// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
