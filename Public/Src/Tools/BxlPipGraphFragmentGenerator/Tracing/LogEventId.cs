// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        GraphFragmentSerializationStats = 7503,
        UnhandledFailure = 7504,
        UnhandledInfrastructureError = 7505,
        // Max: 7600.
    }
}
