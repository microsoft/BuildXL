// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGen
{
    internal sealed class ErrorReport
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
            Console.Error.WriteLine(format, args);
            Interlocked.Increment(ref m_errors);
        }

        /// <summary>
        /// Reports an error
        /// </summary>
        public void ReportError(ISymbol symbol, string errorFormat, params object[] args)
        {
            Interlocked.Increment(ref m_errors);

            string error = string.Format(CultureInfo.InvariantCulture, errorFormat, args);
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
    }
}
