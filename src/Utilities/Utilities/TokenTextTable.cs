// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
