// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public class ContentHashListExtensionsTests
    {
        private const int RandomBytesSize = 27;

        [Fact]
        public void TestContentHashListsNoPayloadOrderInvalidation()
        {
            var vsoContentHash1 = ContentHash.Random();
            var vsoContentHash2 = ContentHash.Random();

            var contentHashList = new ContentHashList(new[] { vsoContentHash1, vsoContentHash2 });
            byte[] hashOfContentHashes = contentHashList.GetHashOfHashes();

            var secondOrderContentHashList = new ContentHashList(new[] { vsoContentHash2, vsoContentHash1 });
            byte[] secondOrderHashOfContentHashes = secondOrderContentHashList.GetHashOfHashes();

            ByteArrayComparer.ArraysEqual(hashOfContentHashes, secondOrderHashOfContentHashes).Should().BeFalse();
        }

        [Fact]
        public void TestContentHashListsPayloadDoesNotInvalidate()
        {
            var vsoContentHash1 = ContentHash.Random();
            var vsoContentHash2 = ContentHash.Random();

            var contentHashes = new[] { vsoContentHash1, vsoContentHash2 };
            var contentHashList = new ContentHashList(contentHashes);
            byte[] hashOfContentHashes = contentHashList.GetHashOfHashes();

            var secondOrderContentHashList = new ContentHashList(contentHashes, ThreadSafeRandom.GetBytes(RandomBytesSize));
            byte[] secondOrderHashOfContentHashes = secondOrderContentHashList.GetHashOfHashes();

            ByteArrayComparer.ArraysEqual(hashOfContentHashes, secondOrderHashOfContentHashes).Should().BeTrue();
        }

        [Fact]
        public void TestContentHashListsEqualityCheckWithPayload()
        {
            var byteStream = ThreadSafeRandom.GetBytes(RandomBytesSize);
            var vsoContentHash1 = ContentHash.Random();
            var vsoContentHash2 = ContentHash.Random();

            var contentHashes = new[] { vsoContentHash1, vsoContentHash2 };
            var contentHashList = new ContentHashList(contentHashes, byteStream);
            byte[] hashOfContentHashes = contentHashList.GetHashOfHashes();

            var secondOrderContentHashList = new ContentHashList(contentHashes, byteStream);
            byte[] secondOrderHashOfContentHashes = secondOrderContentHashList.GetHashOfHashes();

            ByteArrayComparer.ArraysEqual(hashOfContentHashes, secondOrderHashOfContentHashes).Should().BeTrue();
        }

        [Fact]
        public void TestContentHashListsEqualityCheckNoPayload()
        {
            var vsoContentHash1 = ContentHash.Random();
            var vsoContentHash2 = ContentHash.Random();

            var contentHashes = new[] { vsoContentHash1, vsoContentHash2 };
            var contentHashList = new ContentHashList(contentHashes);
            byte[] hashOfContentHashes = contentHashList.GetHashOfHashes();

            var secondOrderContentHashList = new ContentHashList(contentHashes);
            byte[] secondOrderHashOfContentHashes = secondOrderContentHashList.GetHashOfHashes();

            ByteArrayComparer.ArraysEqual(hashOfContentHashes, secondOrderHashOfContentHashes).Should().BeTrue();
        }

        [Fact]
        public void TestComparerAllLengths()
        {
            Random random = new Random();
            byte[] byteArray = new byte[24];
            random.NextBytes(byteArray);
            for (var i = 0; i < 24; i++)
            {
                byte[] iByteArray = new byte[i];
                Array.Copy(byteArray, iByteArray, i);
                ByteArrayComparer.Instance.GetHashCode(iByteArray).Equals(ByteArrayComparer.Instance.GetHashCode(iByteArray)).Should().BeTrue();
            }
        }
    }
}
