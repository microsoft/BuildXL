// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using BuildXL.Plugin;
using Xunit;

namespace Test.BuildXL.Plugin
{
    public sealed class TextLoggerTests : IDisposable
    {
        private static int s_counter;
        private readonly string m_tempDir;
        public string TemporaryDirectory => m_tempDir;

        // TODO: Inherit from TemporaryStorageTestBase once it is ported to xunit v3.
        public TextLoggerTests()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "bxl-tests", "TextLogger", Interlocked.Increment(ref s_counter).ToString());
            if (Directory.Exists(m_tempDir)) Directory.Delete(m_tempDir, true);
            Directory.CreateDirectory(m_tempDir);
        }

        public void Dispose() { try { Directory.Delete(m_tempDir, true); } catch (IOException) { } }

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
            Assert.True(File.Exists(logFileFullPath), $"Log file not found: {logFileFullPath}");

            // check that the verbose message was not logged unless 'logVerbose' is true
            var logLines = File.ReadAllLines(logFileFullPath);

            // check that every line contains the prefix
            Assert.All(logLines, line => Assert.Contains(nameof(TextLoggerTests), line));

            // check individual log messages
            Assert.Contains(infoMessage, logLines[0]);
            Assert.Contains(warningMessage, logLines[1]);
            Assert.Contains(errorMessage, logLines[2]);
            Assert.Contains(debugMessage, logLines[3]);
        }
    }
}
