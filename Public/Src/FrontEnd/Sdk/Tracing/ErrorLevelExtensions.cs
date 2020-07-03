// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;

namespace BuildXL.FrontEnd.Sdk.Tracing
{
    /// <summary>
    /// Set of extension methods for <see cref="EventLevel" /> enumeration.
    /// </summary>
    public static class ErrorLevelExtensions
    {
        /// <summary>
        /// Returns true when <paramref name="eventLevel" /> represents an error level.
        /// </summary>
        public static bool IsError(this EventLevel eventLevel)
        {
            return eventLevel == EventLevel.Critical || eventLevel == EventLevel.Error;
        }
    }
}
