// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class BasicFileOperationTests
    {
        public ITestOutputHelper TestOutput { get; }

        public BasicFileOperationTests(ITestOutputHelper testOutput)
        {
            TestOutput = testOutput;
        }

        /// <summary>
        /// This test is a smoke test for the journal code
        /// </summary>
        [Fact]
        public void CreateFile()
        {
            using var helper = new JournalHelper(TestOutput);

            var filePath = helper.GetPath("a.txt");

            // Track
            helper.TrackPath(filePath);

            // Do
            File.WriteAllText(filePath, "hi");

            // Check
            helper.SnapCheckPoint();
            helper.AssertCreateFile(filePath);
        }

        [Fact]
        public void EditFile()
        {
            using var helper = new JournalHelper(TestOutput);

            var filePath = helper.GetPath("a.txt");
            File.WriteAllText(filePath, "hi-old"); 

            // Track
            helper.TrackPath(filePath);

            // Do
            File.WriteAllText(filePath, "hi");

            // Check
            helper.SnapCheckPoint();
            helper.AssertChangeFile(filePath);
        }

        [Fact]
        public void DeleteFile()
        {
            using var helper = new JournalHelper(TestOutput);

            var filePath = helper.GetPath("a.txt");
            File.WriteAllText(filePath, "hi-old"); 

            // Track
            helper.TrackPath(filePath);

            // Do
            File.Delete(filePath);

            // Check
            helper.SnapCheckPoint();
            helper.AssertDeleteFile(filePath);
        }
    }
}