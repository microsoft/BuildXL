// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public class ZeroLeftPaddedStreamTests : XunitBuildXLTest
    {
        public ZeroLeftPaddedStreamTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestBasicFunctionality()
        {
            var underlyingStream = new MemoryStream(new byte[5] { 1, 2, 3, 4, 5 });
            var paddedStream = new ZeroLeftPaddedStream(underlyingStream, 10);

            // Total length should be 10, even if the underlying stream is smaller
            XAssert.AreEqual(10, paddedStream.Length);

            // Check Position works
            paddedStream.Position = 9;
            XAssert.AreEqual(9, paddedStream.Position);

            // Read the whole stream. We should get zeros in the first 5 positions
            paddedStream.Position = 0;
            XAssert.AreEqual(0, paddedStream.Position);
            var result = new byte[10];
            var bytesRead = paddedStream.Read(result, 0, 10);
            XAssert.AreArraysEqual(new byte[] { 0, 0, 0, 0, 0, 1, 2, 3, 4, 5 }, result, true);
            XAssert.AreEqual(10, bytesRead);

            // Read a part of the stream offsetting the destination
            System.Array.Clear(result, 0, 10);
            paddedStream.Position = 4;
            bytesRead = paddedStream.Read(result, 1, 3);
            XAssert.AreArraysEqual(new byte[] { 0, 0, 1, 2, 0, 0, 0, 0, 0, 0 }, result, true);
            XAssert.AreEqual(3, bytesRead);
        }

        [Fact]
        public void TestSeek()
        {
            var underlyingStream = new MemoryStream(new byte[5] { 1, 2, 3, 4, 5 });
            var paddedStream = new ZeroLeftPaddedStream(underlyingStream, 10);

            var result = new byte[2];

            var newPos = paddedStream.Seek(4, SeekOrigin.Begin);
            XAssert.AreEqual(4, newPos);
            paddedStream.Read(result, 0, 2);
            XAssert.AreEqual(0, result[0]);
            XAssert.AreEqual(1, result[1]);

            newPos = paddedStream.Seek(7, SeekOrigin.Begin);
            XAssert.AreEqual(7, newPos);
            paddedStream.Read(result, 0, 2);
            XAssert.AreEqual(3, result[0]);
            XAssert.AreEqual(4, result[1]);

            newPos = paddedStream.Seek(-1, SeekOrigin.End);
            XAssert.AreEqual(9, newPos);
            paddedStream.Read(result, 0, 1);
            XAssert.AreEqual(5, result[0]);

            newPos = paddedStream.Seek(-6, SeekOrigin.End);
            XAssert.AreEqual(4, newPos);
            paddedStream.Read(result, 0, 2);
            XAssert.AreEqual(0, result[0]);
            XAssert.AreEqual(1, result[1]);

            paddedStream.Position = 6;
            newPos = paddedStream.Seek(-2, SeekOrigin.Current);
            XAssert.AreEqual(4, newPos);
            paddedStream.Read(result, 0, 2);
            XAssert.AreEqual(0, result[0]);
            XAssert.AreEqual(1, result[1]);

            paddedStream.Position = 4;
            newPos = paddedStream.Seek(2, SeekOrigin.Current);
            XAssert.AreEqual(6, newPos);
            paddedStream.Read(result, 0, 2);
            XAssert.AreEqual(2, result[0]);
            XAssert.AreEqual(3, result[1]);
        }
    }
}

