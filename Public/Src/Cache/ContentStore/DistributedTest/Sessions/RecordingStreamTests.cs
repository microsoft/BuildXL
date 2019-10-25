// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Distributed.Sessions;
using System;
using System.IO;
using Xunit;

namespace ContentStoreTest.Distributed.Sessions
{
    public class RecordingStreamTests
    {
        [Fact]
        public void TestCanReadOrCanWrite()
        {
            var inner = new MemoryStream();
            var readWrapper = RecordingStream.ReadRecordingStream(inner, size: null);
            Assert.True(readWrapper.CanRead);
            Assert.False(readWrapper.CanWrite);

            var writeWrapper = RecordingStream.WriteRecordingStream(inner);
            Assert.False(writeWrapper.CanRead);
            Assert.True(writeWrapper.CanWrite);
        }

        [Fact]
        public void BytesAreWrittenAndRecorded()
        {
            var bytes = new byte[] { 74, 117, 97, 110, 32, 67, 97, 114, 108, 111, 115, 32, 59, 41 };
            var inner = new MemoryStream();

            var wrapper = RecordingStream.WriteRecordingStream(inner);
            
            wrapper.Write(bytes, 0, bytes.Length);

            var recorded = wrapper.RecordedBytes;
            Assert.Equal(bytes.Length, recorded.Length);
            Assert.Equal(bytes, recorded);
        }

        [Fact]
        public void BytesAreReadAndRecorded()
        {
            var bytes = new byte[] { 74, 117, 97, 110, 32, 67, 97, 114, 108, 111, 115, 32, 59, 41 };
            var inner = new MemoryStream();
            inner.Write(bytes, 0, bytes.Length);
            inner.Position = 0;

            var wrapper = RecordingStream.ReadRecordingStream(inner, bytes.Length);
            var buffer = new byte[bytes.Length];
            var bytesRead = wrapper.Read(buffer, 0, bytes.Length);

            Assert.Equal(bytes.Length, bytesRead);
            Assert.Equal(bytes, buffer);
            Assert.Equal(bytes, wrapper.RecordedBytes);
        }

        [Fact]
        public void ExceedingCapacityThrows()
        {
            var bytes = new byte[] { 74, 117, 97, 110, 32, 67, 97, 114, 108, 111, 115, 32, 59, 41 };
            var inner = new MemoryStream();
            inner.Write(bytes, 0, bytes.Length);
            inner.Position = 0;

            var wrapper = RecordingStream.ReadRecordingStream(inner, bytes.Length - 1);
            var buffer = new byte[bytes.Length];
            Assert.Throws<InvalidOperationException>(() => wrapper.Read(buffer, 0, bytes.Length));
        }
    }
}
