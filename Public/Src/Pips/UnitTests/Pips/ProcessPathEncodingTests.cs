// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Pips
{
    public sealed class ProcessPathEncodingTests : XunitBuildXLTest
    {
        public ProcessPathEncodingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeltaEncodedProcessCollectionsRoundTrip(bool debug)
        {
            var pathTable = new PathTable();
            var paths = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(
                new[]
                {
                    new AbsolutePath(1),
                    new AbsolutePath(2),
                    new AbsolutePath(129),
                    new AbsolutePath(128),
                    new AbsolutePath(int.MaxValue),
                    new AbsolutePath(1),
                });
            var tablePaths = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(
                new[]
                {
                    AbsolutePath.Create(pathTable, A("c", "deltaEncoding", "first")),
                    AbsolutePath.Create(pathTable, A("c", "deltaEncoding", "second")),
                    AbsolutePath.Create(pathTable, A("c", "deltaEncoding", "third")),
                });
            var files = ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                new[]
                {
                    new FileArtifact(paths[0], 0),
                    new FileArtifact(paths[2], 1),
                    new FileArtifact(paths[1], 17),
                });
            var filesWithAttributes = ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                new[]
                {
                    new FileArtifact(paths[1], 1).WithAttributes(FileExistence.Required),
                    new FileArtifact(paths[3], 7).WithAttributes(FileExistence.Optional, undeclaredSourceRewrite: true),
                    new FileArtifact(paths[4], FileArtifactWithAttributes.MaxRewriteCount).WithAttributes(FileExistence.Temporary),
                    new FileArtifact(paths[5], 1).WithAttributes((FileExistence)FileArtifactWithAttributes.MaxFileExistence),
                });
            var directories = ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(
                new[]
                {
                    new DirectoryArtifact(paths[2], partialSealId: 0, isSharedOpaque: false),
                    new DirectoryArtifact(paths[0], partialSealId: 42, isSharedOpaque: true),
                });
            var sortedFiles = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(files, OrdinalFileArtifactComparer.Instance);
            var sortedDirectories = SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>.CloneAndSort(directories, OrdinalDirectoryArtifactComparer.Instance);

            using (var stream = new MemoryStream())
            {
                using (var writer = new PipWriter(debug, stream, leaveOpen: true, logStats: true))
                {
                    writer.WriteDeltaEncodedAbsolutePathArray(ReadOnlyArray<AbsolutePath>.Empty);
                    writer.WriteDeltaEncodedAbsolutePathArray(paths);
                    writer.WriteDeltaEncodedAbsolutePathArray(tablePaths);
                    writer.WriteDeltaEncodedFileArtifactArray(files);
                    writer.WriteDeltaEncodedFileArtifactWithAttributesArray(filesWithAttributes);
                    writer.WriteDeltaEncodedDirectoryArtifactArray(directories);
                    writer.WriteDeltaEncodedFileArtifactArray(sortedFiles);
                    writer.WriteDeltaEncodedDirectoryArtifactArray(sortedDirectories);
                }

                stream.Position = 0;
                using (var reader = new PipReader(debug, new StringTable(), stream, leaveOpen: true))
                {
                    Assert.Equal(0, reader.ReadDeltaEncodedAbsolutePathArray().Length);
                    AssertArrayEqual(paths, reader.ReadDeltaEncodedAbsolutePathArray());
                    AssertArrayEqual(tablePaths, reader.ReadDeltaEncodedAbsolutePathArray());
                    AssertArrayEqual(files, reader.ReadDeltaEncodedFileArtifactArray());
                    AssertArrayEqual(filesWithAttributes, reader.ReadDeltaEncodedFileArtifactWithAttributesArray());
                    AssertArrayEqual(directories, reader.ReadDeltaEncodedDirectoryArtifactArray());
                    AssertArrayEqual(sortedFiles.BaseArray, reader.ReadDeltaEncodedSortedFileArtifactArray().BaseArray);
                    AssertArrayEqual(sortedDirectories.BaseArray, reader.ReadDeltaEncodedSortedDirectoryArtifactArray().BaseArray);
                    Assert.Equal(stream.Length, stream.Position);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidDeltaEncodedPathIsRejected(bool debug)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new PipWriter(debug, stream, leaveOpen: true, logStats: true))
                {
                    writer.Start<ReadOnlyArray<AbsolutePath>>();
                    writer.WriteCompact(1);
                    writer.Start<AbsolutePath>();
                    ((BinaryWriter)writer).Write(0);
                    writer.End();
                    writer.End();
                }

                stream.Position = 0;
                using (var reader = new PipReader(debug, new StringTable(), stream, leaveOpen: true))
                {
                    Assert.Throws<InvalidDataException>(() => reader.ReadDeltaEncodedAbsolutePathArray());
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OverflowingDeltaEncodedPathIsRejected(bool debug)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new PipWriter(debug, stream, leaveOpen: true, logStats: true))
                {
                    writer.Start<ReadOnlyArray<AbsolutePath>>();
                    writer.WriteCompact(2);
                    writer.Start<AbsolutePath>();
                    ((BinaryWriter)writer).Write(int.MaxValue);
                    writer.End();
                    writer.Start<AbsolutePath>();
                    writer.WriteCompact(2U);
                    writer.End();
                    writer.End();
                }

                stream.Position = 0;
                using (var reader = new PipReader(debug, new StringTable(), stream, leaveOpen: true))
                {
                    Assert.Throws<InvalidDataException>(() => reader.ReadDeltaEncodedAbsolutePathArray());
                }
            }
        }

        private static void AssertArrayEqual<T>(ReadOnlyArray<T> expected, ReadOnlyArray<T> actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }
    }
}
