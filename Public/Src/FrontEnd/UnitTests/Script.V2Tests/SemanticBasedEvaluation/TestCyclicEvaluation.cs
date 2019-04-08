// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Workspaces
{
    public class TestCyclicEvaluation : SemanticBasedTests
    {
        public TestCyclicEvaluation(ITestOutputHelper output) : base(output)
        {
        }

        private SpecEvaluationBuilder BuildForCycleDetection()
        {
            return BuildWithPrelude(@"
config({
    frontEnd: {
        cycleDetectorStartupDelay: 0,
    },
});");
        }

        [Fact]
        public void CycleBetweenInternalValuesIsDetected()
        {
            var result = BuildForCycleDetection()
               .AddSpec("spec1.dsc", "export const a = b;")
               .AddSpec("spec2.dsc", "export const b = a;")
               .RootSpec("spec1.dsc")
               .EvaluateWithFirstError();

            Assert.Equal(LogEventId.Cycle, (LogEventId)result.ErrorCode);
        }

        /// <summary>
        /// A full extent build is requested, so evaluation starts from two different thunks, with different context trees
        /// </summary>
        [Fact]
        public void CycleBetweenValuesIsDetectedFromMultipleContextTrees()
        {
            var result = BuildForCycleDetection()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/spec1.dsc", "export const a = b;")
                .AddSpec(@"APackage/spec2.dsc", "export const b = a;")
                .RootSpec("config.dsc")
                .EvaluateWithFirstError();

            Assert.Equal(LogEventId.Cycle, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void CycleBetweenPublicValuesIsDetected()
        {
            var result = BuildForCycleDetection()
               .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
               .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
               .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = importFrom(""BPackage"").y;")
               .AddSpec(@"BPackage/package.dsc", @"
@@public
export const y = importFrom(""APackage"").x;")
               .RootSpec(@"BPackage/package.dsc")
               .EvaluateWithFirstError();

            Assert.Equal(LogEventId.Cycle, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void TwoIndependentCyclesAreDetected()
        {
            var result = BuildForCycleDetection()
               .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
               .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
               .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = importFrom(""BPackage"").y;")
               .AddSpec(@"BPackage/package.dsc", @"
@@public
export const y = importFrom(""APackage"").x;")
               .RootSpec(@"BPackage/package.dsc")
               .EvaluateWithFirstError();

            Assert.Equal(LogEventId.Cycle, (LogEventId)result.ErrorCode);
        }

        /// <summary>
        /// This induces a chain x{86} -> y{86} -> x{64} -> y{64}, which is not a cycle.
        /// </summary>
        [Fact]
        public void CycleIsNotPresentWhenQualifiersAreDifferent()
        {
            var result = BuildForCycleDetection()
               .AddSpec("spec1.dsc", @"
export declare const qualifier: {platform: ""x86"" | ""amd64""};

export const x = y;")

                .AddSpec("spec2.dsc", @"
export const y = qualifier.platform === ""x86"" ?
                    $.withQualifier({platform: ""amd64""}).x :
                    1;
")
               .Qualifier(@"{platform: ""x86""}")
               .RootSpec("spec1.dsc")
               .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(1, result);
        }
    }
}
