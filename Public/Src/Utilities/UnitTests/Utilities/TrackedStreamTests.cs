// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Utilities.Serialization;
using System.IO;
using System;

namespace Test.BuildXL.Utilities
{
    public class TrackedStreamTests : XunitBuildXLTest
    {
        public TrackedStreamTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestTrackedStream()
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(Guid.NewGuid().ToByteArray());
                memoryStream.Position = 0;

                var expectedReadByte = memoryStream.ReadByte();
                var expectedReadBytePosition = memoryStream.Position;
                var expectedReadBytes = new byte[1024];
                var expectedReadByteCount = memoryStream.Read(expectedReadBytes, 0, expectedReadBytes.Length);

                var expectedReadBytesPosition = memoryStream.Position;

                memoryStream.Position = 0;

                using (var statsStream = new TrackedStream(memoryStream))
                {
                    var actualReadByte = statsStream.ReadByte();
                    Assert.Equal(expectedReadByte, actualReadByte);
                    Assert.Equal(expectedReadBytePosition, statsStream.Position);

                    var actualReadBytes = new byte[expectedReadBytes.Length];
                    var actualReadByteCount = statsStream.Read(actualReadBytes, 0, actualReadBytes.Length);
                    Assert.Equal(expectedReadBytesPosition, statsStream.Position);

                    Assert.Equal(expectedReadByteCount, actualReadByteCount);
                    Assert.Equal(expectedReadBytes, actualReadBytes);

                    var position = statsStream.Position;

                    var bytes = new byte[1024];
                    var seekOffset = 243;
                    byte expectedAfterSeekReadByteValue = 123;
                    bytes[seekOffset] = expectedAfterSeekReadByteValue;
                    memoryStream.Write(bytes, 0, bytes.Length);

                    var absoluteSeekOffset = position + seekOffset;
                    var seekResult = statsStream.Seek(absoluteSeekOffset, SeekOrigin.Begin);
                    Assert.Equal(absoluteSeekOffset, seekResult);
                    Assert.Equal(absoluteSeekOffset, statsStream.Position);
                    Assert.Equal(expectedAfterSeekReadByteValue, statsStream.ReadByte());
                }
            }
        }
    }
}
