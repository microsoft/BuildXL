// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using BuildXL.Scheduler.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for <see cref="ContentHashSerializer"/> — the XLG content hash interning protocol.
    /// </summary>
    public sealed class ContentHashSerializerTests : XunitBuildXLTest
    {
        public ContentHashSerializerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void RoundtripDuplicateHashes()
        {
            var hash = ContentHashingUtilities.CreateRandom();
            var hashes = RoundtripHashes(new[] { hash, hash, hash });

            foreach (var h in hashes)
            {
                XAssert.AreEqual(hash, h);
            }
        }

        [Fact]
        public void RoundtripManyDistinctHashes()
        {
            var input = new ContentHash[10];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = ContentHashingUtilities.CreateRandom();
            }

            var output = RoundtripHashes(input);

            for (int i = 0; i < input.Length; i++)
            {
                XAssert.AreEqual(input[i], output[i], $"Hash mismatch at index {i}");
            }
        }

        [Fact]
        public void InterleaveInternedAndRepeatedHashes()
        {
            // Interleave new hashes with repeated references to earlier ones
            var hashA = ContentHashingUtilities.CreateRandom();
            var hashB = ContentHashingUtilities.CreateRandom();
            var hashC = ContentHashingUtilities.CreateRandom();

            var input = new[] { hashA, hashB, hashA, hashC, hashB, hashA, hashC, hashC };
            var output = RoundtripHashes(input);

            for (int i = 0; i < input.Length; i++)
            {
                XAssert.AreEqual(input[i], output[i], $"Hash mismatch at index {i}");
            }
        }

        [Fact]
        public void NonEventWriterFallsBackToRawBytes()
        {
            // When the writer is not a BinaryLogger.EventWriter, WriteContentHash should
            // write raw hash bytes that can be read back with ContentHashingUtilities.CreateFrom.
            var hash = ContentHashingUtilities.CreateRandom();

            using (var ms = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(debug: false, stream: ms, leaveOpen: true, logStats: false))
                {
                    ContentHashSerializer.WriteContentHash(writer, hash);
                }

                ms.Position = 0;

                using (var reader = new BuildXLReader(debug: false, stream: ms, leaveOpen: true))
                {
                    var readBack = ContentHashingUtilities.CreateFrom(reader);
                    XAssert.AreEqual(hash, readBack);
                }
            }
        }

        /// <summary>
        /// Writes multiple hashes (one per event) through BinaryLogger, then reads them
        /// back via BinaryLogReader and returns the deserialized hashes.
        /// </summary>
        private ContentHash[] RoundtripHashes(ContentHash[] hashes)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            Guid logId = Guid.NewGuid();
            var results = new ContentHash[hashes.Length];
            int readIndex = 0;

            using (var ms = new MemoryStream())
            {
                using (var logger = new BinaryLogger(ms, context, logId, closeStreamOnDispose: false))
                {
                    foreach (var hash in hashes)
                    {
                        using (var scope = logger.StartEvent(1, workerId: 0))
                        {
                            ContentHashSerializer.WriteContentHash(scope.Writer, hash);
                        }
                    }
                }

                ms.Position = 0;

                using (var reader = new BinaryLogReader(ms, context))
                {
                    reader.RegisterHandler(1, (eventId, workerId, timestamp, eventReader) =>
                    {
                        results[readIndex++] = ContentHashSerializer.ReadContentHash(eventReader);
                    });
                    while (reader.ReadEvent() == BinaryLogReader.EventReadResult.Success) { }
                }
            }

            XAssert.AreEqual(hashes.Length, readIndex, "Not all events were read back");
            return results;
        }
    }
}
