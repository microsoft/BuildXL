// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Rush.IntegrationTests;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushExportsTests")]
    public class RushExportsTests : RushIntegrationTestBase
    {
        public RushExportsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData("[{symbolName: undefined, content: []}]")]
        [InlineData("[{symbolName: 'dup', content: []}, {symbolName: 'dup', content: []}]")]
        public void InvalidRushExportsSettings(string rushExports)
        {
            var config = Build(rushExports: rushExports)
                .AddRushProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(LogEventId.InvalidResolverSettings);
        }

        [Theory]
        [InlineData("[{symbolName: 'test', content: ['non-existent']}]", LogEventId.SpecifiedPackageForExportDoesNotExist)]
        [InlineData("[{symbolName: 'test', content: [{packageName:'@ms/project-A', commands: ['non-existent']}]}]", LogEventId.SpecifiedCommandForExportDoesNotExist)]
        public void MissingPackageInExportsIsFlagged(string exports, LogEventId expectedError)
        {
            var config = Build(rushExports: exports)
               .AddRushProject("@ms/project-A", "src/A")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(expectedError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void InvalidExportSymbolIsFlagged()
        {
            var config = Build(rushExports: "[{symbolName: 'invalid-symbol', content: []}]")
               .AddRushProject("@ms/project-A", "src/A")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            // When the configuration cannot be interpreted, a null result os how the test infrastructure manifests it
            Assert.Null(result);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.ConversionException);
        }

        [Fact]
        public void ExportedValuesCanBeConsumed()
        {
            // Set up a rush resolver side-by-side with a DScript resolver.
            var config = 
                Build(
                    // Configure the Rush resolver to export a value called 'exportSymbol' with the content of project A
                    rushExports: "[{symbolName: 'exportSymbol', content: ['@ms/project-A']}]",
                    moduleName: "rushTest",
                    addDScriptResolver: true)
               .AddRushProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'dscriptTest', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});")
               // Consume 'exportSymbol' from DScript and make sure it is an array of shared opaques. 
               // Check as well the share opaque output is related to project A
               .AddSpec(@"
import {exportSymbol} from 'rushTest'; 
const firstOutput = exportSymbol[0];
const assertion1 = Contract.assert(typeof(firstOutput) === 'SharedOpaqueDirectory');
const assertion2 = Contract.assert(firstOutput.root.isWithin(d`src/A`));")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("'@ms/project-A'", true, true)]
        [InlineData("{packageName:'@ms/project-A', commands:['build']}", true, false)]
        [InlineData("{packageName:'@ms/project-A', commands:['test']}", false, true)]
        public void ScriptCommandSelectionBehavior(string exportContent, bool expectBuildOutput, bool expectTestOutput)
        {
            // Set up a rush resolver side-by-side with a DScript resolver.
            // The rush resolver contains project A with build and test scripts
            // Each of these scripts have particular output directories, that we can later use to identify whether
            // the right output directory was exposed in an export value
            var config =
                Build(
                    executeCommands: "['build', 'test']",
                    rushExports: $"[{{symbolName: 'exportSymbol', content: [{exportContent}]}}]",
                    moduleName: "rushTest",
                    addDScriptResolver: true)
               .AddRushProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "call build"), ("test", "call test") })
               .AddRushConfigurationFile("src/A", @"
{
    ""outputDirectories"": [
        {""path"": ""output/dir/for/build"", ""targetScripts"": [""build""]},
        {""path"": ""output/dir/for/test"", ""targetScripts"": [""test""]}
    ]
}")
               .AddSpec("module.config.dsc", "module({name: 'dscriptTest', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});")
               // Consume 'exportSymbol' from DScript and make sure it contains the expected directory outputs
               .AddSpec(@$"
import {{exportSymbol}} from 'rushTest'; 
{(expectBuildOutput? "const buildAssertion = Contract.assert(exportSymbol.some(dirOutput => dirOutput.root.path === p`src/A/output/dir/for/build`));" : string.Empty)}
{(expectTestOutput ? "const testAssertion = Contract.assert(exportSymbol.some(dirOutput => dirOutput.root.path === p`src/A/output/dir/for/test`));" : string.Empty)}
")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void AllProjectsSymbolIsAlwaysExported()
        {
            // Set up a rush resolver side-by-side with a DScript resolver and consume value 'all' 
            var config =
                Build(
                    moduleName: "rushTest",
                    addDScriptResolver: true)
               .AddRushProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'dscriptTest', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});")
               // Consume 'all' from DScript
               .AddSpec("import {all} from 'rushTest';")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void ReservedSymbolIsHandled()
        {
            var config =
                Build(rushExports: $"[{{symbolName: 'all', content: []}}]")
               .AddRushProject("@ms/project-A", "src/A")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
            AssertErrorEventLogged(LogEventId.SpecifiedExportIsAReservedName);
        }
    }
}
