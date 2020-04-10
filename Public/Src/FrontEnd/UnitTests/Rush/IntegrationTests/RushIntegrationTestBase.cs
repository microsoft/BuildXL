// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Provides facilities to run the engine adding Rush specific artifacts.
    /// </summary>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public abstract class RushIntegrationTestBase : DsTestWithCacheBase
    {
        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToRush => Path.Combine(TestDeploymentDir, "Rush", "node_modules", "@microsoft", "rush", "bin", "rush").Replace("\\", "/");

        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToNodeModules => Path.Combine(TestDeploymentDir, "Rush", "node_modules").Replace("\\", "/");

        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToNode => Path.Combine(TestDeploymentDir, "Node", OperatingSystemHelper.IsLinuxOS? "bin/node" : "node.exe").Replace("\\", "/");

        /// <nodoc/>
        protected string PathToNodeFolder => Path.GetDirectoryName(PathToNode).Replace("\\", "/");

        /// <summary>
        /// Default out dir to use in projects
        /// </summary>
        protected string OutDir { get; }

        /// <summary>
        /// Root to the source enlistment root
        /// </summary>
        protected string SourceRoot { get; }

        // By default the engine runs e2e
        protected virtual EnginePhases Phase => EnginePhases.Execute;

        protected RushIntegrationTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Rush.ETWLogger.Log);

            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
            OutDir = "target";
        }

        protected SpecEvaluationBuilder Build(
            Dictionary<string, string> environment = null,
            string executeCommands = null,
            string customRushCommands = null,
            string rushBaseLibLocation = "")
        {
            environment ??= new Dictionary<string, string> { 
                ["PATH"] = PathToNodeFolder
            };

            return Build(
                environment.ToDictionary(kvp => kvp.Key, kvp => new DiscriminatingUnion<string, UnitValue>(kvp.Value)),
                executeCommands,
                customRushCommands,
                rushBaseLibLocation);
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment,
            string executeCommands = null,
            string customRushCommands = null,
            string rushBaseLibLocation = "")
        {
            environment ??= new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder) 
            };

            // We reserve the null string for a true undefined.
            // rush-lib is part of node_modules deployment, so use that by default
            if (rushBaseLibLocation == string.Empty)
            {
                rushBaseLibLocation = PathToNodeModules;
            }

            // Let's explicitly pass an empty environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultRushPrelude(
                    environment: environment,
                    executeCommands: executeCommands,
                    customRushCommands: customRushCommands,
                    rushBaseLibLocation: rushBaseLibLocation));
        }

        protected BuildXLEngineResult RunRushProjects(
            ICommandLineConfiguration config,
            (string, string)[] rushPathAndProjectNames,
            TestCache testCache = null, 
            IDetoursEventListener detoursListener = null)
        {
            // Run 'rush init'. This bootstraps the 'repo' with rush template files, including rush.json
            if (!RushInit(config, out var failure))
            {
                throw new InvalidOperationException(failure);
            }

            // Update rush.json with the projects that need to be part of the 'repo'
            AddProjectsToRushConfig(config, rushPathAndProjectNames);

            // Run 'rush update' so dependencies are configured
            if (!RushUpdate(config, out failure))
            {
                throw new InvalidOperationException(failure);
            }

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                ((CommandLineConfiguration)config).Engine.Phase = Phase;
                ((CommandLineConfiguration)config).Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: true,
                    engine: out var engine,
                    testCache: testCache,
                    detoursListener: detoursListener);

                return engineResult;
            }
        }

        private void AddProjectsToRushConfig(ICommandLineConfiguration config, (string, string)[] rushProjectsPathAndName)
        {
            var pathToRushJson = config.Layout.SourceDirectory.Combine(PathTable, "rush.json").ToString(PathTable);
            string rushJson = File.ReadAllText(pathToRushJson);

            // Update the initial template created by 'rush init' with the projects that need to be part of the build
            var updatedRushJson = rushJson.Replace("\"projects\": [", "\"projects\": [" + string.Join(",", 
                rushProjectsPathAndName.Select(pathAndName => $"{{\"packageName\": \"{pathAndName.Item2}\", \"projectFolder\": \"{pathAndName.Item1}\"}}")));

            File.WriteAllText(pathToRushJson, updatedRushJson);
        }

        public static string CreatePackageJson(string projectName, (string, string)[] scriptCommands = null, string[] dependencies = null)
        {
            scriptCommands ??= new[] { ("build", "node ./main.js") };
            dependencies ??= new string[] { };

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
        {string.Join(",", dependencies.Select(dep => $"\"{dep}\":\"0.0.1\""))}
    }}
}}
";
        }

        protected static bool IsDependencyAndDependent(global::BuildXL.Pips.Operations.Process dependency, global::BuildXL.Pips.Operations.Process dependent)
        {
            // Unfortunately the test pip graph we are using doesn't keep track of dependencies/dependents. So we check there is a directory output of the dependency 
            // that is a directory input for a dependent

            return dependency.DirectoryOutputs.Any(directoryOutput => dependent.DirectoryDependencies.Any(directoryDependency => directoryDependency == directoryOutput));
        }

        private string DefaultRushPrelude(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment,
            string executeCommands,
            string customRushCommands,
            string rushBaseLibLocation) => $@"
config({{
    disableDefaultSourceResolver: true,
    resolvers: [
        {{
            kind: 'Rush',
            moduleName: 'Test',
            root: d`.`,
            nodeExeLocation: f`{PathToNode}`,
            {DictionaryToExpression("environment", environment)}
            {(executeCommands != null? $"execute: {executeCommands}," : string.Empty)}
            {(customRushCommands != null ? $"customCommands: {customRushCommands}," : string.Empty)}
            {(rushBaseLibLocation != null ? $"rushLibBaseLocation: d`{rushBaseLibLocation}`," : string.Empty)}
        }},
    ],
}});";

        private static string DictionaryToExpression(string memberName, Dictionary<string, DiscriminatingUnion<string, UnitValue>> dictionary)
        {
            return (dictionary == null ?
                string.Empty :
                $"{memberName}: Map.empty<string, (PassthroughEnvironmentVariable | string)>(){ string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', {(property.Value?.GetValue() is UnitValue ? "Unit.unit()" : $"'{property.Value?.GetValue()}'")})")) },");
        }

        private bool RushInit(ICommandLineConfiguration config, out string failure)
        {
            var result = RushRun(config, "init --overwrite-existing", out failure);

            if (result)
            {
                var pathToRushJson = config.Layout.SourceDirectory.Combine(PathTable, "rush.json").ToString(PathTable);
                string rushJson = File.ReadAllText(pathToRushJson);

                // Update the initial template created by 'rush init' to accept a higher version of node
                var updatedRushJson = rushJson.Replace(
                    "\"nodeSupportedVersionRange\": \">=10.13.0 <11.0.0\"",
                    "\"nodeSupportedVersionRange\": \">=10.13.0 <13.3.1\"");

                File.WriteAllText(pathToRushJson, updatedRushJson);
            }

            return result;
        }

        private bool RushUpdate(ICommandLineConfiguration config, out string failure)
        {
            return RushRun(config, "update", out failure);
        }

        private bool RushRun(ICommandLineConfiguration config, string rushArgs, out string failure)
        {
            string arguments = $"{PathToRush} {rushArgs}";
            string filename = PathToNode;

            var startInfo = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                WorkingDirectory = config.Layout.SourceDirectory.ToString(PathTable),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            startInfo.Environment["PATH"] += $";{PathToNodeFolder}";
            startInfo.Environment["NODE_PATH"] = PathToNodeModules;
            startInfo.Environment["USERPROFILE"] = config.Layout.RedirectedUserProfileJunctionRoot.IsValid ?
                config.Layout.RedirectedUserProfileJunctionRoot.ToString(PathTable) :
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            startInfo.Environment["APPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var runRush = Process.Start(startInfo);
            runRush.WaitForExit();

            // Rush does not seem pretty consistent around sending stuff to stderr vs stdout, so let's add both.
            failure = runRush.StandardOutput.ReadToEnd() + Environment.NewLine + runRush.StandardError.ReadToEnd();

            return runRush.ExitCode == 0;
        }
    }
}
