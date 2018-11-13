// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utility functions to aid static code analysis.
    /// </summary>
    public static class Analysis
    {
        /// <summary>
        /// Indicate that the return value of a function is explicitly being ignored.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "ignoredResult")]
        public static void IgnoreResult<T>(T ignoredResult)
        {
        }

        /// <summary>
        /// Indicate that a method argument is intentionally not used.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "ignoredArgument")]
        public static void IgnoreArgument<T>(T ignoredArgument)
        {
        }
    }
}
