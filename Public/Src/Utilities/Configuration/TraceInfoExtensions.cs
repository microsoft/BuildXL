// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The case insensitive names of various /traceInfo arguments that are passed to BuildXL
    /// </summary>
    public static class TraceInfoExtensions
    {
        /// <summary>
        /// The argument to specify the branch name in CB.
        /// </summary>
        public const string Branch = "Branch";

        /// <summary>
        /// The argument to specify the queue name in CB.
        /// </summary>
        public const string CloudBuildQueue = "CloudBuildQueue";

        /// <summary>
        /// The argument to specify the AB Testing.
        /// </summary>
        public const string ABTesting = "ABTesting";
    }
}
