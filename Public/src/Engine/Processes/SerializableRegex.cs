// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text.RegularExpressions;

namespace BuildXL.Processes
{
    /// <summary>
    /// Serializable representation of a Regex
    /// </summary>
    public sealed class SerializableRegex
    {
        /// <summary>
        /// The pattern for the Regex
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// The constructed Regex
        /// </summary>
        public Regex Regex { get; }

        /// <summary>
        /// Creates a new instance of the class
        /// </summary>
        /// <param name="pattern">The regular expression pattern to match.</param>
        /// <param name="options">A bitwise combination of the enumeration values that modify the regular expression.</param>
        public SerializableRegex(string pattern, RegexOptions options = RegexOptions.None)
        {
            Pattern = pattern;
            Regex = new Regex(pattern, options);
        }

        /// <summary>
        /// Serializes the Regex to a BinaryWriter
        /// </summary>
        /// <param name="writer">A BinaryWriter</param>
        public void Write(BinaryWriter writer)
        {
            writer.Write(Pattern);
            writer.Write((int)Regex.Options);
        }

        /// <summary>
        /// Deserializes the Regex from a BinaryReader
        /// </summary>
        /// <param name="reader">A BinaryReader</param>
        /// <returns>The Regex read from the reader</returns>
        public static SerializableRegex Read(BinaryReader reader)
        {
            string pattern = reader.ReadString();
            int options = reader.ReadInt32();
            return new SerializableRegex(pattern, (RegexOptions)options);
        }
    }
}
