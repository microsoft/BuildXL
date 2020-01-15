// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Optimized table of token text strings.
    /// </summary>
    public sealed class TokenTextTable : StringTable
    {
        /// <summary>
        /// Initializes a new token text table.
        /// </summary>
        public TokenTextTable()
        {
        }

        /// <summary>
        /// Deserializes the TokenTextTable
        /// </summary>
        internal static TokenTextTable Deserialize(BuildXLReader reader)
        {
            return new TokenTextTable(ReadSerializationState(reader));
        }

        private TokenTextTable(SerializedState state)
            : base(state)
        {
        }
    }
}
