// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public sealed class FileLoggerTests : TemporaryStorageTestBase
    {
        public FileLoggerTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BasicFunctionalityTest(bool logVerbose)
        {
            var logDir = Path.Combine(TemporaryDirectory, nameof(FileLoggerTests), nameof(BasicFunctionalityTest));
            var logFileName = "test.log";
            var monikerId = "moniker";
            var prefix = "QWERTY";
            var infoMessage = "imessage";
            var warningMessage = "wmessage";
            var errorMessage = "emessage";
            var verboseMessage = "vmessage";

            Directory.CreateDirectory(logDir);

            // create a logger and log a couple of messages
            string logFileFullPath = null;
            using (var logger = new FileLogger(logDir, logFileName, monikerId, logVerbose, prefix))
            {
                XAssert.AreEqual(logVerbose, logger.IsLoggingVerbose);
                XAssert.AreEqual(prefix, logger.Prefix);

                logger.Info(infoMessage);
                logger.Warning(warningMessage);
                logger.Error(errorMessage);
                logger.Verbose(verboseMessage);
                logFileFullPath = logger.LogFilePath;
            }

            // check that the log file was produced
            XAssert.FileExists(logFileFullPath);

            // check that the verbose message was not logged unless 'logVerbose' is true
            var logLines = File.ReadAllLines(logFileFullPath);
            XAssert.AreEqual(logVerbose ? 4 : 3, logLines.Length);

            // check that every line contains the prefix;
            XAssert.All(logLines, line => line.Contains(prefix));

            // check individual log messages
            XAssert.Contains(logLines[0], infoMessage);
            XAssert.Contains(logLines[1], warningMessage);
            XAssert.Contains(logLines[2], errorMessage);
            if (logVerbose)
            {
                XAssert.Contains(logLines[3], verboseMessage);
            }

            // create the same logger and assert that it's not going to overwrite the the old log file
            string logFile2FullPath = null;
            using (var logger2 = new FileLogger(logDir, logFileName, monikerId, logVerbose, prefix))
            {
                XAssert.AreNotEqual(logFileFullPath, logger2.LogFilePath);
                logger2.Log(LogLevel.Info, "hi");
                logFile2FullPath = logger2.LogFilePath;
            }

            XAssert.FileExists(logFileFullPath);
            XAssert.FileExists(logFile2FullPath);
        }

        [Fact]
        public void TestConcurrentDispose()
        {
            using var logger = new FileLogger(TemporaryDirectory, nameof(TestConcurrentDispose), "moniker", logVerbose: false);
            Enumerable.Range(0, 10).AsParallel().ForAll(i => { logger.Flush(); logger.Dispose(); });
        }
    }
}
