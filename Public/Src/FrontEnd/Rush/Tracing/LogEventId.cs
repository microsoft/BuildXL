// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Rush.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11700 .. 11800 for Rush front-end
        InvalidRushResolverSettings = 11700,
        UsingRushLibBaseAt = 11701,
    }
}
