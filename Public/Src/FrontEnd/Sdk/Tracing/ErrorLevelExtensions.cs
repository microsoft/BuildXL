// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;

#endif

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
