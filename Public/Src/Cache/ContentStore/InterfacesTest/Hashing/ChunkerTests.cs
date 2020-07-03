// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Test;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ChunkerTests
    {
        [Fact]
        public void CanEnumerateChunksInChunk()
        {
            DedupNode rootFromhash;
            using (var hasher = new DedupNodeHashAlgorithm())
            {
                hasher.SetInputLength(1);
                hasher.ComputeHash(new byte[1]);
                rootFromhash = hasher.GetNode().ChildNodes.Single();
            }

            Assert.Equal(rootFromhash.HashString, rootFromhash.EnumerateChunkLeafsInOrder().Single().HashString);
        }

        [Fact]
        public void ChunksEnumeratedAsFileIsReadManaged()
        {
            ChunksEnumeratedAsFileIsRead(() => new ManagedChunker(ChunkerConfiguration.Default));
        }

        [MtaFact]
        [Trait("Category", "WindowsOSOnly")]
        public void ChunksEnumeratedAsFileIsReadCOM()
        {
            ChunksEnumeratedAsFileIsRead(() => new ComChunker(ChunkerConfiguration.Default));
        }

        private void ChunksEnumeratedAsFileIsRead(Func<IChunker> chunkerFactory)
        {
            var chunks = new List<ChunkInfo>();

            byte[] bytes;

            using (var chunker = chunkerFactory())
            {
                bytes = new byte[4 * chunker.Configuration.MinPushBufferSize];

                var r = new Random(Seed: 0);
                r.NextBytes(bytes);

                using (var session = chunker.BeginChunking(chunk =>
                {
                    chunks.Add(chunk);
                }))
                {
                    int pushSize = 2 * chunker.Configuration.MinPushBufferSize;
                    int lastChunkCount = 0;
                    for (int i = 0; i < bytes.Length; i += pushSize)
                    {
                        session.PushBuffer(bytes, i, Math.Min(pushSize, bytes.Length - i));
                        Assert.True(chunks.Count > lastChunkCount);
                        lastChunkCount = chunks.Count;
                    }
                }
            }

            string[] expectedChunkHashes = chunks.Select(c => c.Hash.ToHex()).ToArray();

            DedupNode rootFromhash;
            string[] actualChunkHashes;
            using (var hasher = new DedupNodeHashAlgorithm())
            {
                hasher.SetInputLength(bytes.Length);
                hasher.ComputeHash(bytes);
                rootFromhash = hasher.GetNode();
                actualChunkHashes = rootFromhash.EnumerateChunkLeafsInOrder().Select(c => c.Hash.ToHex()).ToArray();
                Assert.Equal(expectedChunkHashes, actualChunkHashes);
            }

            var seenNodes = new HashSet<byte[]>(chunks.Select(c => c.Hash), ByteArrayComparer.Instance);

            DedupNode? root = null;
            foreach (var node in PackedDedupNodeTree.EnumerateTree(chunks)
                                                    .Where(n => n.Type != DedupNode.NodeType.ChunkLeaf))
            {
                foreach (var child in node.ChildNodes)
                {
                    Assert.True(seenNodes.Contains(child.Hash));
                }

                Assert.True(seenNodes.Add(node.Hash));
                root = node;
            }

            Assert.True(root.HasValue);

            // ReSharper disable once PossibleInvalidOperationException
            Assert.Equal(rootFromhash, root.Value);
            actualChunkHashes = root.Value.EnumerateChunkLeafsInOrder().Select(c => c.Hash.ToHex()).ToArray();
            Assert.Equal(expectedChunkHashes, actualChunkHashes);
        }
    }
}
