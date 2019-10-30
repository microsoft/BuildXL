// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

using static BuildXL.Processes.SidebandWriter;

namespace Test.BuildXL.Engine
{
    public sealed class SidebandTests : TemporaryStorageTestBase
    {
        private static int s_logDirectoryCounter = 0;

        private PipExecutionContext Context { get; }

        private PathTable PathTable => Context.PathTable;

        public SidebandTests(ITestOutputHelper output)
            : base(output)
        {
            Context = BuildXLContext.CreateInstanceForTesting();
        }

        [Fact]
        public void FindAllSidebandFilesHandlesAbsentFolder()
        {
            var dir = Path.Combine(TemporaryDirectory, "absent-qwre");
            XAssert.IsFalse(Directory.Exists(dir));
            var result = FindAllProcessPipSidebandFiles(dir);
            XAssert.ArrayEqual(new string[0], result.ToArray());
        }

        [Fact]
        public void RecordingDeduplicatesPaths()
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(RecordingDeduplicatesPaths));

            var path = Path.Combine(TemporaryDirectory, "path1");
            var logFile = Path.Combine(myDir, "Pip0");
            
            CreateOutputLoggerAndRecordPaths(logFile, new[] { path, path, path });

            XAssert.ArrayEqual(
                new[] { path },
                ReadRecordedPathsFromSidebandFile(logFile).ToArray());
        }

        [Theory]
        [InlineData("file1", null)]
        [InlineData("file1", new[] { "dir1" })]
        [InlineData("file1", new[] { "dir1", "dir2" })]
        public void DeserializeIsInverseOfSerialize(string logFile, string[] rootDirs)
        {
            using (var original = new SidebandWriter(DefaultSidebandMetadata, logFile, rootDirs))
            using (var clone = CloneViaSerialization(original))
            {
                XAssert.AreEqual(original.SidebandLogFile, clone.SidebandLogFile);
                XAssert.ArrayEqual(original.RootDirectories?.ToArray(), clone.RootDirectories?.ToArray());
            }
        }

        [Theory]
        [InlineData(20)]
        public void ReadIsInverseOfWrite(int numFiles)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(ReadIsInverseOfWrite));

            var paths = Enumerable
                .Range(0, numFiles)
                .Select(i => Path.Combine(TemporaryDirectory, $"path-{i}"))
                .ToArray();

            var logFile = Path.Combine(myDir, "Pip0");
            CreateOutputLoggerAndRecordPaths(logFile, paths);

            var readPaths = ReadRecordedPathsFromSidebandFile(logFile).ToArray();
            XAssert.ArrayEqual(paths, readPaths);
        }

        [Theory]
        [InlineData(20, 20)]
        public void ConcurrentWritesToSidebandFilesInTheSameFolder(int numLogs, int numPathsPerLog)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(ConcurrentWritesToSidebandFilesInTheSameFolder));

            string[][] pathsPerLog = Enumerable
                .Range(0, numLogs)
                .Select(logIdx => Enumerable
                    .Range(0, numPathsPerLog)
                    .Select(pathIdx => Path.Combine(TemporaryDirectory, $"path-{logIdx}-{pathIdx}"))
                    .ToArray())
                .ToArray();

            string[] logFiles = Enumerable
                .Range(0, numLogs)
                .Select(logIdx => Path.Combine(myDir, $"Pip{logIdx}"))
                .ToArray();

            Enumerable
                .Range(0, numLogs)
                .ToArray()
                .AsParallel()
                .ForAll(logIdx => CreateOutputLoggerAndRecordPaths(logFiles[logIdx], pathsPerLog[logIdx]));

            for (int i = 0; i < numLogs; i++)
            {
                XAssert.ArrayEqual(pathsPerLog[i], ReadRecordedPathsFromSidebandFile(logFiles[i]).ToArray());
            }
        }

        [Fact]
        public void ReadingFromAbsentSidebandFileReturnsEmptyIterator()
        {
            var absentFile = "absent-file";
            XAssert.IsFalse(File.Exists(absentFile));
            XAssert.ArrayEqual(new string[0], ReadRecordedPathsFromSidebandFile(absentFile).ToArray());
        }

        [Theory]
        [MemberData(nameof(CrossProduct), 
            new object[] { 0, 1, 10 }, 
            new object[] { true, false }, 
            new object[] { true, false })]
        public void ReadingFromCorruptedSidebandFiles(
            int numValidRecordsToWriteFirst, 
            bool closeLoggerCleanly, 
            bool appendBogusBytes)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(ReadingFromCorruptedSidebandFiles));
            Directory.CreateDirectory(myDir);
            XAssert.IsTrue(Directory.Exists(myDir));

            // write some valid records first
            var validRecords = Enumerable
                .Range(0, numValidRecordsToWriteFirst)
                .Select(i => Path.Combine(TemporaryDirectory, $"path-{i}"))
                .ToArray();

            var logFile = Path.Combine(myDir, $"bogus-log-close_{closeLoggerCleanly}-append_{appendBogusBytes}");
            var sidebandWriter = CreateWriter(logFile);
            sidebandWriter.EnsureHeaderWritten();
            foreach (var path in validRecords)
            {
                XAssert.IsTrue(sidebandWriter.RecordFileWrite(PathTable, path));
            }

            if (closeLoggerCleanly)
            {
                sidebandWriter.Dispose();
            }
            else
            {
                sidebandWriter.CloseWriterWithoutFixingUpHeaderForTestingOnly();
            }

            if (appendBogusBytes)
            {
                // append some bogus stuff at the end
                using (var s = new FileStream(logFile, FileMode.OpenOrCreate))
                using (var bw = new BinaryWriter(s))
                {
                    bw.Seek(0, SeekOrigin.End);
                    bw.Write(1231);
                    bw.Flush();
                }
            }

            // reading should produce valid records and just finish when it encounters the bogus stuff
            var read = SidebandWriter.ReadRecordedPathsFromSidebandFile(logFile).ToArray();
            XAssert.ArrayEqual(validRecords, read);
        }

        [Theory]
        [InlineData("/x/root1;/x/root2", "/x/root1", true)]
        [InlineData("/x/root1;/x/root2", "/x/root2", true)]
        [InlineData("/x/root1;/x/root2", "/x/root1/a", true)]
        [InlineData("/x/root1;/x/root2", "/x/root1/a/b", true)]
        [InlineData("/x/root1;/x/root2", "/x/root2/a", true)]
        [InlineData("/x/root1;/x/root2", "/x/root2/a/b/c", true)]
        [InlineData("/x/root1;/x/root2", "/x/root2/a b c", true)]
        [InlineData("/x/root1;/x/root2", "/x/out/a", false)]
        [InlineData("/x/root1;/x/root2", "/x/a", false)]
        [InlineData("", "/x/a", false)]
        [InlineData(null, "/x/a", true)]
        public void TestRootDirectoryFiltering(string rootDirsStr, string pathToTest, bool expectedToBeRecorded)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(TestRootDirectoryFiltering));
            Directory.CreateDirectory(myDir);
            XAssert.IsTrue(Directory.Exists(myDir));

            var rootDirs = rootDirsStr == null ? null : rootDirsStr
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => X(p))
                .ToList();
            pathToTest = X(pathToTest);

            var logPath = Path.Combine(myDir, "Pip0");
            using (var logger = CreateWriter(logPath, rootDirs))
            {
                bool recorded = logger.RecordFileWrite(PathTable, pathToTest);
                XAssert.AreEqual(expectedToBeRecorded, recorded);
            }

            XAssert.ArrayEqual(
                expectedToBeRecorded ? new[] { pathToTest } : new string[0],
                ReadRecordedPathsFromSidebandFile(logPath).ToArray());
        }

        private void CreateOutputLoggerAndRecordPaths(string logPath, IEnumerable<string> pathsToRecord, IReadOnlyList<string> rootDirs = null)
        {
            using (var logger = CreateWriter(logPath, rootDirs))
            {
                XAssert.AreEqual(logPath, logger.SidebandLogFile);

                foreach (var path in pathsToRecord)
                {
                    logger.RecordFileWrite(PathTable, path);
                }
            }
        }

        private SidebandWriter CreateWriter(string logPath = null, IReadOnlyList<string> rootDirs = null, SidebandMetadata metadata = null)
        {
            logPath = logPath ?? Path.Combine(TemporaryDirectory, $"{s_logDirectoryCounter++}", "log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            return new SidebandWriter(
                metadata: metadata ?? DefaultSidebandMetadata,
                sidebandLogFile: logPath,
                rootDirectories: rootDirs);
        }

        private static SidebandWriter CloneViaSerialization(SidebandWriter original)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(debug: true, stream, leaveOpen: true, logStats: true))
                {
                    original.Serialize(writer);
                }

                stream.Seek(0, SeekOrigin.Begin);
                using (var reader = new BuildXLReader(debug: true, stream, leaveOpen: true))
                {
                    return SidebandWriter.Deserialize(reader);
                }
            }
        }
    }
}
