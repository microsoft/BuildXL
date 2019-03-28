// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling.ConfigurableRules
{
    public class TestEnforceNoTransformersRule : SemanticBasedTests
    {
        private const string Configuration = @"
config({
  frontEnd: {
    enabledPolicyRules: [""NoTransformers""],
  }
});";
        private const string OldTransformersFacade = @"
namespace Transformer {
    export interface Version {
        major: number,
    }; 
    
    export function execute(args:Version): Version {
        return undefined;
    }
}
";

        public TestEnforceNoTransformersRule(ITestOutputHelper output) : base(output)
        {}

        [Theory]
        [InlineData(@"const r: Transformer.Version = undefined;")]
        [InlineData(@"const r = Transformer.execute(undefined);")]
        public void FailedIfTransformerIsUsed(string code)
        {
            var result = BuildWithoutDefautlLibraries()
                .AddPrelude()
                .AddExtraPreludeSpec(OldTransformersFacade)
                .Configuration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithFirstError();

            Assert.Equal((int)LogEventId.AmbientTransformerIsDisallowed, result.ErrorCode);
        }

        [Fact]
        public void CustomTransformerInterfaceShouldWork()
        {
            string code = @"
export namespace Transformer {
   export interface Version {n: number;}
}

const r: Transformer.Version = undefined;";

            var result = BuildWithoutDefautlLibraries()
                .AddPrelude()
                .AddExtraPreludeSpec(OldTransformersFacade)
                .Configuration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithNoErrors();
        }
    }
}
