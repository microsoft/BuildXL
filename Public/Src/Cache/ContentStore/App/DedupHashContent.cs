// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Synchronization;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        private readonly HashSet<byte[]> _allNodes = new HashSet<byte[]>(ByteArrayComparer.Instance);
        private readonly HashSet<byte[]> _allChunks = new HashSet<byte[]>(ByteArrayComparer.Instance);
        private ulong _uniqueBytes;
        private ulong _totalBytes;
        private ulong _totalChunks;
        private ulong _totalNodes;
        private bool _displayChunks;
        private bool _displayChildNodes;

        /// <summary>
        ///     Hash some files and optionally display their chunks
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [Verb(Aliases = "dhf", Description = "Hash content files")]
        public void DedupHashFile
            (
            [Required] string[] path,
            [DefaultValue(false)] bool chunks,
            [DefaultValue(false)] bool childNodes,
            [DefaultValue(false)] bool rollingHash,
            [DefaultValue(FileSystemConstants.FileIOBufferSize)] int bufferSize,
            [DefaultValue((long)0)] long startOffset
            )
        {
            Initialize();

            _displayChunks = chunks;
            _displayChildNodes = childNodes;

            var paths = new List<AbsolutePath>();

            foreach (AbsolutePath root in path.Select(p => new AbsolutePath(Path.GetFullPath(p))))
            {
                if (_fileSystem.DirectoryExists(root))
                {
                    paths.AddRange(_fileSystem.EnumerateFiles(root, EnumerateOptions.Recurse).Select(fileInfo => fileInfo.FullPath));
                }
                else if (_fileSystem.FileExists(root))
                {
                    paths.Add(root);
                }
                else
                {
                    throw new ArgumentException("given path is not an existing file or directory");
                }
            }

            var buffer = new byte[bufferSize];

            using (var hasher = new DedupNodeHashAlgorithm(rollingHash ? DedupNodeTree.Algorithm.RollingHash : DedupNodeTree.Algorithm.MaximallyPacked))
            {
                foreach (var p in paths)
                {
                    hasher.Initialize();
                    TaskSafetyHelpers.SyncResultOnThreadPool(async () =>
                    {
                        using (var fs = await _fileSystem.OpenReadOnlySafeAsync(p, FileShare.Read | FileShare.Delete))
                        {
                            fs.Position = startOffset;
                            int bytesRead;
                            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                            }
                            hasher.TransformFinalBlock(new byte[0], 0, 0);
                            DedupNode root = hasher.GetNode();
                            ulong offset = 0;
                            LogNode(true, string.Empty, root, p, ref offset);
                        }

                        return 0;
                    });
                }
            }

            _logger.Always("Totals:");
            _logger.Always($"Bytes: Unique={_uniqueBytes:N0} Total={_totalBytes:N0}");
            _logger.Always($"Chunks: Unique={_allChunks.Count:N0} Total={_totalChunks:N0}");
            _logger.Always($"Nodes: Unique={_allNodes.Count:N0} Total={_totalNodes:N0}");
        }

        private void LogNode(bool displayChildNodes, string indent, DedupNode root, AbsolutePath path, ref ulong offset)
        {
            _totalNodes++;
            bool newNode = _allNodes.Add(root.Hash);

            if (displayChildNodes)
            {
                var hash = new ContentHash(HashType.DedupNode, root.Hash);
                char newNodeChar = newNode ? '*' : 'd';
                _logger.Always($"{indent}{hash} {newNodeChar} {path}");
            }

            if (root.ChildNodes != null)
            {
                foreach (var child in root.ChildNodes)
                {
                    switch (child.Type)
                    {
                        case DedupNode.NodeType.ChunkLeaf:
                            _totalBytes += child.TransitiveContentBytes;
                            _totalChunks++;

                            bool newChunk = _allChunks.Add(child.Hash);
                            if (newChunk)
                            {
                                _uniqueBytes += child.TransitiveContentBytes;
                            }

                            if (_displayChunks)
                            {
                                char newChunkChar = newChunk ? '*' : 'd';
                                _logger.Always($"{indent} {offset} {child.Hash.ToHex()} {newChunkChar}");
                            }

                            offset += child.TransitiveContentBytes;
                            break;
                        case DedupNode.NodeType.InnerNode:
                            LogNode(_displayChildNodes, indent + " ", child, null, ref offset);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }
        }
    }
}
