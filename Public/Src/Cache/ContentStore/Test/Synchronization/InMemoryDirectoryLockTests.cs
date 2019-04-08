// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using Xunit.Abstractions;

namespace ContentStoreTest.Synchronization
{
    public class InMemoryDirectoryLockTests : DirectoryLockTests
    {
        public InMemoryDirectoryLockTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), output)
        {
        }
    }
}
