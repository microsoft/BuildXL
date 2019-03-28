// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceWellShapedInlineImportConfigPackageRule : DsTest
    {
        public TestEnforceWellShapedInlineImportConfigPackageRule(ITestOutputHelper output)
            : base(output)
        { }

        /// <summary>
        /// package.config.dsc
        /// </summary>
        private const string PackageConfigName = Names.PackageConfigDsc;

        /// <summary>
        /// module.config.bm
        /// </summary>
        private const string ModuleConfigName = Names.ModuleConfigBm;

        /// <summary>
        /// config.dsc
        /// </summary>
        private const string ConfigName = Names.ConfigDsc;

        // Connections:
        // config.dsc imports build.bl's 'modules' property, including package.config.dsc
        // package.config.dsc imports packageBuildList.bl's 'projects' property
        // packageBuildList.bl's 'projects' property imports otherPackageBuildList.bl's 'project' property, which contains build.dsc
        private const string SpecName = "build.dsc";
        private const string SimpleSpecContents = "export const x = 42;";
        private const string ConfigBuildListName = "build.bl";
        private const string ConfigBuildListContents = "export const modules = [f`./" + PackageConfigFileName + "`];";
        private const string ConfigBuildListWithImportsName = "buildWithImports.bl";
        private const string PackageFileName = "package.dsc";
        private const string PackageFileContents = "export const x = 42;";
        private const string PackageConfigFileName = "package.config.dsc";
        private const string PackageConfigFileContents = "module({ name: \"foo\", projects: importFile(f`" + PackageBuildListName + "`).projects });";
        private const string PackageBuildListName = "packageBuildList.bl";
        private const string PackageBuildListContents = "export const projects = importFile(f`" + OtherPackageBuildListName + "`).projects;";
        private const string OtherPackageBuildListName = "otherPackageBuildList.bl";
        private const string OtherPackageBuildListContents = "export const projects = [f`./" + SpecName + "`];";

        [Theory]
        [InlineData("\"./" + SpecName + "\"")]
        [InlineData("\"/" + SpecName + "\"")]
        [InlineData("'./" + SpecName + "'")]
        [InlineData("'/" + SpecName + "'")]
        [InlineData("\"./foobar/build.dsc\"")]
        [InlineData("'./foobar/build.dsc'")]
        [InlineData("\"./foo.dsc\"")]
        [InlineData("\"/package.dsc\"")]
        [InlineData("\"/path/to/package.dsc\"")]
        [InlineData("d`./path/to/package.dsc`")]
        [InlineData("f`./${tag}`")]
        public void TestImportFileInvalidParameterConfig(string importParameter)
        {
            string configCode = $"config({{ modules: [...importFile({importParameter}).modules]}});";

            Build()
                .LegacyConfiguration(configCode)
                .AddSpec(SpecName, SimpleSpecContents)
                .EvaluateWithDiagnosticId(LogEventId.ImportFileNotPassedAFileLiteral);
        }


        [Theory]
        [InlineData("f`./" + ConfigBuildListName + "`")]
        [InlineData("f`" + ConfigBuildListName + "`")]
        public void TestImportFileValidParameterConfig(string importParameter)
        {
            string configCode = $"config({{ modules: [...importFile({importParameter}).modules, p`{SpecEvaluationBuilder.PreludePackageConfigRelativePathDsc}`]}});";

            var result = Build()
                .LegacyConfiguration(configCode)
                .AddSpec(ConfigBuildListName, ConfigBuildListContents)
                .AddSpec(PackageFileName, PackageFileContents)
                .AddSpec(PackageConfigFileName, PackageConfigFileContents)
                .AddSpec(PackageBuildListName, PackageBuildListContents)
                .AddSpec(OtherPackageBuildListName, OtherPackageBuildListContents)
                .AddSpec(SpecName, SimpleSpecContents)
                .RootSpec(SpecName)
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(result, 42);
        }

        [Theory]
        [InlineData("f`./" + ConfigBuildListWithImportsName + "`")]
        [InlineData("f`" + ConfigBuildListWithImportsName + "`")]
        public void TestNestedImportFile(string importParameter)
        {
            string configCode = $"config({{ modules: [...importFile({importParameter}).modules, p`{SpecEvaluationBuilder.PreludePackageConfigRelativePathDsc}`]}});";

            var result = Build()
                .LegacyConfiguration(configCode)
                .AddSpec(ConfigBuildListName, ConfigBuildListContents)
                .AddSpec(ConfigBuildListWithImportsName, "export const modules = importFile(f`" + ConfigBuildListName + "`).modules;")
                .AddSpec(PackageFileName, PackageFileContents)
                .AddSpec(PackageConfigFileName, PackageConfigFileContents)
                .AddSpec(PackageBuildListName, PackageBuildListContents)
                .AddSpec(OtherPackageBuildListName, OtherPackageBuildListContents)
                .AddSpec(SpecName, SimpleSpecContents)
                .RootSpec(SpecName)
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(result, 42);
        }

        [Theory]
        [InlineData("\"./" + SpecName + "\"")]
        [InlineData("\"/" + SpecName + "\"")]
        [InlineData("'./" + SpecName + "'")]
        [InlineData("'/" + SpecName + "'")]
        [InlineData("\"./foobar/build.dsc\"")]
        [InlineData("'./foobar/build.dsc'")]
        [InlineData("\"./foo.dsc\"")]
        [InlineData("\"/package.dsc\"")]
        [InlineData("\"/path/to/package.dsc\"")]
        [InlineData("d`./path/to/package.dsc`")]
        [InlineData("f`./${tag}`")]
        public void TestImportFileInvalidInPackage(string importParameter)
        {
            string configCode = "config({ modules: [f`./" + PackageConfigName + "`], resolvers: [{ kind: \"SourceResolver\", root: d`.`}] });";
            string code = $"module({{ name: \"foo\", projects: [importFile({importParameter}).projects]}});";

            var result = Build()
                .LegacyConfiguration(configCode)
                .AddSpec(PackageConfigName, code)
                .AddSpec(SpecName, SimpleSpecContents)
                .EvaluateWithDiagnosticId(LogEventId.ImportFileNotPassedAFileLiteral);
        }
        
        [Theory]
        [InlineData(PackageConfigName)]
        [InlineData(ModuleConfigName)]
        [InlineData(ConfigName)]
        public void TestNamedImportOfConfigPackageModuleInConfig(string importParameter)
        {
            string configCode = "config({ modules: [...importFrom(\"./" + importParameter + "\").project]});";

            Build()
                .LegacyConfiguration(configCode)
                .AddSpec(SpecName, SimpleSpecContents)
                .EvaluateWithDiagnosticId(LogEventId.NamedImportOfConfigPackageModule);
        }

        [Theory]
        [InlineData(PackageConfigName)]
        [InlineData(ModuleConfigName)]
        [InlineData(ConfigName)]
        public void TestNamedImportOfConfigPackageModuleInModule(string importParameter)
        {
            string configCode = $"config({{ modules: [f`./{ModuleConfigName}`]}});";
            string moduleCode = $"module({{ name: \"foo\", projects: [importFile(f`{importParameter}`).projects]}});";

            Build()
                .LegacyConfiguration(configCode)
                .AddSpec(ModuleConfigName, moduleCode)
                .EvaluateWithDiagnosticId(LogEventId.NamedImportOfConfigPackageModule);
        }

        [Theory]
        [InlineData(PackageConfigName)]
        [InlineData(ModuleConfigName)]
        [InlineData(ConfigName)]
        public void TestNamedImportOfConfigPackageModuleInPackage(string importParameter)
        {
            string configCode = $"config({{ modules: [f`./{PackageConfigName}`]}});";
            string packageCode = $"module({{ name: \"foo\", project = [importFile(f`{importParameter}`).projects]}});";

            var result = Build()
                .LegacyConfiguration(configCode)
                .AddSpec(PackageConfigName, packageCode)
                .EvaluateWithDiagnosticId(LogEventId.NamedImportOfConfigPackageModule);
        }
    }
}
