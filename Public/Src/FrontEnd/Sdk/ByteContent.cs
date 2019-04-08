// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Sdk
{
#pragma warning disable CA1815 // Override equals and operator equals on value types
    /// <summary>
    /// Represents binary content of a file.
    /// </summary>
    public readonly struct ByteContent
#pragma warning restore CA1815 // Override equals and operator equals on value types
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
