// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Interface describing types that can suggest a value to use to access a given file.
    /// </summary>
    public interface IValueSuggester
    {
        /// <summary>
        /// Given an X, suggest a value.
        /// </summary>
        string SuggestValue(AbsolutePath path);
    }
}
