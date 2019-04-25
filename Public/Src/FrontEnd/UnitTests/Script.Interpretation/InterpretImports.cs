// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretImports : DsTest
    {
        public InterpretImports(ITestOutputHelper output)
            : base(null) // becaue error diagnostics generated for 'TestImportFromInvalidParameterConfig' crash XUnit
        {
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var result = base.GetFrontEndConfiguration(isDebugged);
            result.CycleDetectorStartupDelay = 0;
            return result;
        }

        [Fact]
        public void RecursiveImportOfTheSameFileWithImportFileShouldFailWithCycle()
        {
            const string Config = @"config({});";
            const string PackageAFileName = "packageA.dsc";

            string packageA = $@"
export const projectA = importFile(f`./{PackageAFileName}`).projectA;
export const x = 42;
";

            string packageConfigA = $@"
module({{
    name: ""PackA"",
    projects: [f`./{PackageAFileName}`, importFile(f`./{PackageAFileName}`).projectA],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences
}});
";

            Build()
                .LegacyConfiguration(Config)
                .AddSpec(@"package.config.dsc", packageConfigA)
                .AddSpec(PackageAFileName, packageA)
                .RootSpec(PackageAFileName)
                .EvaluateWithDiagnosticId(LogEventId.Cycle);
        }

        [Fact]
        public void RecursiveImportBetweenTwoFilesWithImportFileShouldFailWithCycle()
        {
            const string Config = @"config({});";
            const string PackageAFileName = "packageA.dsc";
            const string PackageBFileName = "packageB.dsc";

            string packageA = $@"
export const projectA = importFile(f`./{PackageBFileName}`).projectB;
export const x = 42;
";

            string packageB = $@"export const projectB = importFile(f`./{PackageAFileName}`).projectA;";

            string packageConfigA = $@"
module({{
    name: ""PackA"",
    projects: [f`./{PackageAFileName}`, f`./{PackageBFileName}`, importFile(f`./{PackageAFileName}`).projectA],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences
}});
";

            Build()
                .LegacyConfiguration(Config)
                .AddSpec(@"package.config.dsc", packageConfigA)
                .AddSpec(PackageAFileName, packageA)
                .AddSpec(PackageBFileName, packageB)
                .RootSpec(PackageAFileName)
                .EvaluateWithDiagnosticId(LogEventId.Cycle);
        }

        [Fact]
        public void ImportFromShouldGracefullyFailWhenUnknownFileIsProvided()
        {
            string spec1 = @"export const x = importFrom('./project1.dsc');";
            AssertCannotBuildWorkspace(spec1, "is referencing project", "which is not under the same package");
        }

        [Theory]
        [InlineData("\"folder/file\"")]
        [InlineData("\"folder\\\\file\"")]
        public void TestImportFromInvalidParameterConfig(string importParameter)
        {
            string spec1 = $@"export const x = importFrom('{importParameter}');";
            AssertCannotBuildWorkspace(spec1, "No resolver was found that owns module");
        }

        [Fact]
        public void InterpretImportFileWithAbsolutePath()
        {
            string absolutePathToSpec1 = Path.Combine(RelativeSourceRoot, "spec1.dsc");

            string PackageConfig = $@"
            module({{
                name: ""MyPack"",
                nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
                projects: [importFile(f`{absolutePathToSpec1}`).fileName]
            }});";

            const string Spec1 =
                "export const fileName = f`./spec2.dsc`;";

            const string Spec2 =
                @"namespace M2 {
  export const r = 42;
}";

            var result =
                Build()
                    .AddSpec(global::BuildXL.FrontEnd.Script.Constants.Names.PackageConfigDsc, PackageConfig)
                    .AddSpec("spec1.dsc", Spec1)
                    .AddSpec("spec2.dsc", Spec2)
                    .EvaluateExpressionWithNoErrors("spec2.dsc", "M2.r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretImportFile()
        {
            const string PackageConfig = @"
            module({
                name: ""MyPack"",
                nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
                projects: [f`./spec1.dsc`, importFile(f`./spec1.dsc`).fileName]
            });";

            const string Spec1 =
                "export const fileName = f`./spec2.dsc`;";

            const string Spec2 =
                @"namespace M2 {
  export const r = 42;
}";

            var result =
                Build()
                    .AddSpec(global::BuildXL.FrontEnd.Script.Constants.Names.PackageConfigDsc, PackageConfig)
                    .AddSpec("spec1.dsc", Spec1)
                    .AddSpec("spec2.dsc", Spec2)
                    .EvaluateExpressionWithNoErrors("spec2.dsc", "M2.r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void NamespaceAliasCanResolveOnlyValue()
        {
            // You can alias namespace to get values directly from it
            string spec = @"
namespace M {
    export const a = 1;
}

const x = M;
export const r1 = x.a; // 1
";

            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("r1");

            Assert.Equal(1, result);
        }

        [Fact]
        public void NamespaceAliasCannotResolveNestedNamespace()
        {
            string spec = @"
namespace M {  
    namespace N {
        export const a = 42;
    }
}

const x = M;
export const r1 = x.N.a; // 1
";

            var result = EvaluateExpressionWithNoErrors(spec, "r1");
            Assert.Equal(42, result);
        }
        
        [Fact]
        public void MalformedSpecShouldNotCrash()
        {
            var spec1 = @"
import x from 'Sdk.Prelude';
";

            Build()
                .AddSpec("spec1.dsc", spec1)
                .RootSpec("spec1.dsc")
                .ParseWithDiagnosticId(LogEventId.DefaultImportsNotAllowed);
        }

        [Fact]
        public void ModuleReferenceWithInvalidCharactersAredetected()
        {
            string PackageConfig = $@"
            module({{
                name: 'MyPack',
                nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
                projects: [f`./spec1.dsc`, importFile(f`./Invalid{Path.GetInvalidPathChars().First()}name.dsc`).fileName]
            }});";

                const string Spec1 =
                    "export const fileName = f`./spec2.dsc`;";

                const string Spec2 =
                    @"namespace M2 {
  export const r = 42;
}";

                var result =
                    Build()
                        .AddSpec(global::BuildXL.FrontEnd.Script.Constants.Names.PackageConfigDsc, PackageConfig)
                        .AddSpec("spec1.dsc", Spec1)
                        .AddSpec("spec2.dsc", Spec2)
                        .EvaluateWithDiagnosticId(LogEventId.ModuleSpecifierContainsInvalidCharacters);

                Assert.Contains("contains invalid characters", result.FullMessage);
        }

        private void AssertCannotBuildWorkspace(string spec, params string[] messages)
        {
            var result = EvaluateSpec(spec, new string[0]);
            result.ExpectErrorCode((int)global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace, count: 1);
            result.ExpectErrorMessageSubstrings(messages);
        }
    }
}
