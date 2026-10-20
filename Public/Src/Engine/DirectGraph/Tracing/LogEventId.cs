// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Engine.DirectGraph.Tracing
{
#pragma warning disable 1591

    /// <summary>
    /// Event identifiers for DirectGraph.
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // Reserved 12900 .. 12999 for DirectGraph.
        GraphOperationReceived = 12900,
        GraphOperationResponse = 12901,
        GraphBuilderToolFailed = 12902,
        GraphBuilderToolExitedWithoutCompletion = 12903,
        GraphBuilderToolWarning = 12904,
        GraphOperationRejected = 12905,
        GraphBuilderToolTimedOut = 12906,
        GraphBuilderToolNotFound = 12907,
    }
}
