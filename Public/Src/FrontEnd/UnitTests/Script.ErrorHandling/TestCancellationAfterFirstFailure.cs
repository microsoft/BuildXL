// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public sealed class TestCancellationAfterFirstFailure : DsTest
    {
        public TestCancellationAfterFirstFailure(ITestOutputHelper output) : base(output)
        {
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var result = base.GetFrontEndConfiguration(isDebugged);
            result.CancelEvaluationOnFirstFailure = true;
            return result;
        }

        [Fact]
        public void TestEarlyCancellation()
        {
            const string BuggySpec = "export const x = [][3];";
            var testResult = EvaluateSpec(BuggySpec, expressions: new string[0]);
            XAssert.IsTrue(testResult.HasError);
            var diagnostics = CaptureEvaluationDiagnostics();
            AssertDiagnosticIdExists(diagnostics, LogEventId.ArrayIndexOufOfRange);
            AssertDiagnosticIdExists(diagnostics, LogEventId.EvaluationCancellationRequestedAfterFirstFailure);
        }
    }
}
