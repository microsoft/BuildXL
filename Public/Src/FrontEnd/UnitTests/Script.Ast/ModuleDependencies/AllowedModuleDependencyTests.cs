// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.ModuleDependencies
{
    public class AllowedModuleDependencyTests : DScriptV2Test
    {
        public AllowedModuleDependencyTests(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void AllowedModuleDependenciesIsBlockedInV1()
        {
            BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V1Module("APackage").WithAllowedDependencies())
                .AddSpec(@"APackage/package.dsc", "const x = 42;")
                .ParseWithDiagnosticId(LogEventId.ExplicitSemanticsDoesNotAdmitAllowedModuleDependencies);
        }

        [Fact]
        public void DuplicateReferencesAreDisallowed()
        {
            BuildWithPrelude()
                .AddSpec(@"A/package.config.dsc", V2Module("A"))
                .AddSpec(@"B/package.config.dsc", V2Module("B"))
                .AddSpec(@"C/package.config.dsc", V2Module("C"))
                .AddSpec(@"D/package.config.dsc", V2Module("D").WithAllowedDependencies("A", "B", "C", "A", "B" ))
                .AddSpec(@"D/package.dsc", "const x = 42;")
                .RootSpec(@"D/package.dsc")
                .ParseWithDiagnosticId(LogEventId.DuplicateAllowedModuleDependencies);
        }

        [Fact]
        public void UnknownAllowedModulesArePermited()
        {
            BuildWithPrelude()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithAllowedDependencies("B"))
                .AddSpec(@"A/package.dsc", "const x = 42;")
                .RootSpec(@"A/package.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void ReferencingAllowedReferencesAreOk()
        {
            BuildWithPrelude()
                .AddSpec(@"A/package.config.dsc", V2Module("A"))
                .AddSpec(@"A/package.dsc", "export const x = 42;")
                .AddSpec(@"B/package.config.dsc", V2Module("B"))
                .AddSpec(@"B/package.dsc", "export const x = 42;")
                .AddSpec(@"C/package.config.dsc", V2Module("C").WithAllowedDependencies("A", "B"))
                .AddSpec(@"C/package.dsc", @"
import * as A from 'A';
import * as B from 'B';
")
                .RootSpec(@"C/package.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void ReferencingDisallowedReferencesFails()
        {
            var diagnostic = BuildWithPrelude()
                .AddSpec(@"A/package.config.dsc", V2Module("A"))
                .AddSpec(@"B/package.config.dsc", V2Module("B"))
                .AddSpec(@"C/package.config.dsc", V2Module("C").WithAllowedDependencies("A"))
                .AddSpec(@"C/package.dsc", @"
import * as A from 'A';
import * as B from 'B';
")
                .RootSpec(@"C/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Importing module 'B' from 'C' is not allowed by 'allowedDependencies' policy specified in module 'C'", diagnostic.FullMessage);
        }
    }
}
