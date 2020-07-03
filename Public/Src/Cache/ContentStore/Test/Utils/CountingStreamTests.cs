// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class CountingStreamTests
    {
        [Fact]
        public void ByteArrayWriteIsCorrectlyCounted()
        {
            var bytes = new byte[] { 74, 117, 97, 110, 32, 67, 97, 114, 108, 111, 115, 32, 59, 41 };
            var inner = new MemoryStream(capacity: bytes.Length);
            var wrapper = new CountingStream(inner);
            wrapper.Write(bytes, 0, bytes.Length);

            Assert.Equal(bytes.Length, wrapper.BytesWritten);
            Assert.Equal(bytes, inner.ToArray());
        }

        [Fact]
        public void NonWriteableStreamThrows()
        {
            var bytes = new byte[] { 74, 117, 97, 110, 32, 67, 97, 114, 108, 111, 115, 32, 59, 41 };
            var inner = new MemoryStream(bytes, writable: false);
            Assert.ThrowsAny<Exception>(() => new CountingStream(inner));
        }
    }
}
