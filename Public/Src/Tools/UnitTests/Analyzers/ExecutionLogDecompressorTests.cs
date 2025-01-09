// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Execution.Analyzer;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using ZstdSharp;
using ConsoleRedirector = Test.BuildXL.TestUtilities.ConsoleRedirector;
using ExecutionLogDecompressorConstants = BuildXL.Execution.Analyzer.ExecutionLogDecompressor.ExecutionLogDecompressorConstants;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Test the functionality of ExecutionLogDecompressor.
    /// </summary>
    public class ExecutionLogDecompressorTests : XunitBuildXLTest
    {
        private const uint TestCase = 0;

        private string m_compressedFilePath;

        public ExecutionLogDecompressorTests(ITestOutputHelper output)
            : base(output) { }

        /// <summary>
        /// Validates the successful decompression of the execution log file by the ExecutionLogDecompressor.
        /// </summary>
        [Fact]
        public void TestGetDecompressedExecutionLogFile()
        {
            using var consoleRedirector = ExecutionLogDecompressorTestHelper(compress: true, isExecutionLogFilePathNotNull: true);
            Assert.Contains(ExecutionLogDecompressorConstants.IsZstdCompressedMessage, consoleRedirector.GetOutput());
            Assert.Contains(ExecutionLogDecompressorConstants.DecompressedFileSuccessMessage, consoleRedirector.GetOutput());

            // If the decompressed file is already present then we do not decompress the file, we reuse the existing one
            ExecutionLogDecompressor.GetDecompressedExecutionLogFile(m_compressedFilePath);
            Assert.Contains(ExecutionLogDecompressorConstants.IsZstdCompressedMessage, consoleRedirector.GetOutput());
            Assert.Contains(ExecutionLogDecompressorConstants.DecompressedExecutionLogFileAlreadyExists, consoleRedirector.GetOutput());
        }

        /// <summary>
        /// Checks whether the specified file is compressed using the Zstd compression stream.
        /// </summary>
        [Fact]
        public void TestFileForZstdCompression()
        {
            using var consoleRedirector = ExecutionLogDecompressorTestHelper(compress: false, isExecutionLogFilePathNotNull: true);
            Assert.Contains(ExecutionLogDecompressorConstants.ExecutionLogFileIsNotZstdCompressedMessage, consoleRedirector.GetOutput());            
        }

        /// <summary>
        /// Validates that the ExecutionLogDecompressor generates an appropriate error message for an invalid execution log file path.
        /// </summary>
        [Fact]
        public void TestErrorMessageForInvalidLogFilePath()
        {
            bool isException = false;
            try
            {
                using var consoleRedirector = ExecutionLogDecompressorTestHelper(compress: true, isExecutionLogFilePathNotNull: false);
            }
            catch (Exception ex)
            {
                isException = true;
                Assert.Contains(ExecutionLogDecompressorConstants.ExecutionLogFilePathErrorMessage, ex.ToString());
            }

            XAssert.IsTrue(isException);
        }

        private ConsoleRedirector ExecutionLogDecompressorTestHelper(bool compress = true, bool isExecutionLogFilePathNotNull = true)
        {
            string consoleOutput = string.Empty;
            ConsoleRedirector consoleRedirector = new ConsoleRedirector(ref consoleOutput);
            m_compressedFilePath = CreateAndCompressExecutionLogFile(compress, isExecutionLogFilePathNotNull);
            var decompressedFile = ExecutionLogDecompressor.GetDecompressedExecutionLogFile(m_compressedFilePath);
            return consoleRedirector;
        }

        /// <summary>
        /// Writes dummy binary log data to the specified stream for testing purposes.
        /// </summary>
        private void WriteToExecutionLogFile(Stream stream, string source)
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            Guid logId = Guid.NewGuid();

            using (BinaryLogger writer = new BinaryLogger(stream, context, logId, lastStaticAbsolutePathIndex: 0, closeStreamOnDispose: false))
            {
                using (var eventScope = writer.StartEvent(TestCase, workerId: 0))
                {
                    eventScope.Writer.Write(source);
                    eventScope.Writer.Write("test string");
                    eventScope.Writer.Write(12345);
                }
            }
        }

        /// <summary>
        /// Create dummy binary log data file for testing purpose.
        /// </summary>
        private string CreateAndCompressExecutionLogFile(bool compress = true, bool isExecutionLogFilePathNotNull = true)
        {
            if (!isExecutionLogFilePathNotNull)
            {
                return null;
            }

            FileStream fileStream = null;
            Stream stream = null;

            var source = Path.Combine(TestOutputDirectory, "test", "Logs");
            string filePath = Path.Combine(source, "file.xlg");

            FileUtilities.CreateDirectory(source);

            using (fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (compress)
                {
                    using (stream = new CompressionStream(fileStream, level: 2, leaveOpen: false))
                    {
                        WriteToExecutionLogFile(stream, source);
                    }
                }
                else
                {
                    WriteToExecutionLogFile(fileStream, source);
                }

            }

            return filePath;
        }
    }
}
