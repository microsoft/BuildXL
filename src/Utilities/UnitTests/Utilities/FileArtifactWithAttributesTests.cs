// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Set of unit tests for the <see cref="FileArtifactWithAttributes"/> class.
    /// </summary>
    public class FileArtifactWithAttributesTests : XunitBuildXLTest
    {
        public FileArtifactWithAttributesTests(ITestOutputHelper output)
            : base(output) { }

        [Theory]
        [InlineData(FileExistence.Optional)]
        [InlineData(FileExistence.Required)]
        [InlineData(FileExistence.Temporary)]
        public void FileExistenceStaysTheSameWithCallToCreateNextWrittenVersion(FileExistence fileExistence)
        {
            var pathTable = new PathTable();
            AbsolutePath filePath = AbsolutePath.Create(pathTable, A("t","file1.txt"));
            var artifact = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(filePath), fileExistence);
            var anotherArtifact = artifact.CreateNextWrittenVersion();

            XAssert.AreEqual(artifact.FileExistence, anotherArtifact.FileExistence,
                "FileExistence should be the same after call to CreateNextWrittenVersion");
        }

        [Fact]
        public void TestTemporaryAndRequiredOutputArtifacts()
        {
            var pathTable = new PathTable();
            AbsolutePath filePath = AbsolutePath.Create(pathTable, A("t","file1.txt"));

            var temporaryArtifact = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(filePath), FileExistence.Temporary).CreateNextWrittenVersion();
            var requiredArtifact = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(filePath), FileExistence.Required).CreateNextWrittenVersion();
            var optionalArtifact = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(filePath), FileExistence.Optional).CreateNextWrittenVersion();

            XAssert.IsTrue(temporaryArtifact.IsOutputFile, "Instance should be output");
            XAssert.IsTrue(requiredArtifact.IsOutputFile, "Instance should be output");
            XAssert.IsTrue(optionalArtifact.IsOutputFile, "Instance should be output");

            XAssert.IsTrue(temporaryArtifact.IsTemporaryOutputFile, "Temporary artifact should be temporary");
            XAssert.IsFalse(requiredArtifact.IsTemporaryOutputFile, "Required artifact is not temporary");
            XAssert.IsFalse(optionalArtifact.IsTemporaryOutputFile, "Optional artifact is not temporary");

            XAssert.IsFalse(temporaryArtifact.IsRequiredOutputFile, "Temporary artifact is not required");
            XAssert.IsTrue(requiredArtifact.IsRequiredOutputFile, "Required artifact is required");
            XAssert.IsFalse(optionalArtifact.IsRequiredOutputFile, "Optional artifact is not required");
        }

        [Fact]
        public void FileArtifactEquality()
        {
            var pathTable = new PathTable();
            FileArtifact file1 = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, A("t","file1.txt")));
            FileArtifact file2 = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, A("t","file2.txt")));

            StructTester.TestEquality(
                baseValue: FileArtifactWithAttributes.Create(file1, FileExistence.Required),
                equalValue: FileArtifactWithAttributes.Create(file1, FileExistence.Required),
                notEqualValues: new[]
                                {
                                    FileArtifactWithAttributes.Create(file1, FileExistence.Optional),
                                    FileArtifactWithAttributes.Create(file1, FileExistence.Optional).CreateNextWrittenVersion(),
                                    FileArtifactWithAttributes.Create(file2, FileExistence.Temporary),
                                    FileArtifactWithAttributes.Create(file2, FileExistence.Temporary).CreateNextWrittenVersion(),
                                    FileArtifactWithAttributes.Create(file1, FileExistence.Required).CreateNextWrittenVersion(),
                                    FileArtifactWithAttributes.Create(file1, FileExistence.Required).CreateNextWrittenVersion().CreateNextWrittenVersion(),
                                    FileArtifactWithAttributes.Create(file1.CreateNextWrittenVersion(), FileExistence.Required)
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right,
                skipHashCodeForNotEqualValues: true);
        }

        [Theory]
        [InlineData(FileExistence.Temporary)]
        [InlineData(FileExistence.Required)]
        [InlineData(FileExistence.Optional)]
        public void TestSerialization(FileExistence fileExistence)
        {
            var pathTable = new PathTable();
            var fileArtifact = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, 
                A("c","foo.txt"))), fileExistence);

            HasTheSamePathAndExistence(fileArtifact, CloneViaSerialization(fileArtifact));

            // Write count is not affected by serialization/deserialization
            HasTheSamePathAndExistence(fileArtifact, CloneViaSerialization(fileArtifact.CreateNextWrittenVersion()));
        }

        private FileArtifactWithAttributes CloneViaSerialization(FileArtifactWithAttributes fileArtifact)
        {
            using (var memoryStream = new MemoryStream())
            {
                var writer = new BuildXLWriter(false, memoryStream, true, false);
                fileArtifact.Serialize(writer);

                memoryStream.Position = 0;

                var reader = new BuildXLReader(false, memoryStream, false);
                return FileArtifactWithAttributes.Deserialize(reader);
            }
        }

        private static void HasTheSamePathAndExistence(FileArtifactWithAttributes left, FileArtifactWithAttributes right)
        {
            XAssert.AreEqual(left.Path, right.Path);
            XAssert.AreEqual(left.FileExistence, right.FileExistence);
        }
    }
}
