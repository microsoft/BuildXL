// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    }
}
