// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
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
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(LogEventId.InvalidResolverSettings);
        }

        [Theory]
        [InlineData("[{symbolName: 'test', content: ['non-existent']}]", LogEventId.SpecifiedPackageForExportDoesNotExist)]
        [InlineData("[{symbolName: 'test', content: [{packageName:'@ms/project-A', commands: ['non-existent']}]}]", LogEventId.SpecifiedPackageForExportDoesNotExist)]
        public void MissingPackageInExportsIsFlagged(string exports, LogEventId expectedError)
        {
            var config = Build(rushExports: exports)
               .AddJavaScriptProject("@ms/project-A", "src/A")
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
               .AddJavaScriptProject("@ms/project-A", "src/A")
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
               .AddJavaScriptProject("@ms/project-A", "src/A")
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
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "call build"), ("test", "call test") })
               .AddBxlConfigurationFile("src/A", @"
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
               .AddJavaScriptProject("@ms/project-A", "src/A")
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
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
            AssertErrorEventLogged(LogEventId.SpecifiedExportIsAReservedName);
        }

        [Fact]
        public void ExportedValuesWithProjectMappingCanBeConsumed()
        {
            // Set up a rush resolver side-by-side with a DScript resolver.
            // With includeProjectMapping: true, the export should produce a Map<JavaScriptProjectIdentifier, SharedOpaqueDirectory[]>
            var config =
                Build(
                    rushExports: "[{symbolName: 'exportSymbol', content: ['@ms/project-A'], includeProjectMapping: true}]",
                    moduleName: "rushTest",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'dscriptTest', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});")
               // Consume 'exportSymbol' from DScript with explicit type annotations to validate the type checker accepts the generated type
               .AddSpec(@"
import {exportSymbol} from 'rushTest'; 

const typedExport : Map<JavaScriptProjectIdentifier, SharedOpaqueDirectory[]> = exportSymbol;
const entries : [JavaScriptProjectIdentifier, SharedOpaqueDirectory[]][] = typedExport.toArray();
const assertion1 = Contract.assert(entries.length === 1);

// Each entry is a [key, value] tuple
const entry : [JavaScriptProjectIdentifier, SharedOpaqueDirectory[]] = entries[0];
const projectId : JavaScriptProjectIdentifier = entry[0];
const outputs : SharedOpaqueDirectory[] = entry[1];
const assertion2 = Contract.assert(projectId.packageName === '@ms/project-A');
const assertion3 = Contract.assert(projectId.command === 'build');
const assertion4 = Contract.assert(typeof(outputs[0]) === 'SharedOpaqueDirectory');
const assertion5 = Contract.assert(outputs[0].root.isWithin(d`src/A`));")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void ExportWithProjectMappingDefaultIsFalse()
        {
            // Without includeProjectMapping (defaults to false), the export is a StaticDirectory[]
            var config =
                Build(
                    rushExports: "[{symbolName: 'exportSymbol', content: ['@ms/project-A']}]",
                    moduleName: "rushTest",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'dscriptTest', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});")
               .AddSpec(@"
import {exportSymbol} from 'rushTest'; 

const typedExport : SharedOpaqueDirectory[] = exportSymbol;
const firstOutput : SharedOpaqueDirectory = typedExport[0];
const assertion1 = Contract.assert(typeof(firstOutput) === 'SharedOpaqueDirectory');")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void ProjectMappingWithMultipleProjects()
        {
            // With multiple projects in the export content and includeProjectMapping: true,
            // the map should have one entry per project+command
            var config =
                Build(
                    rushExports: "[{symbolName: 'exportSymbol', content: ['@ms/project-A', '@ms/project-B'], includeProjectMapping: true}]",
                    moduleName: "rushTest",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddJavaScriptProject("@ms/project-B", "src/B")
               .AddSpec("module.config.dsc", "module({name: 'dscriptTest', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});")
               // Verify the map has 2 entries, with explicit type annotation
               .AddSpec(@"
import {exportSymbol} from 'rushTest'; 

const typedExport : Map<JavaScriptProjectIdentifier, SharedOpaqueDirectory[]> = exportSymbol;
const entries : [JavaScriptProjectIdentifier, SharedOpaqueDirectory[]][] = typedExport.toArray();
const assertion1 = Contract.assert(entries.length === 2);")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B")
            });

            Assert.True(result.IsSuccess);
        }
    }
}
