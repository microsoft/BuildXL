// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidLabelRule : DsTest
    {
        public TestForbidLabelRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestLabelsAreNotAllowed()
        {
            string code = @"
aLabel:
for(let x in [1]) {
   continue aLabel;
}";
            ParseWithDiagnosticId(code, LogEventId.LabelsAreNotAllowed);
        }
    }
}
