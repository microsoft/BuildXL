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
using Test.DScript.Ast;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Provides facilities to run the engine adding Lage specific artifacts.
    /// </summary>
    public abstract class LageIntegrationTestBase : DsTestWithCacheBase
    {
        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToNode => Path.Combine(TestDeploymentDir, "node", OperatingSystemHelper.IsLinuxOS ? "bin/node" : "node.exe").Replace("\\", "/");

        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToLage => Path.Combine(TestDeploymentDir, "lage", ".bin", OperatingSystemHelper.IsWindowsOS ? "lage.cmd" : "lage").Replace("\\", "/");

        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToYarn => Path.Combine(TestDeploymentDir, "yarn", "bin", OperatingSystemHelper.IsWindowsOS ? "yarn.cmd" : "yarn").Replace("\\", "/");

        /// <nodoc/>
        protected string PathToNodeFolder => Path.GetDirectoryName(PathToNode).Replace("\\", "/");

        /// <nodoc/>
        protected string PathToLageFolder => Path.Combine(TestDeploymentDir, "lage").Replace("\\", "/");

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

        protected override bool DisableDefaultSourceResolver => true;

        protected LageIntegrationTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Lage.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.JavaScript.ETWLogger.Log);

            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
            OutDir = "target";
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment = null,
            string moduleName = "Test",
            string root = "d`.`",
            IEnumerable<string> executeCommands = null)
        {
            environment ??= new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
                ["NODE_PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToLageFolder),
            };

            // On Linux/Mac, Lage depends on low level tools like sed, readlink, etc. If the value for path is not configured as a passthrough, inject /usr/bin
            if (!OperatingSystemHelper.IsWindowsOS && environment.TryGetValue("PATH", out var value) && value.GetValue() is string path)
            { 
                environment["PATH"] = new DiscriminatingUnion<string, UnitValue>(path + Path.PathSeparator + "/usr/bin");
            }

            // Let's explicitly pass an environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultLagePrelude(
                    environment: environment,
                    moduleName: moduleName,
                    root: root,
                    executeCommands: executeCommands));
        }

        protected BuildXLEngineResult RunLageProjects(
            ICommandLineConfiguration config,
            TestCache testCache = null, 
            IDetoursEventListener detoursListener = null)
        {
            // This bootstraps the 'repo'
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, "package.json").ToString(PathTable), $@"
            {{
                ""private"": true,
                ""name"": ""LageTest"",
                ""version"": ""1.0.0"",
                ""workspaces"": {{
                    ""packages"": [
                      ""src/*""
                    ]
                }},
                ""scripts"": {{
                    ""lage"" : ""{PathToLage}""
                }}
            }}");

            // For now we are hardcoding build -> build and test -> build
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, "lage.config.js").ToString(PathTable), @"
module.exports = {
  pipeline: {
    build: [""^build""],
    test: [""build""],
  },
};");
            // Run yarn install to get the workspace populated
            if (!YarnRun(config.Layout.SourceDirectory.ToString(PathTable), "install"))
            {
                throw new InvalidOperationException("Yarn install failed.");
            }

            return RunEngine(config, testCache, detoursListener);
        }

        /// <summary>
        /// Runs the engine for a given config, assuming the Lage repo is already initialized
        /// </summary>
        protected BuildXLEngineResult RunEngine(ICommandLineConfiguration config, TestCache testCache = null, IDetoursEventListener detoursListener = null)
        {
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

        private string DefaultLagePrelude(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment,
            string moduleName,
            string root,
            IEnumerable<string> executeCommands) => $@"
config({{
    resolvers: [
        {{
            kind: 'Lage',
            moduleName: '{moduleName}',
            root: {root},
            nodeExeLocation: f`{PathToNode}`,
            {DictionaryToExpression("environment", environment)}
            {(executeCommands == null ? string.Empty : $"execute: [{string.Join(",", executeCommands.Select(command => $"\"{command}\""))}],")}
        }}
    ],
}});";

        private static string DictionaryToExpression(string memberName, Dictionary<string, DiscriminatingUnion<string, UnitValue>> dictionary)
        {
            return (dictionary == null ?
                string.Empty :
                $"{memberName}: Map.empty<string, (PassthroughEnvironmentVariable | string)>(){ string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', {(property.Value?.GetValue() is UnitValue ? "Unit.unit()" : $"'{property.Value?.GetValue()}'")})")) },");
        }

        private bool YarnRun(string workingDirectory, string yarnArgs)
        {
            string arguments = $"{Path.Combine(Path.GetDirectoryName(PathToYarn), "yarn")}.js {yarnArgs}";
            string filename = PathToNode;

            // Unfortunately, capturing standard out/err non-deterministically hangs node.exe on exit when 
            // concurrent npm install operations happen. Found reported bugs about this that look similar enough
            // to the problem that manifested here.
            // So we just report exit codes.
            var startInfo = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = false,
                RedirectStandardOutput = false,
                UseShellExecute = false,
            };

            startInfo.Environment["PATH"] += $";{PathToNodeFolder}";
            startInfo.Environment["APPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var runYarn = Process.Start(startInfo);
            runYarn.WaitForExit();

            return runYarn.ExitCode == 0;
        }
    }
}
