// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceSimplifiedForRule : DsTest
    {
        public TestEnforceSimplifiedForRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestInvalidMultipleInitializers()
        {
            string spec =
@"namespace Test {
    function f() {
      for (let test = 1, test1 = 2 ; test < index; test += 1) {
      }
    }
}
";
            ParseWithDiagnosticId(spec, LogEventId.InvalidForVarDeclarationInitializer);
        }

        [Fact]
        public void TestInvalidSingleNotInitialized()
        {
            string spec =
@"namespace Test {
    function f() {
      for (let test; test < index; test += 1) {
      }
    }
}
";
            ParseWithDiagnosticId(spec, LogEventId.VariableMustBeInitialized);
        }

        [Fact]
        public void TestInvalidAbsentIncrementor()
        {
            string spec =
@"namespace Test {
    function f() {
      for (let test = 1 ; test < index; ) {
      }
    }
}
";
            ParseWithDiagnosticId(spec, LogEventId.ForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement);
        }

        [Fact]
        public void TestInvalidForIncrementorIsNotAssignment()
        {
            string spec =
@"namespace Test {
    function f() {
      for (let test = 1 ; test < index; 1 < 2) {
      }
    }
}
";
            ParseWithDiagnosticId(spec, LogEventId.ForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement);
        }
    }
}
