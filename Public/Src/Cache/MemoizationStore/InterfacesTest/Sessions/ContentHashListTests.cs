// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public class ContentHashListTests
    {
        private static readonly ContentHash[] ContentHashArray = {ContentHash.Random()};
        private static readonly byte[] Payload = Enumerable.Repeat((byte)0, 7).ToArray();

        [Fact]
        public void ConstructWithPayload()
        {
            var contentHashList = new ContentHashList(ContentHashArray, Payload);
            Assert.True(contentHashList.HasPayload);
            Assert.NotNull(contentHashList.Payload);
        }

        [Fact]
        public void ConstructWithoutPayload()
        {
            var contentHashList = new ContentHashList(ContentHashArray);
            Assert.False(contentHashList.HasPayload);
            Assert.Null(contentHashList.Payload);
        }

        [Fact]
        public void TooLargePayloadThrows()
        {
            var payload = Enumerable.Repeat((byte)0, 1025).ToArray();
            ContentHashList contentHashList = null;
            Action a = () => contentHashList = new ContentHashList(ContentHashArray, payload);
            ArgumentException ex = Assert.Throws<ArgumentException>(a);

            Assert.Contains("<= 1kB", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(contentHashList);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var v1 = new ContentHashList(ContentHashArray);
            var v2 = new ContentHashList(ContentHashArray) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new ContentHashList(ContentHashArray);
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullPayload()
        {
            var v1 = ContentHashList.Random();
            var v2 = ContentHashList.Random();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseMismatchPayload()
        {
            var v1 = new ContentHashList(ContentHashArray, new byte[] {0, 1});
            var v2 = new ContentHashList(ContentHashArray, new byte[] {0});
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullThisPayload()
        {
            var v1 = new ContentHashList(ContentHashArray);
            var v2 = new ContentHashList(ContentHashArray, new byte[] {0});
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullOtherPayload()
        {
            var v1 = new ContentHashList(ContentHashArray, new byte[] {0});
            var v2 = new ContentHashList(ContentHashArray);
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrueNotReferenceEqualContentHashArray()
        {
            var b = Enumerable.Repeat((byte)0, 20).ToArray();
            var v1 = new ContentHashList(new[] {new ContentHash(HashType.SHA1, b)});
            var v2 = new ContentHashList(new[] {new ContentHash(HashType.SHA1, b)}) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrueWithPayload()
        {
            var v1 = new ContentHashList(ContentHashArray, new byte[] {1, 2, 3});
            var v2 = new ContentHashList(ContentHashArray, new byte[] {1, 2, 3});
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseWithPayload()
        {
            var v1 = new ContentHashList(ContentHashArray, new byte[] {1, 2, 3});
            var v2 = new ContentHashList(ContentHashArray, new byte[] {1, 1, 3});
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = new ContentHashList(ContentHashArray);
            var v2 = new ContentHashList(ContentHashArray);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = ContentHashList.Random();
            var v2 = ContentHashList.Random();
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void RandomNullPayload()
        {
            Assert.Null(ContentHashList.Random().Payload);
        }

        [Fact]
        public void SerializeRoundtrip()
        {
            var value = ContentHashList.Random();
            Utilities.TestSerializationRoundtrip(value, value.Serialize, ContentHashList.Deserialize);
        }

        [Fact]
        public void SerializeRoundtripNonNullPayload()
        {
            var value = ContentHashList.Random(payload: new byte[] { 1, 2, 3 });
            Utilities.TestSerializationRoundtrip(value, value.Serialize, ContentHashList.Deserialize);
        }

        [Fact]
        public void SerializeWithDeterminismRoundtrip()
        {
            var value = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            Utilities.TestSerializationRoundtrip(value, value.Serialize, ContentHashListWithDeterminism.Deserialize);
        }

        [Fact]
        public void SerializeWithDeterminismRoundtripNoContentHashList()
        {
            var value = new ContentHashListWithDeterminism(null, CacheDeterminism.None);
            Utilities.TestSerializationRoundtrip(value, value.Serialize, ContentHashListWithDeterminism.Deserialize);
        }
    }
}
