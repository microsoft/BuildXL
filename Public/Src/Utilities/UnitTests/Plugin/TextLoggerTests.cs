// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Plugin;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Plugin
{
    public sealed class TextLoggerTests : TemporaryStorageTestBase
    {
        public TextLoggerTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void BasicFunctionalityTest()
        {
            var logDir = Path.Combine(TemporaryDirectory, nameof(TextLoggerTests), nameof(BasicFunctionalityTest));
            var logFileName = "test.log";
            var port = "60000";
            var infoMessage = "Info";
            var debugMessage = "Debug";
            var errorMessage = "Error";
            var warningMessage = "Warning";
            var message = "none";

            Directory.CreateDirectory(logDir);

            // create a logger and log a couple of messages
            string logFileFullPath = Path.Combine(logDir, logFileName + $"-{port}.log");
            using (var logger = PluginLogUtils.GetLogger<TextLoggerTests>(logDir, logFileName, port) )
            { 
                logger.Info(message);
                logger.Warning(message);
                logger.Error(message);
                logger.Debug(message);
            }

            // check that the log file was produced
            XAssert.FileExists(logFileFullPath);

            // check that the verbose message was not logged unless 'logVerbose' is true
            var logLines = File.ReadAllLines(logFileFullPath);

            // check that every line contains the prefix;
            XAssert.All(logLines, line => line.Contains(nameof(TextLoggerTests)));

            // check individual log messages
            XAssert.Contains(logLines[0], infoMessage);
            XAssert.Contains(logLines[1], warningMessage);
            XAssert.Contains(logLines[2], errorMessage);
            XAssert.Contains(logLines[3], debugMessage);
        }
    }
}
