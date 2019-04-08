// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidPropertyAccessOnQualifierRule : SemanticBasedTests
    {
        public TestForbidPropertyAccessOnQualifierRule(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void CurrentQualifierCannotHaveNamespaceQualifications()
        {
            var code = @"
namespace A {
    namespace B {
        export declare const qualifier : {};
    }
    export const x = B.qualifier;
}
";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.CurrentQualifierCannotBeAccessedWithQualifications);
        }
    }
}
