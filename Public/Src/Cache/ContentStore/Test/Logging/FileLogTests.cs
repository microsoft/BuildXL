// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Logging
{
    public class FileLogTests : TestBase
    {
        public FileLogTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), NullLogger.Instance)
        {
        }

        [Fact]
        public void FileCreatedUnderTemp()
        {
            using (var log = new FileLog(Path.GetTempPath(), GetRandomFileName()))
            {
                File.Exists(log.FilePath).Should().BeTrue();
                log.FilePath.Should().StartWith(Path.GetTempPath());
            }
        }

        [Fact]
        public void NamedFileConstructor()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var filePath = testDirectory.Path / "test.log";
                using (new FileLog(filePath.Path))
                {
                    FileSystem.FileExists(filePath).Should().BeTrue();
                }
            }
        }

        [Fact]
        public void AlwaysMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Always);
            line.Should().Contain("ALWAY");
        }

        [Fact]
        public void FatalMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Fatal);
            line.Should().Contain("FATAL");
        }

        [Fact]
        public void ErrorMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Error);
            line.Should().Contain("ERROR");
        }

        [Fact]
        public void WarningMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Warning);
            line.Should().Contain("WARN");
        }

        [Fact]
        public void InfoMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Info);
            line.Should().Contain("INFO");
        }

        [Fact]
        public void DebugMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Debug);
            line.Should().Contain("DEBUG");
        }

        [Fact]
        public void DiagnosticMethodSucceeds()
        {
            var line = WriteAndVerify(Severity.Diagnostic);
            line.Should().Contain("DIAG");
        }

        [Fact]
        public void FilteredSeverityIgnored()
        {
            var line = WriteAndVerify(Severity.Unknown, true);
            line.Should().Be(string.Empty);
        }

        private static string WriteAndVerify(Severity logSeverity, bool expectEmpty = false)
        {
            const string message = "message";
            string logFilePath = null;

            try
            {
                using (var log = new FileLog(Path.GetTempPath(), GetRandomFileName()))
                {
                    log.Write(DateTime.Now, Thread.CurrentThread.ManagedThreadId, logSeverity, message);
                    logFilePath = log.FilePath;
                }

                var lines = File.ReadLines(logFilePath).ToList();
                if (!expectEmpty)
                {
                    Debug.Print(string.Join(" - ", lines));
                    lines.Count.Should().Be(1);
                    lines[0].Should().Contain(message);
                    return lines[0];
                }

                return string.Empty;
            }
            finally
            {
                if (logFilePath != null)
                {
                    File.Delete(logFilePath);
                }
            }
        }

        [Fact]
        public void RollingWithMaxFileCount()
        {
            foreach (var maxFileCount in new[] {0, 10})
            {
                using (var fileSystem = new PassThroughFileSystem(Logger))
                using (var uniqueTempDirectory = new DisposableDirectory(fileSystem))
                {
                    var message = new string('h', 1000);

                    using (var log = new FileLog(
                        uniqueTempDirectory.Path.Path,
                        "test",
                        maxFileSize: 1000,
                        maxFileCount: maxFileCount,
                        dateInFileName: false,
                        processIdInFileName: false))
                    {
                        for (var i = 0; i < 12; i++)
                        {
                            log.Write(DateTime.Now, Thread.CurrentThread.ManagedThreadId, Severity.Always, message);
                        }
                    }

                    var actualFileNames = fileSystem.EnumerateFiles(uniqueTempDirectory.Path, EnumerateOptions.None)
                        .Select(fileInfo => fileInfo.FullPath.FileName).ToHashSet();

                    if (maxFileCount == 0)
                    {
                        actualFileNames.Count.Should().Be(12);
                        actualFileNames.Contains("test-0.log").Should().BeTrue();
                        actualFileNames.Contains("test-1.log").Should().BeTrue();
                        actualFileNames.Contains("test-2.log").Should().BeTrue();
                        actualFileNames.Contains("test-3.log").Should().BeTrue();
                        actualFileNames.Contains("test-4.log").Should().BeTrue();
                        actualFileNames.Contains("test-5.log").Should().BeTrue();
                        actualFileNames.Contains("test-6.log").Should().BeTrue();
                        actualFileNames.Contains("test-7.log").Should().BeTrue();
                        actualFileNames.Contains("test-8.log").Should().BeTrue();
                        actualFileNames.Contains("test-9.log").Should().BeTrue();
                        actualFileNames.Contains("test-10.log").Should().BeTrue();
                        actualFileNames.Contains("test-11.log").Should().BeTrue();
                    }
                    else
                    {
                        actualFileNames.Count.Should().Be(10);
                        actualFileNames.Contains("test-02.log").Should().BeTrue();
                        actualFileNames.Contains("test-03.log").Should().BeTrue();
                        actualFileNames.Contains("test-04.log").Should().BeTrue();
                        actualFileNames.Contains("test-05.log").Should().BeTrue();
                        actualFileNames.Contains("test-06.log").Should().BeTrue();
                        actualFileNames.Contains("test-07.log").Should().BeTrue();
                        actualFileNames.Contains("test-08.log").Should().BeTrue();
                        actualFileNames.Contains("test-09.log").Should().BeTrue();
                        actualFileNames.Contains("test-10.log").Should().BeTrue();
                        actualFileNames.Contains("test-11.log").Should().BeTrue();
                    }
                }
            }
        }
    }
}
