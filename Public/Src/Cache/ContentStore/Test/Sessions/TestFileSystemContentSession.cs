// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace ContentStoreTest.Sessions
{
    public class TestFileSystemContentSession : FileSystemContentSession
    {
        public TestFileSystemContentSession(string name, ImplicitPin implicitPin, IContentStoreInternal store)
            : base(name, implicitPin, store)
        {
        }

        public async Task<IReadOnlyList<ContentHash>> EnumerateHashes()
        {
            IReadOnlyList<ContentInfo> contentInfoList = await Store.EnumerateContentInfoAsync();
            return contentInfoList.Select(x => x.ContentHash).ToList();
        }
    }
}
