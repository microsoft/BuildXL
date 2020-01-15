// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Extensions for <see cref="BuildXLReader"/>.
    /// </summary>
    public static class BuildXLReaderExtensions
    {
        /// <summary>
        /// Reads a string whose result can be null.
        /// </summary>
        public static string ReadNullableString(this BuildXLReader reader)
        {
            return reader.ReadNullable(r => r.ReadString());
        }
    }
}
