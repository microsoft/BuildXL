// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Available DScript analyzers
    /// </summary>
    public enum AnalyzerKind
    {
        /// <nodoc />
        PrettyPrint,

        /// <nodoc />
        LegacyLiteralCreation,

        /// <nodoc />
        PathFixer,

        /// <nodoc />
        Documentation,

        /// <nodoc />
        Codex
    }
}
