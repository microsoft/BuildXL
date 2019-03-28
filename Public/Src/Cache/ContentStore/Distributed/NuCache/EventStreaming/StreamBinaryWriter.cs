// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Helper class that can be used for binary serializing multiple objects in memory efficient way
    /// by reusing <see cref="MemoryStream"/> and <see cref="BinaryWriter"/> instances.
    /// </summary>
    /// <remarks>
    /// The instance of this type is not thread-safe.
    /// </remarks>
    internal sealed class StreamBinaryWriter
    {
        public MemoryStream Buffer { get; }
        public BuildXLWriter Writer { get; }

        /// <nodoc />
        public StreamBinaryWriter()
        {
            Buffer = new MemoryStream();
            Writer = BuildXLWriter.Create(Buffer);
        }

        /// <nodoc />
        public ArraySegment<byte> Serialize(Action<BuildXLWriter> serializeFunc)
        {
            var bufferOffset = (int)Buffer.Position;
            serializeFunc(Writer);
            var length = ((int)Buffer.Position) - bufferOffset;
            return new ArraySegment<byte>(Buffer.GetBuffer(), bufferOffset, length);
        }

        /// <nodoc />
        public PositionRestorer PreservePosition()
        {
            return new PositionRestorer(this);
        }

        /// <nodoc />
        public void ResetPosition() => Buffer.SetLength(0);

        /// <summary>
        /// Helper struct that restores the position of <see cref="StreamBinaryWriter"/>.
        /// </summary>
        internal struct PositionRestorer : IDisposable
        {
            private readonly StreamBinaryWriter _writer;

            internal PositionRestorer(StreamBinaryWriter writer)
            {
                _writer = writer;
            }

            public void Dispose() => _writer.ResetPosition();
        }
    }
}
