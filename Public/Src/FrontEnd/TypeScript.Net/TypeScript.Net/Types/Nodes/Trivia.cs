// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JetBrains.Annotations;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Stores the trivia information for an associated node
    /// </summary>
    public sealed class Trivia
    {
        /// <summary>
        /// The number of whitespace newlines in the trivia for the associated node
        /// </summary>
        public int LeadingNewLineCount { get; set;  }

        /// <summary>
        /// Leading comments.
        /// </summary>
        /// <remarks>
        /// When None are present this is null
        /// </remarks>
        [CanBeNull]
        public Comment[] LeadingComments { get; set; }

        /// <summary>
        /// Trailing comments.
        /// </summary>
        /// <remarks>
        /// When None are present this is null
        /// </remarks>
        [CanBeNull]
        public Comment[] TrailingComments { get; set; }

        /// <summary>
        /// A comment structure
        /// </summary>
        public readonly struct Comment
        {
            /// <summary>
            /// Returns true if this is a /* */ style comment
            /// </summary>
            public bool IsMultiLine { get; }

            /// <summary>
            /// Returns true if this is a // style comment
            /// </summary>
            public bool IsSingleLine => !IsMultiLine;

            /// <summary>
            /// The comment including the markers /*, */ and //
            /// </summary>
            public string Content { get; }

            /// <nodoc />
            public Comment(string content, bool isMultiLine)
            {
                IsMultiLine = isMultiLine;
                Content = content;
            }
        }
    }
}
