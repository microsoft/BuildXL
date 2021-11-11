// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGen.Core
{
    /// <summary>
    /// Error report for log generation failures.
    /// </summary>
    public class ErrorReport
    {
        private int m_errors = 0;

        /// <summary>
        /// Count of errors reported
        /// </summary>
        public int Errors => m_errors;

        /// <summary>
        /// Reports an error
        /// </summary>
        public void ReportError(string format, params object[] args)
        {
            Interlocked.Increment(ref m_errors);
            ReportErrorCore(format, args);
        }

        /// <nodoc />
        protected virtual void ReportErrorCore(string format, params object[] args) => Console.Error.WriteLine(format, args);

        /// <summary>
        /// Reports an error
        /// </summary>
        public void ReportError(ISymbol symbol, string errorFormat, params object[] args)
        {
            Interlocked.Increment(ref m_errors);

            ReportErrorCore(symbol, errorFormat, args);
        }

        /// <nodoc />
        protected virtual void ReportErrorCore(ISymbol symbol, string errorFormat, params object[] args)
        {
            string error = SafeFormat(errorFormat, args);
            if (symbol.Locations.Length > 0)
            {
                var lineSpan = symbol.Locations[0].SourceTree.GetLineSpan(symbol.Locations[0].SourceSpan);

                Console.Error.WriteLine("{0}({1},{2}): {3}", lineSpan.Path, lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character, error);
            }
            else
            {
                Console.Error.WriteLine("{0}.{1} {2}", symbol.ContainingNamespace, symbol.Name, error);
            }
        }

        /// <nodoc />
        protected static string SafeFormat(string format, params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}
