// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.PipGraphFragmentGenerator.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    /// <remarks>
    /// Assembly reserved range 7200 - 7499
    /// </remarks>
    public enum LogEventId : ushort
    {
        None = 0,

        ErrorParsingFilter = 7501,
        GraphFragmentExceptionOnSerializingFragment = 7502,
        GraphFragmentSerializationStats = 7503
        // Max: 7600.
    }
}
