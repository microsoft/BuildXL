// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Extensions for <see cref="BuildXLWriter"/>
    /// </summary>
    public static class BuildXLWriterExtensions
    {
        /// <summary>
        /// Writes a string whose value can be <code>null</code>.
        /// </summary>
        public static void WriteNullableString(this BuildXLWriter writer, string canBeNullString)
        {
            writer.Write(canBeNullString, (w, v) => w.Write(v));
        }
    }
}
