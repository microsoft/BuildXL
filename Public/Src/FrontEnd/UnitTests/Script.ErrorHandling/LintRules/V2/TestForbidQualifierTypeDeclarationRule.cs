// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidQualifierTypeDeclarationRule : SemanticBasedTests
    {
        public TestForbidQualifierTypeDeclarationRule(ITestOutputHelper output)
            : base(output)
        { }

        [Theory]
        [InlineData("interface Qualifier{}")]
        [InlineData("type Qualifier = number;")]
        public void ReservedQualifierTypeCannotBeUsed(string declaration)
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", declaration)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierTypeNameIsReserved);
        }
    }
}
