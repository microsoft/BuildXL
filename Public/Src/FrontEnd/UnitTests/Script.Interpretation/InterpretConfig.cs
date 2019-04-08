// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretConfig : DsTest
    {
        public InterpretConfig(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ReportErrorOnConfigurationDeclarationInTheSourceFile()
        {
            string code = @"
config({resolvers: []});

const x = 42;";
            var result = EvaluateWithFirstError(code);

            Assert.Equal((int) LogEventId.ConfigurationDeclarationIsOnlyInConfigurationFile, result.ErrorCode);
        }

        [Fact]
        public void UnparsableConfigurationShouldEmitSpecialError()
        {
            string configuration = @"asfdas `sdf {dfsa";

            string code = @"let p: string = ""foo"";";

            var result = Build()
                .LegacyConfiguration(configuration)
                .AddSpec("build.dsc", code)
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.ConfigurationParsingFailed);
        }

        [Theory]
        [InlineData(@"
config({
    modules: [],
    packages: []
});")]
        [InlineData(@"
config({
    resolvers: [
        {
            kind: ""SourceResolver"",
            modules: [],
            packages: [],
        },
    ],
});")]
        public void ModulesAndPackagesCannotBeSpecifiedSimultaneously(string config)
        {
            Build()
                .Configuration(config)
                .AddSpec("build.dsc", "export const x = 42;")
                .EvaluateWithDiagnosticId(LogEventId.CannotUsePackagesAndModulesSimultaneously);
        }

        [Theory]
        [InlineData(@"
config({
    modules: [f`module.config.bm`, f`" + SpecEvaluationBuilder.PreludePackageConfigRelativePathDsc + @"`]
});")]
        [InlineData(@"
config({
    resolvers: [
        {
            kind: ""SourceResolver"",
            modules: [f`module.config.bm`, f`" + SpecEvaluationBuilder.PreludePackageConfigRelativePathDsc + @"`],
        },
    ],
});")]
        public void ModulesFieldCanBeSpecified(string config)
        {
            Build()
                .Configuration(config)
                .AddSpec("module.config.bm", @"module({name: ""aModule""});")
                .AddSpec("build.dsc", "export const x = 42;")
                .RootSpec("build.dsc")
                .EvaluateWithNoErrors();
        }
    }
}
