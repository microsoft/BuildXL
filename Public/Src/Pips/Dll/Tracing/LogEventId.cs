// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Pips.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        FailedToAddFragmentPipToGraph = 5048,
        ExceptionOnAddingFragmentPipToGraph = 5049,
        ExceptionOnDeserializingPipGraphFragment = 5050,
        DeserializationStatsPipGraphFragment = 5051,
    }
}
