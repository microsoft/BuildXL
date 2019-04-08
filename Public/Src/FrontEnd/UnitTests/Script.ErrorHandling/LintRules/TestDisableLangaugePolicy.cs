// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestDisableLangaugePolicy : DsTest
    {
        public TestDisableLangaugePolicy(ITestOutputHelper output)
            : base(output)
        {}

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var baseConfiguration = base.GetFrontEndConfiguration(isDebugged: false);
            baseConfiguration.DisableLanguagePolicyAnalysis = true;
            return baseConfiguration;
        }

        [Fact]
        public void ShouldNotGetWarningOnMissingSemicolonBecauseOptionalRuleIsDisabled()
        {
            string code =
@"function foo() {
    let x = 42;
}";
            Build().AddSpec("build.dsc", code).ParseWithNoErrors();
        }

        [Fact]
        public void ShouldNotGetWarningOnCamelCasedConstantBecauseSemanticInformationIsBeingUsed()
        {
            string code =
@"const Value = 42;";
            Build().AddSpec("build.dsc", code).ParseWithNoErrors();
        }
    }
}
