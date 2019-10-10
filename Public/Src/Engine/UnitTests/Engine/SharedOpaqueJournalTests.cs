// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    public sealed class SharedOpaqueJournalTests : TemporaryStorageTestBase
    {
        private static int s_journalDirectoryCounter = 0;

        private PipExecutionContext Context { get; }

        private PathTable PathTable => Context.PathTable;

        public SharedOpaqueJournalTests(ITestOutputHelper output)
            : base(output)
        {
            Context = BuildXLContext.CreateInstanceForTesting();
        }

        [Fact]
        public void FindAllJournalFilesHandlesAbsentFolder()
        {
            var dir = Path.Combine(TemporaryDirectory, "absent-qwre");
            XAssert.IsFalse(Directory.Exists(dir));
            var result = SharedOpaqueJournal.FindAllJournalFiles(dir);
            XAssert.ArrayEqual(new string[0], result.ToArray());
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

            var journalPath = Path.Combine(myDir, "Pip0");
            CreateJournalAndRecordPaths(journalPath, paths);

            var readPaths = SharedOpaqueJournal.ReadRecordedWritesFromJournal(journalPath).ToArray();
            XAssert.ArrayEqual(paths, readPaths);
        }

        [Theory]
        [InlineData(20, 20)]
        public void ConcurrentWritesToJournalsInTheSameFolder(int numJournals, int numPathsPerJournal)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(ConcurrentWritesToJournalsInTheSameFolder));

            string[][] pathsPerJournal = Enumerable
                .Range(0, numJournals)
                .Select(journalIdx => Enumerable
                    .Range(0, numPathsPerJournal)
                    .Select(pathIdx => Path.Combine(TemporaryDirectory, $"path-{journalIdx}-{pathIdx}"))
                    .ToArray())
                .ToArray();

            string[] journalFiles = Enumerable
                .Range(0, numJournals)
                .Select(journalIdx => Path.Combine(myDir, $"Pip{journalIdx}"))
                .ToArray();

            Enumerable
                .Range(0, numJournals)
                .ToArray()
                .AsParallel()
                .ForAll(journalIdx => CreateJournalAndRecordPaths(journalFiles[journalIdx], pathsPerJournal[journalIdx]));

            for (int i = 0; i < numJournals; i++)
            {
                XAssert.ArrayEqual(pathsPerJournal[i], SharedOpaqueJournal.ReadRecordedWritesFromJournal(journalFiles[i]).ToArray());
            }
        }

        [Fact]
        public void ReadingFromAbsentJournalThrowsFileNotFound()
        {
            var absentFile= "absent-file";
            XAssert.IsFalse(File.Exists(absentFile));
            Assert.Throws<FileNotFoundException>(() => SharedOpaqueJournal.ReadRecordedWritesFromJournal(absentFile).ToArray());
        }

        [Fact]
        public void TestReadWrapExceptions()
        {
            var absentFile = "absent-file2";
            XAssert.IsFalse(File.Exists(absentFile));
            Assert.Throws<BuildXLException>(() => SharedOpaqueJournal.ReadRecordedWritesFromJournalWrapExceptions(absentFile));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public void ReadingFromBogusJournalShouldNotThrow(int numValidRecordsToWriteFirst)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(ReadingFromBogusJournalShouldNotThrow));
            Directory.CreateDirectory(myDir);
            XAssert.IsTrue(Directory.Exists(myDir));

            // write some valid records first
            var validRecords = Enumerable
                .Range(0, numValidRecordsToWriteFirst)
                .Select(i => Path.Combine(TemporaryDirectory, $"path-{i}"))
                .ToArray();
            var journalFile = Path.Combine(myDir, "bogus-journal");
            CreateJournalAndRecordPaths(journalFile, validRecords);

            // append some bogus stuff at the end
            using (var s = new FileStream(journalFile, FileMode.Open))
            using (var bw = new BinaryWriter(s))
            {
                bw.Seek(0, SeekOrigin.End);
                bw.Write(1231);
                bw.Flush();
            }

            // reading should produce valid records and just finish when it encounters the bogus stuff
            var read = SharedOpaqueJournal.ReadRecordedWritesFromJournal(journalFile).ToArray();
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
        public void TestRootDirectoryFiltering(string rootDirsStr, string pathToTest, bool expectedToBeRecorded)
        {
            var myDir = Path.Combine(TemporaryDirectory, nameof(TestRootDirectoryFiltering));
            Directory.CreateDirectory(myDir);
            XAssert.IsTrue(Directory.Exists(myDir));

            var rootDirs = rootDirsStr
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => AbsolutePath.Create(PathTable, X(p)));
            pathToTest = X(pathToTest);

            var journalPath = Path.Combine(myDir, "Pip0");
            using (var journal = CreateJournal(journalPath, rootDirs))
            {
                bool recorded = journal.RecordFileWrite(AbsolutePath.Create(PathTable, pathToTest));
                XAssert.AreEqual(expectedToBeRecorded, recorded);
            }

            XAssert.ArrayEqual(
                expectedToBeRecorded ? new[] { pathToTest } : new string[0],
                SharedOpaqueJournal.ReadRecordedWritesFromJournal(journalPath).ToArray());
        }

        private void CreateJournalAndRecordPaths(string journalPath, IEnumerable<string> pathsToRecord, IEnumerable<AbsolutePath> rootDirs = null)
        {
            using (var journal = CreateJournal(journalPath, rootDirs))
            {
                XAssert.AreEqual(journalPath, journal.JournalPath);

                foreach (var path in pathsToRecord)
                {
                    journal.RecordFileWrite(AbsolutePath.Create(PathTable, path));
                }
            }
        }

        private SharedOpaqueJournal CreateJournal(string journalPath = null, IEnumerable<AbsolutePath> rootDirs = null)
        {
            journalPath = journalPath ?? Path.Combine(TemporaryDirectory, $"{s_journalDirectoryCounter++}", "journal");
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath));
            return new SharedOpaqueJournal(
                PathTable,
                rootDirectories: rootDirs ?? new[] { AbsolutePath.Create(PathTable, TemporaryDirectory) },
                journalPath: AbsolutePath.Create(PathTable, journalPath)); 
        }
    }
}
