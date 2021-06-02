// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;

#pragma warning disable CS3001 // CLS
#pragma warning disable CS3002
#pragma warning disable CS3003

namespace BuildXL.Cache.ContentStore.Hashing.FileSystemHelpers
{
    /// <summary>
    /// A helper class for working with memory mapped files.
    /// </summary>
    public abstract unsafe class MemoryMappedFileHandle : IDisposable
    {
        /// <nodoc />
        protected readonly byte* Data;
        
        /// <nodoc />
        protected readonly int Length;

        private readonly MemoryMappedFile _memoryMappedFile;
        private readonly MemoryMappedViewAccessor _memoryMappedViewAccessor;

        /// <nodoc />
        protected MemoryMappedFileHandle(int length, MemoryMappedFile memoryMappedFile, MemoryMappedViewAccessor memoryMappedViewAccessor)
        {
            Length = length;
            _memoryMappedFile = memoryMappedFile;
            _memoryMappedViewAccessor = memoryMappedViewAccessor;
            _memoryMappedViewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Data);
        }

        /// <summary>
        /// Creates a readonly handle for working with a given <paramref name="fs"/> as with memory mapped file.
        /// </summary>
        public static ReadOnlyMemoryMappedFileHandle CreateReadOnly(FileStream fs, bool leaveOpen = true)
        {
            ValidateFileLength(fs);

            var memoryMappedFile = MemoryMappedFile.CreateFromFile(
                fs,
                mapName: null,
                capacity: fs.Length,
                access: MemoryMappedFileAccess.Read,
                inheritability: HandleInheritability.None,
                leaveOpen: leaveOpen);

            var viewAccessor = memoryMappedFile.CreateViewAccessor(offset: 0, size: fs.Length, access: MemoryMappedFileAccess.Read);
            return new ReadOnlyMemoryMappedFileHandle((int)fs.Length, memoryMappedFile, viewAccessor);
        }

        /// <summary>
        /// Creates a read-write handle for working with a given <paramref name="fs"/> as with memory mapped file.
        /// </summary>
        public static ReadWriteMemoryMappedFileHandle CreateReadWrite(FileStream fs, bool leaveOpen = true)
        {
            ValidateFileLength(fs);

            var memoryMappedFile = MemoryMappedFile.CreateFromFile(
                fs,
                mapName: null,
                capacity: fs.Length,
                access: MemoryMappedFileAccess.ReadWrite,
                inheritability: HandleInheritability.Inheritable,
                leaveOpen: leaveOpen);

            var viewAccessor = memoryMappedFile.CreateViewAccessor(offset: 0, size: fs.Length, access: MemoryMappedFileAccess.ReadWrite);
            return new ReadWriteMemoryMappedFileHandle((int)fs.Length, memoryMappedFile, viewAccessor);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _memoryMappedViewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _memoryMappedViewAccessor.Dispose();
            _memoryMappedFile.Dispose();
        }

        private static void ValidateFileLength(FileStream fs)
        {
            if (fs.Length == 0)
            {
                throw new ArgumentException("File length can't be 0 in order to use memory mapped file", nameof(fs));
            }

            if (fs.Length >= int.MaxValue)
            {
                throw new ArgumentException(
                    $"Can't open a memory mapped file projection for the file, because the length exceeds int.MaxValue. The Length is: {fs.Length}.");
            }
        }
    }

    /// <summary>
    /// A read-only memory mapped file handle for getting the content of the file as <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public unsafe class ReadOnlyMemoryMappedFileHandle : MemoryMappedFileHandle
    {
        internal ReadOnlyMemoryMappedFileHandle(int length, MemoryMappedFile memoryMappedFile, MemoryMappedViewAccessor memoryMappedViewAccessor)
            : base(length, memoryMappedFile, memoryMappedViewAccessor)
        {
        }

        /// <summary>
        /// Gets the content of the file as a span of bytes.
        /// </summary>
        public ReadOnlySpan<byte> Content => new ReadOnlySpan<byte>(Data, Length);
    }

    /// <summary>
    /// A read-write memory mapped file handle for getting the content of the file as <see cref="Span{T}"/>.
    /// </summary>
    public unsafe class ReadWriteMemoryMappedFileHandle : MemoryMappedFileHandle
    {
        internal ReadWriteMemoryMappedFileHandle(int length, MemoryMappedFile memoryMappedFile, MemoryMappedViewAccessor memoryMappedViewAccessor)
            : base(length, memoryMappedFile, memoryMappedViewAccessor)
        {
        }

        /// <summary>
        /// Gets the content of the file as a span of bytes.
        /// </summary>
        public Span<byte> Content => new Span<byte>(Data, Length);
    }
}
