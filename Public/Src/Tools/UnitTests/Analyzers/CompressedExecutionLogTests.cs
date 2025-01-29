// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Execution.Analyzer;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using ZstdSharp;

namespace Test.Tool.Analyzers
{
    public class CompressedExecutionLogTests : TemporaryStorageTestBase
    {
        public CompressedExecutionLogTests()
        {
            // Override the location for decompressed execution log files to ensure it is unique for each test case.
            // This isn't necessary for general test cases since the decompressed files are robust to concurrent
            // access, but it is necessary for these test cases which validate that concurrent access handling.
            Environment.SetEnvironmentVariable(AnalysisInput.BuildXLAnalyzerWorkingDirEnvVar, TemporaryDirectory, EnvironmentVariableTarget.Process);
        }

        [Fact]
        public void UncompressedStream()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write([0x00, 0x01, 0x02, 0x03]);
                var resultingStream = AnalysisInput.SwapForDecompressedStream(ms, out bool usedCachedStream);

                // The stream is not compressed. The original stream should be returned
                Assert.Same(ms, resultingStream);
                Assert.False(usedCachedStream);
            }
        }

        [Fact]
        public void CompressedStream()
        {
            using (MemoryStream ms = new MemoryStream())
            using (DisposeTrackingStream trackedMs = new DisposeTrackingStream(ms))
            {
                WriteCompressedData(ms);

                using (var resultingStream = AnalysisInput.SwapForDecompressedStream(trackedMs, out bool usedCachedStream))
                {
                    // The compressed stream should have been decompressed, returning a new stream
                    Assert.NotSame(ms, resultingStream);
                    Assert.False(usedCachedStream);

                    // The original compressed stream should have been disposed
                    Assert.True(trackedMs.IsDisposed);
                }
            }
        }

        [Fact]
        public void ReuseExistingDecompressedStream()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                WriteCompressedData(ms);

                // Open two streams with identical compressed data. The stream should be decompressed the first time
                // and the second time should noop and not recreate the decompressed file stream.
                using (FileStream firstDecompression = CopyToTempStreamAndDecompress(ms, out bool usedCachedStream) as FileStream)
                {
                    Assert.NotNull(firstDecompression);
                    Assert.False(usedCachedStream);
                }

                using (FileStream secondDecompression = CopyToTempStreamAndDecompress(ms, out bool usedCachedStream) as FileStream)
                {
                    Assert.NotNull(secondDecompression);
                    Assert.True(usedCachedStream);
                }

                // Change the input stream and ensure the cached stream gets recomputed
                WriteCompressedData(ms, additionalData: 0x14);

                using (FileStream thirdDecompression = CopyToTempStreamAndDecompress(ms, out bool usedCachedStream) as FileStream)
                {
                    Assert.NotNull(thirdDecompression);
                    Assert.False(usedCachedStream);
                }
            }
        }

        [Fact]
        public void ConcurrentAccess()
        {
            using (MemoryStream ms1 = new MemoryStream())
            using (MemoryStream ms2 = new MemoryStream())
            {
                // Set up 2 compressed streams with different data to be decompressed concurrently
                WriteCompressedData(ms1);
                WriteCompressedData(ms2, additionalData: 0x14);

                // Decompress the original stream and hold the handle
                using (FileStream firstDecompression = CopyToTempStreamAndDecompress(ms1, out _) as FileStream)
                {
                    Assert.NotNull(firstDecompression);

                    // Concurrently decompress a 2nd stream
                    string secondDecompressionPath;
                    using (FileStream secondDecompression = CopyToTempStreamAndDecompress(ms2, out bool secondUsedCachedStream) as FileStream)
                    {
                        Assert.NotNull(secondDecompression);
                        secondDecompressionPath = secondDecompression.Name;
                        // The paths of the decompressed streams should not match
                        Assert.NotEqual(firstDecompression.Name, secondDecompression.Name);
                        Assert.False(secondUsedCachedStream);
                    }

                    // The second decompressed file should be deleted once the stream is disposed
                    Assert.False(File.Exists(secondDecompressionPath));
                }
            }
        }

        /// <summary>
        /// Writes consistent garbage compressed data to a stream and resets the position to 0.
        /// </summary>
        private void WriteCompressedData(MemoryStream ms, byte additionalData = byte.MinValue)
        {
            ms.Position = 0;
            using (CompressionStream cs = new CompressionStream(ms, leaveOpen: true))
            {
                cs.Write([0x00, 0x01, 0x02, 0x03]);
                cs.WriteByte(additionalData);
            }

            ms.Position = 0;
        }

        /// <summary>
        /// Copies the stream to a temporary intermediate stream and decompresses it. The input stream will not be disposed.
        /// </summary>
        /// <returns>Result of <see cref="AnalysisInput.SwapForDecompressedStream(Stream)"/></returns>
        private static Stream CopyToTempStreamAndDecompress(MemoryStream stream, out bool usedCachedStream)
        {
            using (MemoryStream temp = new MemoryStream())
            {
                stream.Position = 0;
                stream.CopyTo(temp);
                return AnalysisInput.SwapForDecompressedStream(temp, out usedCachedStream);
            }
        }

        /// <summary>
        /// Wraps a stream, passing through all operations. Exposes whether or not Dispose() was called for sake of testing.
        /// </summary>
        private class DisposeTrackingStream : Stream
        {
            private readonly Stream m_innerStream;
            public bool IsDisposed { get; private set; }

            public DisposeTrackingStream(Stream innerStream)
            {
                m_innerStream = innerStream;
            }

            public override bool CanRead => m_innerStream.CanRead;
            public override bool CanSeek => m_innerStream.CanSeek;
            public override bool CanWrite => m_innerStream.CanWrite;
            public override long Length => m_innerStream.Length;

            public override long Position
            {
                get => m_innerStream.Position;
                set => m_innerStream.Position = value;
            }

            public override void Flush() => m_innerStream.Flush();

            public override int Read(byte[] buffer, int offset, int count) => m_innerStream.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => m_innerStream.Seek(offset, origin);

            public override void SetLength(long value) => m_innerStream.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) => m_innerStream.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing && !IsDisposed)
                {
                    IsDisposed = true;
                    m_innerStream.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
