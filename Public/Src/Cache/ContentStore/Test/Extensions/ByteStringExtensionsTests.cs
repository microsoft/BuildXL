// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Service.Grpc;
using ContentStoreTest.Test;
using Google.Protobuf;
using Xunit;

namespace ContentStoreTest.Extensions
{
    public class ByteStringExtensionsTests : TestBase
    {
        public ByteStringExtensionsTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void UnsafeCreateFromBytesCreatesTheInstance()
        {
            byte[] input = new byte[]{1,2,3};
            var byteString = ByteStringExtensions.UnsafeCreateFromBytes(input);

            var byteString2 = ByteString.CopyFrom(input);

            // ByteString uses "value" comparison. So two "strings" are equals even
            // when they point to different byte arrays.
            // This test mostely checks that the hacky solution works.
            Assert.Equal(byteString, byteString2);
        }
    }
}
