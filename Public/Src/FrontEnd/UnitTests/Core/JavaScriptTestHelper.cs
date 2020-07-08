// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.Utilities.Configuration;
using Pips = global::BuildXL.Pips.Operations;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Some utilities for JavaScript-based tests
    /// </summary>
    public static class JavaScriptTestHelper
    {
        /// <summary>
        /// <see cref="AddJavaScriptProject(SpecEvaluationBuilder, string, string, string, string[], (string, string)[])"/>
        /// </summary>
        public static SpecEvaluationBuilder AddJavaScriptProject(
            this SpecEvaluationBuilder builder,
            string packageName,
            string packageFolder,
            string content = null,
            string[] dependencies = null,
            (string, string)[] scriptCommands = null)
        {
            var dependenciesWithVersions = dependencies?.Select(dep => (dep, "0.0.1"))?.ToArray();
            return AddJavaScriptProjectWithExplicitVersions(builder, packageName, packageFolder, content, dependenciesWithVersions, scriptCommands);
        }

        /// <summary>
        /// Utility for adding a node spec together with a corresponding package.json
        /// </summary>
        public static SpecEvaluationBuilder AddJavaScriptProjectWithExplicitVersions(
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
                    CreatePackageJson(packageName, scriptCommands, dependenciesWithVersions ?? new (string, string)[] { }));
        }

        /// <summary>
        /// Persists a Bxl configuration file at the given path
        /// </summary>
        public static SpecEvaluationBuilder AddBxlConfigurationFile(
            this SpecEvaluationBuilder builder,
            string path,
            string content)
        {
            builder.AddFile(Path.Combine(path, JavaScriptWorkspaceResolver<DsTest, IJavaScriptResolverSettings>.BxlConfigurationFilename), content);
            return builder;
        }

        /// <summary>
        /// Uses the provenance set by the JavaScript scheduler to retrieve a process pip that corresponds to a given package name and script command
        /// </summary>
        /// <returns>Null if the process is not found</returns>
        public static Pips.Process RetrieveProcess(this EngineState engineState, string packageName, string scriptCommand = null)
        {
            scriptCommand ??= "build";

            var projectSymbol = JavaScriptPipConstructor.GetFullSymbolFromProject(packageName, scriptCommand, engineState.SymbolTable);
            var processes = engineState.PipGraph.RetrievePipsOfType(Pips.PipType.Process);

            return (Pips.Process)processes.FirstOrDefault(process => process.Provenance.OutputValueSymbol == projectSymbol);
        }

        public static string CreatePackageJson(
            string projectName,
            (string, string)[] scriptCommands = null,
            (string dependency, string version)[] dependenciesWithVersions = null)
        {
            scriptCommands ??= new[] { ("build", "node ./main.js") };
            dependenciesWithVersions ??= new (string, string)[] { };

            return $@"
{{
  ""name"": ""{projectName}"",
  ""version"": ""0.0.1"",
  ""description"": ""Test project {projectName}"",
  ""scripts"": {{
        {string.Join(",", scriptCommands.Select(kvp => $"\"{kvp.Item1}\": \"{kvp.Item2}\""))}
  }},
  ""main"": ""main.js"",
  ""dependencies"": {{
        {string.Join(",", dependenciesWithVersions.Select(depAndVer => $"\"{depAndVer.dependency}\":\"{depAndVer.version}\""))}
    }}
}}
";
        }
    }
}
