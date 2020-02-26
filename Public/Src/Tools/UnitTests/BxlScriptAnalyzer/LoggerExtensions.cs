// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using System.Linq;
using Logger = BuildXL.FrontEnd.Script.Analyzer.Tracing.Logger;

namespace Test.Tool.DScript.Analyzer
{
    public static class LoggerExtensions
    {
        /// <nodoc />
        public static bool HasErrors(this Logger logger)
        {
            return logger.CapturedDiagnostics.Any(diagnostic => diagnostic.Level == EventLevel.Error);
        }

        /// <nodoc />
        public static int ErrorCount(this Logger logger)
        {
            return logger.CapturedDiagnostics.Count(diagnostic => diagnostic.Level == EventLevel.Error);
        }
    }
}
