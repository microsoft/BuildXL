// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretPackages : DsTest
    {
        public InterpretPackages(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("package")]
        [InlineData("module")]
        public void ReportErrorOnPackageDeclarationInTheSourceFile(string configurationCall)
        {
            string code = $@"
{configurationCall}({{
    name: ""MyPack""
}});

const x = 42;";
            Build()
                .AddSpec(code)
                .EvaluateWithDiagnosticId(LogEventId.PackageConfigurationDeclarationIsOnlyInConfigurationFile);
        }

        [Fact]
        public void PackageConfigWithEmptyMainShouldNotFail()
        {
            string packageConfig = @"
module({
    name: ""MyPack"",
    main: f``,
});";

            string package = @"
export const r = 42";

            Build()
                .AddSpec(Names.PackageConfigDsc, packageConfig)
                .AddSpec("package.dsc", package)
                .RootSpec("package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.InvalidPathExpression);
        }

        [Fact]
        public void MistypedPackageKeywordShouldGiveAGoodErrorMessage()
        {
            // This was a Bug #468584
            var testWriter = CreateTestWriter("src");
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddPackage("MyPack", @"MyPack/", @"
namespace MyPack
{
    export const x = 12 - 1;
}");
            testWriter.AddExtraFile(@"MyPack/package.config.dsc", @"
pckage({
    name: ""MyPack""
});");

            var sourceResolver = configWriter.AddSourceResolver();
            sourceResolver.AddPackage(@"MyPack");

            configWriter.AddDefaultSourceResolver();

            var result = Evaluate(testWriter, @"MyPack/package.dsc", new[] { "MyPack.x" });
            result.ExpectErrorCode((int)LogEventId.UnknownFunctionCallInPackageConfigurationFile, count: 1);
        }

        [Fact]
        public void PackageConfigurationShouldHaveOnlyOneFunctionInvocation()
        {
            var testWriter = CreateTestWriter("src");
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddPackage("MyPack", @"MyPack/", @"
namespace MyPack
{
    export const x = 12 - 1;
}");
            testWriter.AddExtraFile(@"MyPack/package.config.dsc", @"
module({
    name: ""MyPack""
});

const x = 42;");

            var sourceResolver = configWriter.AddSourceResolver();
            sourceResolver.AddPackage(@"MyPack");

            configWriter.AddDefaultSourceResolver();

            var result = Evaluate(testWriter, @"MyPack/package.dsc", new[] { "MyPack.x" });
            result.ExpectErrorCode((int)LogEventId.UnknownFunctionCallInPackageConfigurationFile, count: 1);
        }

        [Fact]
        public void PackagesSectionWithLambdaExpression()
        {
            string config = @"
config({
    // No orphan projects are owned by this configuration.
    projects: [],

    // Packages that define the build extent.
    modules: [
        d`PackA`,
        d`" + SpecEvaluationBuilder.PreludeDir + @"`
    ].mapMany(d => globR(d, 'package.config.dsc')),
});";

            string packageA = @"
export const x = 42;
";

            string packageConfigA = @"
module({
    name: ""PackageA"",
    projects: []
});

";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec(@"PackA/package.config.dsc", packageConfigA)
                .AddSpec(@"PackA/package.dsc", packageA)
                .RootSpec(@"PackA/package.dsc")
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }

        // V1 packages should emit an error when they have conflicting ownership of main files
        [Fact]
        public void ExpectedFailureWhenMainProjectIsOwnedByMultipleExplicitPackages()
        {
            const string Config = "config({});";

            string packageA = @"
export const x = 42;
";

            string packageConfigA = @"
module({
    name: ""PackB"",
    main: f`./packageA.dsc`,
    projects: [f`./package.dsc`]
});
module({
    name: ""c:/foo/bar$``// sadf/t sadf1 100**"", // just for fun. This name should be valid
    main: f`./packageA.dsc`,
    projects: []
});
";

            Build()
                .LegacyConfiguration(Config)
                .AddSpec("package.config.dsc", packageConfigA)
                .AddSpec("package.dsc", packageA)
                .RootSpec("package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage);
        }

        // V2 packages should emit an error when more than one package owns the same project file.
        [Fact]
        public void ExpectedFailureWhenProjectIsOwnedByMultiplePackages()
        {
            const string Config = @"config({});";
            const string PackageFileName = "package.dsc";

            string packageA = @"
export const x = 42;
";

            string packageConfigA = $@"
module({{
    name: ""PackA"",
    projects: [f`{PackageFileName}`],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences

}});
module({{
    name: ""PackB"",
    projects: [f`{PackageFileName}`],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences
}});
";

            var result = Build()
                .LegacyConfiguration(Config)
                .AddSpec("package.config.dsc", packageConfigA)
                .AddSpec(PackageFileName, packageA)
                .RootSpec(PackageFileName)
                .Evaluate("x");

            result.ExpectErrorCode((int)LogEventId.FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage, count: 1);
            result.ExpectErrorMessageSubstrings(new[] { PackageFileName });
        }

        [Fact]
        public void ExpectedSuccessWhenMainProjectHasMultiplePackages()
        {
            const string Config = @"config({});";

            string packageA = @"
export const x = 42;
";

            string packageConfigA = @"
module({
    name: ""PackA"",
    projects: [f`packageA.dsc`],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences
});
module({
    name: ""PackB"",
    projects: [f`packageB.dsc`],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences
});
";

            var result = Build()
                .LegacyConfiguration(Config)
                .AddSpec(@"package.config.dsc", packageConfigA)
                .AddSpec(@"packageA.dsc", packageA)
                .AddSpec(@"packageB.dsc", packageA)
                .RootSpec(@"packageA.dsc")
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }

        [Fact]
        public void UnparsableConfigurationShouldEmitSpecialError()
        {
            string packageA = @"export const x = 42;";

            string packageConfigA = @"1231 1231``` {{;";

            var result = Build()
                .AddSpec(@"PackA/package.config.dsc", packageConfigA)
                .AddSpec(@"PackA/package.dsc", packageA)
                .RootSpec(@"PackA/package.dsc")
                .Evaluate();

            result.ExpectErrorCode((int)LogEventId.PackageConfigurationParsingFailed, count: 1);
        }

        [Fact]
        public void PackageDefinitionShouldTakeOneArgument()
        {
            string packageA = @"export const x = 42;";

            string packageConfigA = @"module(1,2);";

            Build()
                .AddSpec(@"PackA/package.config.dsc", packageConfigA)
                .AddSpec(@"PackA/package.dsc", packageA)
                .RootSpec(@"PackA/package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.InvalidPackageConfigurationFileFormat);
        }

        [Fact]
        public void ImplicitPackageConfigDoesNotAdmitMainFile()
        {
            string packageConfig = @"
module({
    name: ""MyPack"",
    main: f`package.dsc`,
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
});";

            string package = @"
export const r = 42";

            Build()
                .AddSpec(Names.PackageConfigDsc, packageConfig)
                .AddSpec("package.dsc", package)
                .RootSpec("package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.ImplicitSemanticsDoesNotAdmitMainFile);
        }

        [Fact]
        public void PackageWithoutLocalProjectFileV2()
        {
            const string SpecPath = "NestedFolder/spec1.dsc";
            const string Spec = "export const x = 42;";
            const string PackagePath = "package.config.dsc";

            var result = Build()
                .LegacyConfiguration($"config({{ modules: [f`{PackagePath}`, f`{SpecEvaluationBuilder.PreludePackageConfigRelativePathDsc}`] }});")
                .AddSpec(PackagePath, CreatePackageConfig("MyPackage", true, SpecPath))
                .AddSpec(SpecPath, Spec)
                .EvaluateExpressionWithNoErrors(SpecPath, "x");

            Assert.Equal(42, result);
        }

        [Fact]
        public void TestImportFileInPackageConfig()
        {
            string packageConfig = @"
module({
    name: ""MyPack"",
    main: f`spec1.dsc`,
    projects: [f`spec1.dsc`, ...importFile(f`./dirs.dm`).projects]
});";

            string spec1 = @"
export const r = 42;";

            string spec2 = @"
export const r = 43;";

            string dirs = @"
export const projects = [f`spec2.dsc`];";

            var result =
                Build()
                .AddSpec(Names.PackageConfigDsc, packageConfig)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("dirs.dm", dirs)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc").EvaluateExpressionWithNoErrors("r");

            Assert.Equal(43, result);
        }

        [Fact]
        public void InterpretModulesWithModuleConfigDscConfigs()
        {
            string packageConfig = @"
module({
    name: ""MyPack"",
    projects: [f`spec1.dsc`, ...importFile(f`./dirs.dm`).projects]
});";

            string spec1 = @"
export const r1 = 42;";

            string spec2 = @"
export const r2 = 43;";

            string dirs = @"
export const projects = [f`spec2.dsc`];";

            var result =
                Build()
                .AddSpec(Names.ModuleConfigDsc, packageConfig)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("dirs.dm", dirs)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc").EvaluateExpressionWithNoErrors("r2");

            Assert.Equal(43, result);
        }

        [Fact]
        public void TestImportFileInModuleConfig()
        {
            string packageConfig = @"
package({
    name: ""MyPack"",
    main: f`spec1.dsc`,
    projects: [f`spec1.dsc`, ...importFile(f`./dirs.dm`).projects]
});";

            string spec1 = @"
export const r = 42;";

            string spec2 = @"
export const r = 43;";

            string dirs = @"
export const projects = [f`spec2.dsc`];";

            var result =
                Build()
                .AddSpec(Names.ModuleConfigBm, packageConfig)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("dirs.dm", dirs)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc").EvaluateExpressionWithNoErrors("r");

            Assert.Equal(43, result);
        }
    }
}
