// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ContentHashWithSizeAndLastAccessTimeTests
    {
        [Fact]
        public void TestToString()
        {
            // This is not a real test, rather a test that shows the way generators are used in this codebase.
            var entry = new ContentHashWithLastAccessTime(ContentHash.Random(), DateTime.UtcNow);
            Assert.Contains("Hash", entry.ToString());
            Assert.Contains("LastAccessTime", entry.ToString());
        }
    }
}
