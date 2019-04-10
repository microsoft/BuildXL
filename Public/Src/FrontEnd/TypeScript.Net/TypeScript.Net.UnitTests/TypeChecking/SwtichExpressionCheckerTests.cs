// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Diagnostics;
using Xunit;

namespace TypeScript.Net.UnitTests.TypeChecking
{
    // Mainly used for debuging issues.
    public sealed class SwitchExpressionCheckerTests
    {
        [Fact]
        public void SuccessOnMatchingTypes()
        {
            string code =
                @"let result: number = 'a' switch {
   'b': 1,
   'c': 0
};
";
            ExpectNoErrors(code);
        }

        [Fact]
        public void StringDoesNotMatchBoolean()
        {
            string code =
                @"let result: number = 'a' switch {
   'b': '1',
};
";
            ExpectOneError(code, "Type 'string' is not assignable to type 'number'");
        }

        [Fact]
        public void ProperUnionDoesNotMatchBoolean()
        {
            string code =
                @"let result: number = 'a' switch {
   'b': '1',
   'c': true,
   'd': '2',
};
";
            ExpectOneError(code, "Type 'string | boolean' is not assignable to type 'number'");
        }

        [Fact]
        public void OnlyAllowSwitchOnStringOrNumber()
        {
            string code =
                @"let result: number = true switch {
   'b': 0,
   'c': 1,
   'd': 2,
};
";
            ExpectOneError(code, "Switch expression only supports checking on string values. Type 'boolean' is not");
        }

        [Fact]
        public void SwitchOnStringLiteralAllowed()
        {
            string code =
                @"
let x : 'A' = 'A'
let result: number = x switch {
   'b': 0,
   'c': 1,
   'd': 2,
};
";
            ExpectNoErrors(code);
        }

        [Fact]
        public void SwitchOnStringAllowed()
        {
            string code =
                @"
let x : string = 'A'
let result: number = x switch {
   'b': 0,
   'c': 1,
   'd': 2,
};
";
            ExpectNoErrors(code);
        }
        
        [Fact]
        public void SwitchOnUnionStringLiteralAllowed()
        {
            string code =
                @"
let x : 'A' | string = 'A'
let result: number = x switch {
   'b': 0,
   'c': 1,
   'd': 2,
};
";
            ExpectNoErrors(code);
        }

        [Fact]
        public void SwitchWithNumber()
        {
            string code =
                @"
let result: number = 1 switch {
   1: 0,
   2: 1,
   3: 2,
};
";
            ExpectNoErrors(code);
        }

        [Fact]
        public void NoClauses()
        {
            string code =
                @"
let result = 'a' switch {};
";
            ExpectOneError(code, "Switch expression must have at least one clause.");
        }

        private static void ExpectNoErrors(string code)
        {
            var diagnostics = GetSemanticDiagnostics(code);

            if (diagnostics.Count != 0)
            {
                string message = $"Expected no errors but got {diagnostics.Count}.\r\n{DiagnosticMessages(diagnostics)}";
                CustomAssert.Fail(message);
            }
        }

        private static void ExpectOneError(string code, string expectedErrorMessage)
        {
            var diagnostics = GetSemanticDiagnostics(code);

            var error = diagnostics.First();
            Assert.Contains(expectedErrorMessage, error.MessageText.ToString());
        }

        private static string DiagnosticMessages(IEnumerable<Diagnostic> diagnostics)
        {
            return string.Join(Environment.NewLine, diagnostics.Select(d => d.MessageText.ToString()));
        }

        private static List<Diagnostic> GetSemanticDiagnostics(string code)
        {
            return TypeCheckingHelper.GetSemanticDiagnostics(useCachedVersion: false, codes: code);
        }
    }
}
