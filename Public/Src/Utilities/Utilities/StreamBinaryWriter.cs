// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper class that can be used for binary serializing multiple objects in memory efficient way
    /// by reusing <see cref="MemoryStream"/> and <see cref="BinaryWriter"/> instances.
    /// </summary>
    /// <remarks>
    /// The instance of this type is not thread-safe.
    /// </remarks>
    public sealed class StreamBinaryWriter
    {
        /// <nodoc />
        public MemoryStream Buffer { get; }

        /// <nodoc />
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
        public struct PositionRestorer : IDisposable
        {
            private readonly StreamBinaryWriter _writer;

            /// <nodoc />
            internal PositionRestorer(StreamBinaryWriter writer)
            {
                _writer = writer;
            }

            /// <inheritdoc />
            public void Dispose() => _writer.ResetPosition();
        }
    }
}
