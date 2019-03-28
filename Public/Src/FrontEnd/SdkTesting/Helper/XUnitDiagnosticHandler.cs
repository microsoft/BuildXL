// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using BuildXL.Utilities.Instrumentation.Common;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif
using Xunit.Abstractions;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <nodoc />
    public sealed class XUnitDiagnosticHandler
    {
        /// <nodoc />
        private readonly ITestOutputHelper m_output;

        /// <nodoc />
        private readonly StringBuilder m_stringBuilder;

        /// <nodoc />
        public string AllErrors => m_stringBuilder.ToString();

        /// <nodoc />
        public XUnitDiagnosticHandler(ITestOutputHelper output)
        {
            m_output = output;
            m_stringBuilder = new StringBuilder();
        }

        /// <nodoc />
        public void HandleDiagnostic(Diagnostic diagnostic)
        {
            m_output.WriteLine(diagnostic.FullMessage);
            if (diagnostic.Level == EventLevel.Critical || diagnostic.Level == EventLevel.Error)
            {
                m_stringBuilder.AppendLine(diagnostic.FullMessage);
            }
        }
    }
}
