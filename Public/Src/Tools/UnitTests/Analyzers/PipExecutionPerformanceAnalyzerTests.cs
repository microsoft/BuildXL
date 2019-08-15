// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Execution.Analyzer;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PipExecutionPerformanceAnalyzerTests : AnalyzerTestBase
    {
        public PipExecutionPerformanceAnalyzerTests(ITestOutputHelper output) : base(output)
        {
           
        }

        [Fact]
        public void QuoteEscapeValidation()
        {
            char quote = '"';
            char slash = '\\';
            Assert.True(("" + slash + slash).Equals(PipExecutionPerformanceAnalyzer.NormalizeString("" + slash)));
            Assert.True(("" + slash + quote).Equals(PipExecutionPerformanceAnalyzer.NormalizeString("" + quote)));
            Assert.True(("" + slash + slash + slash + quote).Equals(PipExecutionPerformanceAnalyzer.NormalizeString("" + slash + quote)));
            Assert.True(("" + slash + slash + slash + slash + slash + quote).Equals(PipExecutionPerformanceAnalyzer.NormalizeString("" + slash + slash + quote)));
        }

    }
}
