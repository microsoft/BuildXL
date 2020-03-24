// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// VSTS chunk-level deduplication file node
    /// </summary>
    public sealed class DedupNodeHashAlgorithm : HashAlgorithm
    {
        /// <summary>
        /// Creates a chunker appropriate to the runtime environment
        /// </summary>
        public static IChunker CreateChunker()
        {
            if (IsComChunkerSupported)
            {
                try
                {
                    return new ComChunker();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Some older versions of windows. Fall back to managed chunker.
                }
            }

            return new ManagedChunker();
        }

        /// <summary>
        /// Returns whether or not this environment supports chunking via the COM library
        /// </summary>
        public static readonly bool IsComChunkerSupported =
#if NET_FRAMEWORK
            true;
#else
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
#endif

        private readonly List<ChunkInfo> _chunks = new List<ChunkInfo>();
        private readonly DedupNodeTree.Algorithm _treeAlgorithm;
        private readonly IChunker _chunker;
        private IChunkerSession _session;
        private DedupNode? _lastNode;

        /// <inheritdoc />
        public override int HashSize => 256;

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm()
            : this(DedupNodeTree.Algorithm.MaximallyPacked, CreateChunker())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm(DedupNodeTree.Algorithm treeAlgorithm)
            : this(treeAlgorithm, CreateChunker())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm(IChunker chunker)
            : this(DedupNodeTree.Algorithm.MaximallyPacked, chunker)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm(DedupNodeTree.Algorithm treeAlgorithm, IChunker chunker)
        {
            _treeAlgorithm = treeAlgorithm;
            _chunker = chunker;
            Initialize();
        }

        /// <summary>
        /// Creates a copy of the chunk list.
        /// </summary>
        // ReSharper disable once PossibleInvalidOperationException
        public DedupNode GetNode() => _lastNode.Value;

        /// <inheritdoc />
        public override void Initialize()
        {
            _chunks.Clear();
            _session = _chunker.BeginChunking(SaveChunks);
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            if (_chunker.TotalBytes == 0)
            {
                _lastNode = null;
            }

            _session.PushBuffer(array, ibStart, cbSize);
        }

        /// <inheritdoc />
        protected override byte[] HashFinal()
        {
            _session.Dispose();

            if (_chunker.TotalBytes == 0)
            {
                _chunks.Add(new ChunkInfo(0, 0, DedupChunkHashInfo.Instance.EmptyHash.ToHashByteArray()));
            }

            _lastNode = DedupNodeTree.Create(_chunks, _treeAlgorithm);

            // The array returned by this function will be cleared when this is disposed, so clone it.
            return _lastNode.Value.Hash.ToArray();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunker.Dispose();
            }

            base.Dispose(disposing);
        }

        private void SaveChunks(ChunkInfo chunk)
        {
            _chunks.Add(chunk);
        }
    }
}
