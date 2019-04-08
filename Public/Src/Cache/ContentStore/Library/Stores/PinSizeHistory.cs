// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     History of pinned sizes by a set of pin contexts.
    /// </summary>
    public sealed class PinSizeHistory
    {
        private const string BinaryFileName = "PinHistory.bin";
        private readonly PinHistoryBuffer _pinHistoryBuffer;
        private readonly AbsolutePath _directoryPath;
        private readonly IClock _clock;
        private long _timestampInTick;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinSizeHistory" /> class.
        /// </summary>
        private PinSizeHistory(IClock clock, PinHistoryBuffer pinHistoryBuffer, long timestampInTick, AbsolutePath directoryPath)
        {
            Contract.Requires(clock != null);
            Contract.Requires(pinHistoryBuffer != null);
            Contract.Requires(directoryPath != null);

            _clock = clock;
            _pinHistoryBuffer = pinHistoryBuffer;
            _timestampInTick = timestampInTick;
            _directoryPath = directoryPath;
        }

        /// <summary>
        ///     Loads pin history from disk if exists, otherwise create a new instance.
        /// </summary>
        public static async Task<PinSizeHistory> LoadOrCreateNewAsync(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath directoryPath,
            int? newCapacity = default(int?))
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(directoryPath != null);

            var filePath = directoryPath / BinaryFileName;

            try
            {
                if (!fileSystem.FileExists(filePath))
                {
                    return new PinSizeHistory(
                        clock,
                        newCapacity.HasValue ? PinHistoryBuffer.Create(newCapacity.Value) : PinHistoryBuffer.Create(),
                        clock.UtcNow.Ticks,
                        directoryPath);
                }

                using (var stream = await fileSystem.OpenReadOnlySafeAsync(filePath, FileShare.Delete))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        var pinHistoryBuffer = PinHistoryBuffer.Deserialize(reader, newCapacity);
                        var timeStampInTick = reader.ReadInt64();
                        return new PinSizeHistory(clock, pinHistoryBuffer, timeStampInTick, directoryPath);
                    }
                }
            }
            catch (IOException)
            {
                return new PinSizeHistory(
                    clock,
                    newCapacity.HasValue ? PinHistoryBuffer.Create(newCapacity.Value) : PinHistoryBuffer.Create(),
                    clock.UtcNow.Ticks,
                    directoryPath);
            }
        }

        /// <summary>
        ///     Adds pinned size to the history.
        /// </summary>
        public void Add(long pinnedSize)
        {
            Contract.Requires(pinnedSize >= 0);

            lock (_pinHistoryBuffer)
            {
                _pinHistoryBuffer.Add(pinnedSize);
                _timestampInTick = _clock.UtcNow.Ticks;
            }
        }

        /// <summary>
        ///     Reads the last history that is within a specified width of window.
        /// </summary>
        public ReadHistoryResult ReadHistory(int windowSize)
        {
            Contract.Requires(windowSize >= 0);

            lock (_pinHistoryBuffer)
            {
                return new ReadHistoryResult(_pinHistoryBuffer.GetLastEntries(windowSize), _timestampInTick);
            }
        }

        /// <summary>
        ///     Saves this instance to disk.
        /// </summary>
        public async Task SaveAsync(IAbsFileSystem fileSystem)
        {
            Contract.Requires(fileSystem != null);

            var filePath = _directoryPath / BinaryFileName;

            try
            {
                fileSystem.DeleteFile(filePath);

                using (
                    var stream =
                        await fileSystem.OpenSafeAsync(filePath, FileAccess.Write, FileMode.CreateNew, FileShare.Delete))
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        lock (_pinHistoryBuffer)
                        {
                            _pinHistoryBuffer.Serialize(writer);
                            writer.Write(_timestampInTick);
                        }
                    }
                }
            }
            catch (IOException)
            {
                // When failed, clean up so that it is not used in the next load.
                fileSystem.DeleteFile(filePath);
            }
        }

        /// <summary>
        ///     Structure for reading history result.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct ReadHistoryResult
        {
            /// <summary>
            ///     History window.
            /// </summary>
            public readonly IReadOnlyList<long> Window;

            /// <summary>
            ///     Time stamp (in tick) of the current history.
            /// </summary>
            public readonly long TimestampInTick;

            /// <summary>
            ///     Initializes a new instance of the <see cref="ReadHistoryResult"/> struct.
            /// </summary>
            public ReadHistoryResult(IReadOnlyList<long> window, long timestampInTick)
            {
                Window = window;
                TimestampInTick = timestampInTick;
            }
        }

        private class PinHistoryBuffer
        {
            /// <summary>
            ///     Default capacity.
            /// </summary>
            private const int DefaultCapacity = 100;

            private static readonly long[] EmptyEntries = new long[0];

            private long[] _buffer;
            private int _next;

            /// <summary>
            ///     Initializes a new instance of the <see cref="PinHistoryBuffer" /> class.
            /// </summary>
            private PinHistoryBuffer(int capacity)
            {
                Contract.Requires(capacity >= 0);

                ResizeBuffer(capacity);
            }

            private void ResizeBuffer(int capacity)
            {
                _buffer = new long[capacity];

                for (int i = 0; i < _buffer.Length; ++i)
                {
                    _buffer[i] = -1;
                }

                _next = 0;
            }

            /// <summary>
            ///     Creates a new instance of the <see cref="PinHistoryBuffer" /> class.
            /// </summary>
            public static PinHistoryBuffer Create(int capacity = DefaultCapacity)
            {
                return new PinHistoryBuffer(capacity);
            }

            /// <summary>
            ///     Deserializes to a new instance of <see cref="PinHistoryBuffer" /> class.
            /// </summary>
            public static PinHistoryBuffer Deserialize(BinaryReader reader, int? newCapacity = default(int?))
            {
                Contract.Requires(reader != null);
                Contract.Requires(!newCapacity.HasValue || newCapacity.Value >= 0);

                var capacity = reader.ReadInt32();
                var pinHistoryBuffer = Create(capacity);

                for (int i = 0; i < capacity; ++i)
                {
                    pinHistoryBuffer._buffer[i] = reader.ReadInt64();
                }

                pinHistoryBuffer._next = reader.ReadInt32();

                if (newCapacity.HasValue)
                {
                    pinHistoryBuffer.Resize(newCapacity.Value);
                }

                return pinHistoryBuffer;
            }

            /// <summary>
            ///     Adds an entry to the buffer.
            /// </summary>
            public void Add(long entry)
            {
                Contract.Requires(entry >= 0);

                if (_buffer.Length == 0)
                {
                    return;
                }

                Contract.Assert(_next >= 0 && _next < _buffer.Length);
                _buffer[_next++] = entry;

                if (_next == _buffer.Length)
                {
                    _next = 0;
                }
            }

            /// <summary>
            ///     Gets the last N entries or less if buffer is small.
            /// </summary>
            public long[] GetLastEntries(int numberOfLastEntries)
            {
                Contract.Requires(numberOfLastEntries >= 0);

                if (_buffer.Length == 0 || numberOfLastEntries == 0)
                {
                    return EmptyEntries;
                }

                var entries = new List<long>(numberOfLastEntries);
                var startIndex = _next - 1;
                if (startIndex == -1)
                {
                    startIndex = _buffer.Length - 1;
                }

                Contract.Assert(startIndex >= 0 && startIndex < _buffer.Length);
                var currentIndex = startIndex;

                for (int i = 0; i < numberOfLastEntries; ++i)
                {
                    Contract.Assert(currentIndex >= 0 && currentIndex < _buffer.Length);

                    var entry = _buffer[currentIndex];

                    if (entry == -1)
                    {
                        // Buffer is empty at the current index.
                        break;
                    }

                    entries.Add(entry);
                    --currentIndex;

                    if (currentIndex == -1)
                    {
                        // Index currentIndex should cycle.
                        currentIndex = _buffer.Length - 1;
                    }

                    if (currentIndex == startIndex)
                    {
                        // Index currentIndex reaches the starting index.
                        break;
                    }
                }

                return entries.ToArray();
            }

            /// <summary>
            ///     Resizes buffer with a new capacity.
            /// </summary>
            public void Resize(int newCapacity)
            {
                Contract.Requires(newCapacity >= 0);

                if (_buffer.Length == newCapacity)
                {
                    // New capacity equals current one, then do nothing.
                    return;
                }

                // Get all entries.
                var entries = GetLastEntries(_buffer.Length);
                var startIndex = newCapacity >= entries.Length ? entries.Length - 1 : newCapacity - 1;

                ResizeBuffer(newCapacity);

                for (int i = startIndex; i >= 0; --i)
                {
                    Add(entries[i]);
                }
            }

            /// <summary>
            ///     Serializes buffer through a <see cref="BinaryWriter" />.
            /// </summary>
            public void Serialize(BinaryWriter writer)
            {
                Contract.Requires(writer != null);

                writer.Write(_buffer.Length);

                foreach (var b in _buffer)
                {
                    writer.Write(b);
                }

                writer.Write(_next);
            }
        }
    }
}
