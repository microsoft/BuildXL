// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
