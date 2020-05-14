// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.FrontEnd.Rush;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Pips = global::BuildXL.Pips.Operations;

namespace Test.BuildXL.FrontEnd.Rush.IntegrationTests
{
    /// <nodoc/>
    public static class RushIntegrationTestsHelper
    {
        /// <summary>
        /// <see cref="AddRushProject(SpecEvaluationBuilder, string, string, string, string[], (string, string)[])"/>
        /// </summary>
        public static SpecEvaluationBuilder AddRushProject(
            this SpecEvaluationBuilder builder,
            string packageName,
            string packageFolder,
            string content = null,
            string[] dependencies = null,
            (string, string)[] scriptCommands = null)
        {
            var dependenciesWithVersions = dependencies?.Select(dep => (dep, "0.0.1"))?.ToArray();
            return AddRushProjectWithExplicitVersions(builder, packageName, packageFolder, content, dependenciesWithVersions, scriptCommands);
        }

        /// <summary>
        /// Utility for adding a node spec together with a corresponding package.json
        /// </summary>
        public static SpecEvaluationBuilder AddRushProjectWithExplicitVersions(
            this SpecEvaluationBuilder builder, 
            string packageName, 
            string packageFolder, 
            string content = null,
            (string, string)[] dependenciesWithVersions = null, 
            (string, string)[] scriptCommands = null)
        {
            return builder
                .AddSpec(Path.Combine(packageFolder, "main.js"), content ?? "function A(){}")
                .AddSpec(Path.Combine(packageFolder, "package.json"), 
                    RushIntegrationTestBase.CreatePackageJson(packageName, scriptCommands, dependenciesWithVersions ?? new (string, string)[] { }));
        }

        /// <summary>
        /// Persists a rush configuration file at the given path
        /// </summary>
        public static SpecEvaluationBuilder AddRushConfigurationFile(
            this SpecEvaluationBuilder builder,
            string path,
            string content)
        {
            builder.AddFile(Path.Combine(path, RushWorkspaceResolver.BxlConfigurationFilename), content);
            return builder;
        }

        /// <summary>
        /// Uses the provenance set by the rush scheduler to retrieve a process pip that corresponds to a given package name and script command
        /// </summary>
        /// <returns>Null if the process is not found</returns>
        public static Pips.Process RetrieveProcess(this EngineState engineState, string packageName, string scriptCommand = null)
        {
            scriptCommand ??= "build";

            var projectSymbol = RushPipConstructor.GetFullSymbolFromProject(packageName, scriptCommand, engineState.SymbolTable);
            var processes = engineState.PipGraph.RetrievePipsOfType(Pips.PipType.Process);

            return (Pips.Process)processes.FirstOrDefault(process => process.Provenance.OutputValueSymbol == projectSymbol);
        }
    }
}
