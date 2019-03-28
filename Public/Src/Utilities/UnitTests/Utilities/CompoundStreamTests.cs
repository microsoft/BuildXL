// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Utilities.Serialization;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using BuildXL.Utilities.Collections;
using System.Linq;
using System.Collections.Generic;

namespace Test.BuildXL.Utilities
{
    public class CompoundStreamTests : XunitBuildXLTest
    {
        private Dictionary<string, ReadOnlyArray<byte>> m_fileSystem = 
            new Dictionary<string, ReadOnlyArray<byte>>(StringComparer.OrdinalIgnoreCase);

        private const int CheckBytesUsingReadBufferSize = 1231;

        public CompoundStreamTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void CompoundStreamZiplikeRoundTripTest()
        {
            // File with the same size as write byte
            GenerateRandomFile(CheckBytesUsingReadBufferSize);

            // Test some common powers of two
            GenerateRandomFile(1024);
            GenerateRandomFile(2048);
            GenerateRandomFile(4096);

            // Test boundary conditions around block size
            GenerateRandomFile(CompoundStream.DefaultBlockSize);
            GenerateRandomFile(CompoundStream.DefaultBlockSize * 2);
            GenerateRandomFile(CompoundStream.DefaultBlockSize * 4);
            GenerateRandomFile(CompoundStream.DefaultBlockSize - 1);
            GenerateRandomFile(CompoundStream.DefaultBlockSize + 1);

            // Zero length file
            GenerateRandomFile(0);

            // Single byte file
            GenerateRandomFile(1);

            // Large file test
            GenerateRandomFile(CompoundStream.DefaultBlockSize * 16 + 3243);

            var allFiles = m_fileSystem.Keys.ToArray();

            var fileIdMap = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var filesWithIds = new BlockingCollection<string>();

            var zipLikePath = "ZipLikeCompoundStream";

            using (var zipLikeFileStream = new MemoryStream())
            {
                using (var zipLikeStream = CompoundStream.OpenWrite(zipLikeFileStream))
                {
                    var directoryWriteTask = Task.Run(() =>
                    {
                        using (var listingStream = zipLikeStream.InitialPartStream)
                        using (var writer = new BinaryWriter(listingStream))
                        {
                            writer.Write(allFiles.Length);
                            foreach (var file in filesWithIds.GetConsumingEnumerable())
                            {
                                writer.Write(file);
                                writer.Write(fileIdMap[file]);
                            }
                        }
                    });

                    Parallel.ForEach(allFiles, file =>
                    {
                        using (var filePartStream = zipLikeStream.CreateWritePartStream(4096))
                        using (var fileStream = OpenRead(file))
                        {
                            fileIdMap[file] = filePartStream.Index;
                            filesWithIds.Add(file);
                            fileStream.CopyTo(filePartStream);
                        }
                    });

                    filesWithIds.CompleteAdding();

                    directoryWriteTask.GetAwaiter().GetResult();
                }

                m_fileSystem[zipLikePath] = zipLikeFileStream.ToArray().ToReadOnlyArray();
            }

            var readFileIdMap = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var readFilesWithIds = new BlockingCollection<string>();

            using (var zipLikeStream = CompoundStream.OpenRead(() => OpenRead(zipLikePath)))
            {
                var listingReadTask = Task.Run(() =>
                {
                    using (var listingStream = zipLikeStream.InitialPartStream)
                    using (var reader = new BinaryReader(listingStream))
                    {
                        var fileCount = reader.ReadInt32();
                        for (int i = 0; i < fileCount; i++)
                        {
                            var file = reader.ReadString();
                            var fileId = reader.ReadInt32();
                            readFileIdMap[file] = fileId;
                            readFilesWithIds.Add(file);
                        }

                        readFilesWithIds.CompleteAdding();
                    }
                });

                Parallel.ForEach(readFilesWithIds.GetConsumingEnumerable(), file =>
                {
                    using (var filePartStream = zipLikeStream.OpenReadPartStream(readFileIdMap[file]))
                    using (var fileContentsStream = new MemoryStream((int)filePartStream.Length))
                    {
                        if (file.GetHashCode() % 2 == 1)
                        {
                            CheckBytesUsingCopyTo(filePartStream, file);
                        }
                        else
                        {
                            CheckBytesUsingRead(filePartStream, file);
                        }

                        filePartStream.CopyTo(fileContentsStream);
                    }
                });
            }
        }

        private void GenerateRandomFile(int size)
        {
            Random r = new Random();
            var bytes = new byte[size];
            r.NextBytes(bytes);
            var content = ReadOnlyArray<byte>.FromWithoutCopy(bytes);
            m_fileSystem[size + "_" + Guid.NewGuid().ToString()] = content;
        }

        private MemoryStream OpenRead(string file)
        {
            return new MemoryStream(m_fileSystem[file].ToArray(), false);
        }

        private void CheckBytesUsingRead(Stream filePartStream, string file)
        {
            byte[] fileBuffer = new byte[CheckBytesUsingReadBufferSize];
            byte[] partBuffer = new byte[CheckBytesUsingReadBufferSize];
            using (var fileStream = OpenRead(file))
            {
                var fileReadBytesCount = fileStream.Read(fileBuffer, 0, fileBuffer.Length);
                var partReadBytesCount = filePartStream.Read(partBuffer, 0, partBuffer.Length);

                Assert.Equal(fileReadBytesCount, partReadBytesCount);
                for (int i = 0; i < fileReadBytesCount; i++)
                {
                    Assert.Equal(fileBuffer[i], partBuffer[i]);
                }
            }
        }

        private void CheckBytesUsingCopyTo(Stream filePartStream, string file)
        {
            using (var fileContentsStream = new MemoryStream((int)filePartStream.Length))
            {
                var bytes = m_fileSystem[file];
                Assert.Equal(bytes.Length, filePartStream.Length);
                filePartStream.CopyTo(fileContentsStream);

                Assert.Equal(fileContentsStream.Length, filePartStream.Length);

                var partReadBytes = fileContentsStream.GetBuffer();
                for (int i = 0; i < bytes.Length; i++)
                {
                    Assert.Equal(bytes[i], partReadBytes[i]);
                }
            }
        }
    }
}
