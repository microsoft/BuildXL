// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Analyzer.Tracing
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

        ErrorParsingFile = 7200,
        ErrorParsingFilter = 7201,
        ErrorFilterHasNoMatchingSpecs = 7202,
        FixRequiresPrettyPrint = 7203,
        AnalysisErrorSummary = 7204,

        // PrettyPrint
        PrettyPrintErrorWritingSpecFile = 7210,
        PrettyPrintUnexpectedChar = 7211,
        PrettyPrintExtraTargetLines = 7212,
        PrettyPrintExtraSourceLines = 7213,

        // LegacyLiteralCration
        LegacyLiteralFix = 7220,

        // PathFix
        PathFixerIllegalPathSeparator = 7225,
        PathFixerIllegalCasing = 7226,

        // Documentation
        DocumentationMissingOutputFolder = 7230,
        DocumentationErrorCleaningFolder = 7231,
        DocumentationErrorCreatingOutputFolder = 7232,
        DocumentationSkippingV1Module = 7233,

        // Graph fragment
        GraphFragmentMissingOutputFile =  7240,
        GraphFragmentInvalidOutputFile = 7241,
        GraphFragmentMissingGraph = 7242,
        GraphFragmentExceptionOnSerializingFragment = 7243,
        GraphFragmentSerializationStats = 7244,

        // Max: 7499
    }
}
