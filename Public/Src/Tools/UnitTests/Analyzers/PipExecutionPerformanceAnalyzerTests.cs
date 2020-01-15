// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Execution.Analyzer;
using Test.BuildXL.TestUtilities.Xunit;
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
            XAssert.AreEqual($"{slash}{slash}", PipExecutionPerformanceAnalyzer.NormalizeString($"{slash}"));
            XAssert.AreEqual($"{slash}{quote}", PipExecutionPerformanceAnalyzer.NormalizeString($"{quote}"));
            XAssert.AreEqual($"{slash}{slash}{slash}{quote}", PipExecutionPerformanceAnalyzer.NormalizeString($"{slash}{quote}"));
            XAssert.AreEqual($"{slash}{slash}{slash}{slash}{slash}{quote}", PipExecutionPerformanceAnalyzer.NormalizeString($"{slash}{slash}{quote}"));
        }

    }
}