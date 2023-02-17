// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Core
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
        public static void IgnoreResult<T>(T ignoredResult, string justification = null)
        {
        }

        /// <summary>
        /// Indicate that a method argument is intentionally not used.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "ignoredArgument")]
        public static void IgnoreArgument<T>(T ignoredArgument, string justification = null)
        {
        }

        /// <summary>
        /// Indicates that an exception block is intentionally not handlign exceptions
        /// </summary>
        public static void IgnoreException(string justification)
        {
        }
    }
}
