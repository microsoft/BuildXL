// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestSpecConfigFilesAreValidated : DsTest
    {
        public TestSpecConfigFilesAreValidated(ITestOutputHelper output) : base(output)
        { }

        [Theory]
        [InlineData("configFile.dsc")]
        [InlineData("configFile.bc")]
        [InlineData("configFile.bl")]
        public void ValidateDisallowedAmbientAccessInConfigString(string configurationFileName)
        {
            // Here we just pick a random error code that we know applies to spec config files.
            // What we want to validate is that extra configuration files are actually validated, we are not after this specific error.
            var result = Build()
                .LegacyConfiguration($"config({{ qualifiers: {{ defaultQualifier: {{ platform: importFile(f`{configurationFileName}`).platform, configuration: \"foo\", targetFramework: \"foo\" }}, }} }});")
                .AddFile(configurationFileName,
@"
export const platform = 'win';
function foo() {
    // Binding patterns are dissallowed.
    for (const [key, value] of extraEnvVars) {
    }
}
")
                .EvaluateWithFirstError();

            Assert.Equal((int)LogEventId.ReportBindingPatternInVariableDeclarationIsNowAllowed, result.ErrorCode);
        }
    }
}
