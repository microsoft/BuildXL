// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class ForbidModuleSelfReferencingRule : SemanticBasedTests
    {
        public ForbidModuleSelfReferencingRule(ITestOutputHelper output)
            : base(output)
        { }

        [Theory]
        [InlineData("import * as foo from 'MyPackage';")]
        [InlineData("export * from 'MyPackage';")]
        [InlineData("export const a = importFrom('MyPackage');")]
        public void SpecCanNotImportTheSameModule(string code)
        {
            var packageConfig = @"
module({
    name: ""MyPackage"",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
});
";
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(Names.PackageConfigDsc, packageConfig)
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.ModuleShouldNotImportItself);
        }
    }
}
