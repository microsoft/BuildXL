// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling.ConfigurableRules
{
    public class TestEnforceNoGlobRule : DsTest
    {
        public TestEnforceNoGlobRule(ITestOutputHelper output) : base(output)
        {}

        [Theory]
        [InlineData(@"const r = glob(d`dirs`, ""*"");")]
        [InlineData(@"const r = globR(d`.`, ""*.cs"");")]
        [InlineData(@"const r = globRecursively(d`.`);")]
        [InlineData(@"const r = globFolders(d`.`);")]

        // If functions are in different namespaces they should be allowed. We don't know whether they're real glob functions or not
        public void FailOnKnownGlobFunctions(string code)
        {
            string configuration = @"
config({
  frontEnd: {
    enabledPolicyRules: [""NoGlob""],
  }
});";

            var result = Build()
                .LegacyConfiguration(configuration)
                .AddSpec(code)
                .ParseWithDiagnosticId(LogEventId.GlobFunctionsAreNotAllowed);
        }

        [Theory]

        // If functions are in different namespaces they should be allowed. We don't know whether they're real glob functions or not
        [InlineData("globRecursively")]
        [InlineData("globFolders")]
        public void ShouldNotFailOnUnknownGlobFunctions(string globFunctionName)
        {
            string configuration = @"
config({
  frontEnd: {
    enabledPolicyRules: [""NoGlob""],
  }
});";
            string spec = @"
namespace Foo {
    export function " + globFunctionName + @"(root: Directory): File[] {}
}

export const r = Foo." + globFunctionName + @"(d`.`);
";

            Build()
                .LegacyConfiguration(configuration)
                .AddSpec(spec)
                .ParseWithNoErrors();
        }
    }
}
