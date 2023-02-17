// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Represents content of the file.
    /// </summary>
    public readonly struct FileContent : IEquatable<FileContent>
    {
        // In the future, this type can start using pooled buffers.
        // Currently, unfortunately, this is not possible.
        // In current architecture, parsed source file holds a reference to a content
        // for the entire lifetime, preventing us from reusing buffers for different files.

        /// <nodoc />
        private FileContent(char[] content, int length)
        {
            Contract.Requires(content != null);
            Contract.Requires(length >= 0);
            Contract.Requires(length <= content.Length);

            Content = content;
            Length = length;
        }

        /// <summary>
        /// Invalid instance.
        /// </summary>
        public static FileContent Invalid { get; } = default;

        /// <summary>
        /// Content of a file.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public char[] Content { get; }

        /// <summary>
        /// Length of a content.
        /// </summary>
        /// <remarks>
        /// This type can wrap an array from a pool, meaning that the array can be just partially filled with data.
        /// </remarks>
        public int Length { get; }

        /// <summary>
        /// Returns true if the instance is valid.
        /// </summary>
        public bool IsValid => Content != null;

        /// <summary>
        /// Returns the content as a full string.
        /// </summary>
        public string GetContentAsString()
        {
            return new string(Content, 0, Length);
        }

        /// <inheritdoc />
        public bool Equals(FileContent other)
        {
            return Content == other.Content;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is FileContent && Equals((FileContent)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Content?.GetHashCode() ?? 42;
        }

        /// <nodoc />
        public static bool operator ==(FileContent left, FileContent right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileContent left, FileContent right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Reads a file content from a stream <paramref name="stream"/>.
        /// </summary>
        public static async Task<FileContent> ReadFromAsync(Stream stream)
        {
            Contract.Requires(stream != null);

            long initialPosition = stream.Position;

            stream.Position = 0;

            char[] charBuffer;
            try
            {
                // Since we are not planning to support files larger that 2G, we throw an exception.
                // The cast below can result in a negative number, and can throw an Overflow exception.
                // Also, the allocation can result in OutOfMemoryException when the length is too big.
                charBuffer = new char[(int)stream.Length];
            }
            catch (Exception e) when (e is OverflowException || e is OutOfMemoryException)
            {
                throw new BuildXLException(
                    "Failed to read content from stream",
                    new FileContentException(
                        $"Failed to create buffer for reading content from stream. Max buffer size is {int.MaxValue} bytes, but stream length is {stream.Length} bytes.",
                        e));
            }

            int actualLength;
            const int DefaultBufferSize = 1024;

            // Need to use the following 'verbose' overload to prevent StreamReader from closing the stream.
            using (var sr = new StreamReader(stream, leaveOpen: true, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: DefaultBufferSize))
            {
                actualLength = await sr.ReadAsync(charBuffer, 0, (int)stream.Length);
            }

            stream.Position = initialPosition;

            return new FileContent(charBuffer, actualLength);
        }

        /// <summary>
        /// Reads a FileContent from a string
        /// </summary>
        public static FileContent ReadFromString(string str)
        {
            return new FileContent(str.ToCharArray(), str.Length);
        }

        /// <summary>
        /// Specific exception when reading file content.
        /// </summary>
        public class FileContentException : Exception
        {
            /// <summary>
            /// Creates an instance of <see cref="FileContentException"/>.
            /// </summary>
            public FileContentException(string message, Exception inner = null)
                : base(message, inner)
            {
            } 
        }

    }
}
