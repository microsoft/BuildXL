// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utilities for warnings.
    /// </summary>
    public static class Warning
    {
        /// <summary>
        /// Adapted from Microsoft.BUild.Utilities.Core / CanonicalError.cs
        /// </summary>
        public const string DefaultWarningPattern =

            // Beginning of line and any amount of whitespace.
            @"^\s*"

            // Match a [optional project number prefix 'ddd>'], single letter + colon + remaining filename, or
            // string with no colon followed by a colon.
            + @"((((((\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)"

            // Origin may also be empty. In this case there's no trailing colon.
            + "|())"

            // Match the empty string or a string without a colon that ends with a space
            + "(()|([^:]*? ))"

            // Match 'warning'.
            + @"warning"

            // Match anything starting with a space that's not a colon/space, followed by a colon.
            // Error code is optional in which case "warning" can be followed immediately by a colon.
            + @"( \s*([^: ]*))?\s*:"

            // Whatever's left on this line, including colons.
            + ".*$";
    }
}
