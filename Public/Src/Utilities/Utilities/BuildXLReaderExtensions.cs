// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
