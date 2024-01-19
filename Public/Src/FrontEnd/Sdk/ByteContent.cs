// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Represents binary content of a file.
    /// </summary>
    public readonly record struct ByteContent
    {
        /// <summary>
        /// Invalid instance.
        /// </summary>
        public static ByteContent Invalid { get; } = default(ByteContent);

        /// <summary>
        /// Content of a file.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] Content { get; }

        /// <summary>
        /// Length of a content.
        /// </summary>
        /// <remarks>
        /// This type can wrap an array from a pool, meaning that <see cref="Content"/> can be just partially filled with data.
        /// </remarks>
        public int Length { get; }

        /// <nodoc />
        public bool IsValid => Content != null;

        /// <nodoc />
        private ByteContent(byte[] content, int length)
        {
            Contract.Requires(content != null);
            Contract.Requires(length >= 0);
            Contract.Requires(length <= content.Length);

            Content = content;
            Length = length;
        }

        /// <nodoc />
        public static ByteContent Create(byte[] content, long length)
        {
            return new ByteContent(content, (int)length);
        }
    }
}
