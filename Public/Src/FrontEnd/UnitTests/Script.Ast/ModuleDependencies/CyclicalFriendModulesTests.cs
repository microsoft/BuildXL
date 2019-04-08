// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.ModuleDependencies
{
    public class CyclicalFriendModulesTests : DScriptV2Test
    {
        public CyclicalFriendModulesTests(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void CyclicalFriendModulesIsBlockedInV1()
        {
            BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V1Module("APackage").WithCyclicalFriendModules())
                .AddSpec(@"APackage/package.dsc", "const x = 42;")
                .ParseWithDiagnosticId(LogEventId.ExplicitSemanticsDoesNotAdmitCyclicalFriendModules);
        }

        [Fact]
        public void DuplicateCyclicalFriendsAreDisallowed()
        {
            BuildWithModulePolicies()
                .AddSpec(@"A/package.config.dsc", V2Module("A"))
                .AddSpec(@"B/package.config.dsc", V2Module("B"))
                .AddSpec(@"C/package.config.dsc", V2Module("C"))
                .AddSpec(@"D/package.config.dsc", V2Module("D").WithCyclicalFriendModules("A", "B", "C", "A", "B" ))
                .AddSpec(@"D/package.dsc", "const x = 42;")
                .RootSpec(@"D/package.dsc")
                .ParseWithDiagnosticId(LogEventId.DuplicateCyclicalFriendModules);
        }

        [Fact]
        public void UnknownCyclicalFriendsArePermited()
        {
            BuildWithModulePolicies()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithCyclicalFriendModules("B"))
                .AddSpec(@"A/package.dsc", "const x = 42;")
                .RootSpec(@"A/package.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void CyclicalFriendsAreDisallowedIfNoGlobalPolicyIsPresent()
        {
            BuildWithPrelude()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithCyclicalFriendModules("A"))
                .AddSpec(@"A/package.dsc", "export const x = 42;")
                .RootSpec(@"A/package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.CyclicalFriendModulesNotEnabledByPolicy);
        }

        [Fact]
        public void CyclicalFriendsAreAllowedByGlobalPolicy()
        {
            // TODO:ST: I'm not sure that the following case should be allowed.
            BuildWithModulePolicies()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithCyclicalFriendModules("A"))
                .AddSpec(@"A/package.dsc", "export const x = 42;")
                .RootSpec(@"A/package.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void CyclicalFriendsWhitelistCycles()
        {
            // A -> B -> C -> A, but A allows C in a cycle
            BuildWithModulePolicies()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithAllowedDependencies("B").WithCyclicalFriendModules("C"))
                .AddSpec(
                    @"B/package.config.dsc",
                    V2Module("B").WithAllowedDependencies("C"))
                .AddSpec(@"C/package.config.dsc", V2Module("C").WithAllowedDependencies("A"))
                .RootSpec(@"A/package.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void CyclicalFriendsWhitelistCycles2()
        {
            // A -> B -> C -> A, but C allows B in a cycle
            BuildWithModulePolicies()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithAllowedDependencies("B"))
                .AddSpec(
                    @"B/package.config.dsc",
                    V2Module("B").WithAllowedDependencies("C"))
                .AddSpec(@"C/package.config.dsc", V2Module("C").WithAllowedDependencies("A").WithCyclicalFriendModules("B"))
                .RootSpec(@"A/package.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void CyclicalAreDisallowed()
        {
            // A -> B -> C -> A, but C allows B in a cycle
            var diagnostic = BuildWithModulePolicies()
                .AddSpec(@"A/package.config.dsc", V2Module("A").WithAllowedDependencies("B"))
                .AddSpec(
                    @"B/package.config.dsc",
                    V2Module("B").WithAllowedDependencies("C"))
                .AddSpec(@"C/package.config.dsc", V2Module("C").WithAllowedDependencies("A"))
                .RootSpec(@"A/package.dsc")
                .EvaluateWithFirstError();

            // The graph in the error can be different, so we can't assert on it:
            // It can be 'A' -> 'B' -> 'C' -> 'A' or 'C' -> 'A' -> 'B' -> 'C'
            Assert.Contains("Module dependency graph forms a cycle", diagnostic.FullMessage);
        }

        private SpecEvaluationBuilder BuildWithModulePolicies()
        {
            return BuildWithPrelude(
@"config({
        frontEnd: {enableCyclicalFriendModules: true}
    });");
        }
    }
}
