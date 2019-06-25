// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Storage
{
    public class FileCombinerTests : TemporaryStorageTestBase
    {
        private LoggingContext m_loggingContext = new LoggingContext("UnitTest");
        private string m_path;

        public FileCombinerTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Storage.ETWLogger.Log);
            m_path = Path.Combine(TemporaryDirectory, "CombinedFile.bin");
            File.Delete(m_path);
        }

        private FileCombiner CreateFileCombiner(LoggingContext loggingContext, string path)
        {
            return new FileCombiner(m_loggingContext, m_path, FileCombiner.FileCombinerUsage.SpecFileCache, logFileCombinerStatistics: true);
        }

        private FileCombiner CreateFileCombiner(LoggingContext loggingContext, string path, double allowableUnreferencedRatio)
        {
            return new FileCombiner(m_loggingContext, m_path, FileCombiner.FileCombinerUsage.SpecFileCache, logFileCombinerStatistics: true, allowableUnreferencedRatio);
        }

        private FileCombiner CreateFileCombiner(LoggingContext loggingContext, string path, double allowableUnreferencedRatio, int maxBackingBufferBytes)
        {
            return new FileCombiner(m_loggingContext, m_path, FileCombiner.FileCombinerUsage.SpecFileCache, allowableUnreferencedRatio, maxBackingBufferBytes);
        }

        [Fact]
        public void CreateAndReloadCombinedFile()
        {
            int maxBackingBufferBytes = 3; // let's make sure we are going to span multiple chunks...

            FakeFile f1 = CreateFakeFile(R("foo","bar1.txt"), "bar1.txt");
            FakeFile f2 = CreateFakeFile(R("foo", "bar2.txt"), "bar2.txt");
            FakeFile f3 = CreateFakeFile(R("foo", "bar3.txt"), "bar3.txt");

            XAssert.IsTrue(f1.Content.Length > maxBackingBufferBytes * 3);
            XAssert.IsTrue(f2.Content.Length > maxBackingBufferBytes * 3);
            XAssert.IsTrue(f3.Content.Length > maxBackingBufferBytes * 3);

            Logger.FileCombinerStats stats = new Logger.FileCombinerStats();

            // Create a combiner and add some files
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
                combiner.GetStatsRefForTest(ref stats);
                AddFile(combiner, f3);
                AddFile(combiner, f2);
                AddFile(combiner, f1);
            }

            XAssert.AreEqual(3, stats.EndCount);
            XAssert.AreEqual(0, stats.CompactingTimeMs, "FileCombiner should not have been compacted");

            // Make sure the file is longer than the max backing buffer so we can test data being split across buffers
            FileInfo info = new FileInfo(m_path);
            XAssert.IsTrue(info.Length > maxBackingBufferBytes);

            // reload the combiner
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1, maxBackingBufferBytes))
            {
                // fetch a file that exists and verify the correct data is returned
                using (MemoryStream ms = combiner.RequestFile(f1.Path, f1.Hash))
                {
                    XAssert.IsNotNull(ms);
                    AssertContentMatches(ms, f1);
                }

                // Fetch a file with the wrong hash. Make sure no content is returned
                using (MemoryStream ms = combiner.RequestFile(f1.Path, f2.Hash))
                {
                    XAssert.IsNull(ms);
                }

                // Fetch a file that doesn't exist. Make sure no content is returned
                using (MemoryStream ms = combiner.RequestFile(R("foo","bar4"), f2.Hash))
                {
                    XAssert.IsNull(ms);
                }
            }
        }

        [Fact]
        public void CreateAndReloadCombinedFileAlignmentBug()
        {
            FakeFile file = default(FakeFile);
            file.Path = R("foo","bar1.txt");
            using (var contentStream = new MemoryStream())
            {
                using (var binaryWriter = new BuildXLWriter(debug: false, stream: contentStream, leaveOpen: true, logStats: false))
                {
                    binaryWriter.WriteCompact(-1);
                }

                file.Content = contentStream.ToArray();
                file.Hash = ContentHashingUtilities.HashBytes(file.Content);
            }

            int maxBackingBufferBytes = 4 + 6 + 8 + file.Content.Length; // magic size that used to trigger a bug

            Logger.FileCombinerStats stats = new Logger.FileCombinerStats();

            // Create a combiner and add some files
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
                combiner.GetStatsRefForTest(ref stats);
                AddFile(combiner, file);
            }

            XAssert.AreEqual(1, stats.EndCount);
            XAssert.AreEqual(0, stats.CompactingTimeMs, "FileCombiner should not have been compacted");

            // reload the combiner
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1, maxBackingBufferBytes))
            {
                // fetch a file that exists and verify the correct data is returned
                using (MemoryStream ms = combiner.RequestFile(file.Path, file.Hash))
                {
                    XAssert.IsNotNull(ms);
                    AssertContentMatches(ms, file);
                }
            }
        }

        [Fact]
        public void FileGetsUpdated()
        {
            FakeFile initial = CreateFakeFile(R("foo", "bar1.txt"), "BeginningContent");

            // Create a combiner and add some files
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
                AddFile(combiner, initial);
            }

            FakeFile updated = CreateFakeFile(R("foo", "bar1.txt"), "UpdatedContent");
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
                AddFile(combiner, updated);
            }

            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
                using (MemoryStream ms = combiner.RequestFile(initial.Path, initial.Hash))
                {
                    XAssert.IsNull(ms);
                }

                using (MemoryStream ms = combiner.RequestFile(updated.Path, updated.Hash))
                {
                    XAssert.IsNotNull(ms);
                    AssertContentMatches(ms, updated);
                }
            }
        }

        [Fact]
        public void CorruptFile()
        {
            // Write out some garbage to create a corrupt file
            File.WriteAllText(m_path, "1Cleanasldkjf09234,kns90j23lk4n2309u4");

            FakeFile f1 = CreateFakeFile(R("foo", "bar1.txt"), "bar1.txt");

            // Try to use it
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path))
            {
                XAssert.IsNull(combiner.RequestFile(f1.Path, f1.Hash));
                AddFile(combiner, f1);
            }

            // Reload and consume the added file
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path))
            {
                using (MemoryStream ms = combiner.RequestFile(f1.Path, f1.Hash))
                {
                    XAssert.IsNotNull(ms);
                }
            }

            AssertWarningEventLogged(EventId.FileCombinerVersionIncremented);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // need to investigate if equivalent behavior on Unix
        public void FileInUse()
        {
            // Open the backing file so the FileCombiner can't open it
            using (var file = File.Open(m_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                FakeFile f1 = CreateFakeFile(R("foo", "bar1.txt"), "bar1.txt");

                // Try to use it
                using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path))
                {
                    XAssert.IsNull(combiner.RequestFile(f1.Path, f1.Hash));
                    AddFile(combiner, f1);
                }

                AssertWarningEventLogged(LogEventId.FileCombinerFailedToInitialize);
                AssertWarningEventLogged(LogEventId.FileCombinerFailedToCreate);
            }
        }

        [Fact]
        public void CompactFile()
        {
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
                for (int i = 0; i < 10; i++)
                {
                    FakeFile f = CreateFakeFile(R("foo", "bar") + i, i.ToString());
                    AddFile(combiner, f);
                }
            }

            FileInfo fileInfo = new FileInfo(m_path);
            long initialLength = fileInfo.Length;

            // File shouldn't shrink
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, .9))
            {
                for (int i = 0; i < 2; i++)
                {
                    FakeFile f = CreateFakeFile(R("foo", "bar") + i, i.ToString());
                    combiner.RequestFile(f.Path, f.Hash);
                }
            }

            fileInfo = new FileInfo(m_path);
            XAssert.AreEqual(initialLength, fileInfo.Length);

            // File shouldn't shrink since no new content was added. Delay the shrink for a future run.
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, .2))
            {
                combiner.RequestFile(R("foo", "bar1"), null);
                combiner.RequestFile(R("foo", "bar8"), null);
            }

            fileInfo = new FileInfo(m_path);
            XAssert.AreEqual(initialLength, fileInfo.Length);

            // File should shrink
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, .2))
            {
                combiner.RequestFile(R("foo", "bar1"), null);
                combiner.RequestFile(R("foo", "bar8"), null);
                FakeFile f = CreateFakeFile(R("foo", "bar10"), "10");
                AddFile(combiner, f);
            }

            fileInfo = new FileInfo(m_path);
            XAssert.IsTrue(initialLength > fileInfo.Length);

            // Request files from before, inbetween, and after the ones that got removed
            using (FileCombiner combiner = CreateFileCombiner(m_loggingContext, m_path, .2))
            {
                AssertContentMatches(combiner.RequestFile(R("foo", "bar1"), null), "1");
                AssertContentMatches(combiner.RequestFile(R("foo", "bar8"), null), "8");
                AssertContentMatches(combiner.RequestFile(R("foo", "bar10"), null), "10");
            }
        }

        [Fact]
        public void EmptyFileCombiner()
        {
            // Creating and disposing an empty file combiner
            using (var combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
            }

            // Loading the same file combiner again, to make sure an empty file combiner is loadable
            using (var combiner = CreateFileCombiner(m_loggingContext, m_path, 1))
            {
            }
        }

        private FakeFile CreateFakeFile(string path, string content)
        {
            FakeFile file = default(FakeFile);
            file.Path = path;

            // Make the content have a reasonable size so we can test getting split into multiple buffers when the backing
            // file is read into memory
            file.Content = Encoding.UTF8.GetBytes(Padding + content);
            file.Hash = ContentHashingUtilities.HashBytes(file.Content);

            return file;
        }

        private const string Padding = "123456789012345678901234567890123456789012345678901234567890";

        private void AddFile(FileCombiner combiner, FakeFile fakeFile)
        {
            combiner.AddFile(fakeFile.Content, fakeFile.Hash, fakeFile.Path);
        }

        private struct FakeFile
        {
            public ContentHash Hash;
            public byte[] Content;
            public string Path;
        }

        private void AssertContentMatches(MemoryStream ms, FakeFile info)
        {
            using (StreamReader reader = new StreamReader(ms))
            {
                string read = reader.ReadToEnd();
                string original = Encoding.UTF8.GetString(info.Content);

                XAssert.AreEqual(original, read);
            }
        }

        private void AssertContentMatches(MemoryStream ms, string contentAfterPadding)
        {
            using (StreamReader reader = new StreamReader(ms))
            {
                string read = reader.ReadToEnd();
                XAssert.AreEqual(Padding + contentAfterPadding, read);
            }
        }
    }
}
