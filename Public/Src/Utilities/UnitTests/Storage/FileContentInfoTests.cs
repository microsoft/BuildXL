// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Storage
{
    public sealed class FileContentInfoTests : XunitBuildXLTest
    {
        public FileContentInfoTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void FileContentInfoEquality()
        {
            ContentHash hash1 = ContentHashingUtilities.CreateRandom();
            ContentHash hash2 = hash1;
            ContentHash hash3 = ContentHashingUtilities.CreateRandom();

            StructTester.TestEquality(
                baseValue: new FileContentInfo(hash1, 100),
                equalValue: new FileContentInfo(hash2, 100),
                notEqualValues: new[]
                                {
                                    new FileContentInfo(hash3, 100),
                                    new FileContentInfo(hash2, 200),
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }
    }
}
