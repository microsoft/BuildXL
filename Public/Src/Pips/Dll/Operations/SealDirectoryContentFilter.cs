// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Filter that is used during the construction of a composite shared opaque directories.
    /// </summary>
    public struct SealDirectoryContentFilter
    {
        /// <summary>
        /// A string representation of the regex used for filtering.
        /// </summary>
        public string Regex { get; }

        /// <summary>
        /// Whether to include or exclude files that match the regex.
        /// </summary>
        public ContentFilterKind Kind { get; }

        /// <nodoc />
        public enum ContentFilterKind : byte
        {
            /// <nodoc />
            Include,

            /// <nodoc />
            Exclude
        }

        /// <nodoc/>
        public SealDirectoryContentFilter(ContentFilterKind kind, string regex)
        {
            Kind = kind;
            Regex = regex;
        }
    }
}
