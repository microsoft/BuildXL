// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner.Vsts;

namespace AdoBuildRunner.Vsts
{
    /// <summary>
    /// Custom exception type to be thrown when encountering failing conditions in the build coordination stages
    /// </summary>
    public class CoordinationException : Exception
    {
        /// <inheritdoc />
        public CoordinationException(string message) : base(message) { }

        /// <summary>
        /// Logs an error message in the logger and throws a CoordinationException with the same message
        /// </summary>
        public static void LogAndThrow(ILogger logger, string message)
        {
            logger?.Error(message);
            throw new CoordinationException(message);
        }
    }
}
