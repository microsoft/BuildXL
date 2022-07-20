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

namespace Test.BuildXL.FrontEnd.Yarn
{
    /// <summary>
    /// Provides facilities to run the engine adding Yarn specific artifacts.
    /// </summary>
    public abstract class YarnIntegrationTestBase : DsTestWithCacheBase
    {
        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToYarn => Path.Combine(TestDeploymentDir, "yarn", "bin", OperatingSystemHelper.IsWindowsOS ? "yarn.cmd" : "yarn").Replace("\\", "/");

        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToNode => Path.Combine(TestDeploymentDir, "node", OperatingSystemHelper.IsWindowsOS? "node.exe" : "node").Replace("\\", "/");

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

        protected override bool DisableDefaultSourceResolver => true;

        protected YarnIntegrationTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Yarn.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.JavaScript.ETWLogger.Log);

            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
            OutDir = "target";
        }

        protected SpecEvaluationBuilder Build(
            Dictionary<string, string> environment = null,
            string yarnLocation = "",
            string moduleName = "Test",
            string root = "d`.`",
            string additionalOutputDirectories = null,
            bool? enableFullReparsePointResolving = null,
            string nodeLocation = "")
        {
            environment ??= new Dictionary<string, string> {
                ["PATH"] = PathToNodeFolder,
            };

            return Build(
                environment.ToDictionary(kvp => kvp.Key, kvp => new DiscriminatingUnion<string, UnitValue>(kvp.Value)),
                yarnLocation,
                moduleName,
                root,
                additionalOutputDirectories,
                enableFullReparsePointResolving,
                nodeLocation);
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment,
            string yarnLocation = "",
            string moduleName = "Test",
            string root = "d`.`",
            string additionalOutputDirectories = null,
            bool? enableFullReparsePointResolving = null,
            string nodeLocation = "")
        {
            environment ??= new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
            };

            // On Linux/Mac, yarn depends on low level tools like sed, readlink, etc. If the value for path is not configured as a passthrough, inject /usr/bin
            if (!OperatingSystemHelper.IsWindowsOS && environment.TryGetValue("PATH", out var value) && value.GetValue() is string path)
            { 
                environment["PATH"] = new DiscriminatingUnion<string, UnitValue>(path + Path.PathSeparator + "/usr/bin");
            }

            // We reserve the null string for a true undefined.
            if (yarnLocation == string.Empty)
            {
                yarnLocation = $"f`{PathToYarn}`";
            }

            // We reserve the null string for a true undefined.
            if (nodeLocation == string.Empty)
            {
                nodeLocation = $"f`{PathToNode}`";
            }

            // Let's explicitly pass an environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultYarnPrelude(
                    environment: environment,
                    yarnLocation: yarnLocation,
                    moduleName: moduleName,
                    root: root,
                    additionalOutputDirectories,
                    nodeLocation: nodeLocation,
                    enableFullReparsePointResolving));
        }

        protected BuildXLEngineResult RunYarnProjects(
            ICommandLineConfiguration config,
            TestCache testCache = null, 
            IDetoursEventListener detoursListener = null)
        {
            // This bootstraps the 'repo'
            if (!YarnInit(config.Layout.SourceDirectory))
            {
                throw new InvalidOperationException("Yarn init failed.");
            }

            return RunEngine(config, testCache, detoursListener);
        }

        /// <summary>
        /// Runs the engine for a given config, assuming the Yarn repo is already initialized
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

        private string DefaultYarnPrelude(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment,
            string yarnLocation,
            string moduleName,
            string root,
            string additionalOutputDirectories,
            string nodeLocation,
            bool? enableFullReparsePointResolving = null) => $@"
config({{
    resolvers: [
        {{
            kind: 'Yarn',
            moduleName: '{moduleName}',
            root: {root},
            {(nodeLocation != null ? $"nodeExeLocation: {nodeLocation}," : string.Empty)}
            {DictionaryToExpression("environment", environment)}
            {(yarnLocation != null ? $"yarnLocation: {yarnLocation}," : string.Empty)}
            {(additionalOutputDirectories != null ? $"additionalOutputDirectories: {additionalOutputDirectories}," : string.Empty)}
        }}
    ],
    {(enableFullReparsePointResolving != null ? $"sandbox: {{unsafeSandboxConfiguration: {{enableFullReparsePointResolving: {(enableFullReparsePointResolving.Value ? "true" : "false")}}}}}" : string.Empty)}
}});";

        private static string DictionaryToExpression(string memberName, Dictionary<string, DiscriminatingUnion<string, UnitValue>> dictionary)
        {
            return (dictionary == null ?
                string.Empty :
                $"{memberName}: Map.empty<string, (PassthroughEnvironmentVariable | string)>(){ string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', {(property.Value?.GetValue() is UnitValue ? "Unit.unit()" : $"'{property.Value?.GetValue()}'")})")) },");
        }

        /// <summary>
        /// Initializes a Yarn repo at the target directory
        /// </summary>
        protected bool YarnInit(AbsolutePath targetDirectory, string packagesPattern = "src/*")
        {
            // Create a package.json, root of all the workspaces. This package needs to be private
            // since workspaces need to be declared in a private one
            var result = YarnRun(targetDirectory.ToString(PathTable), "init --private --yes");

            if (!result)
            {
                return false;
            }

            // Update the root package.json to enable workspaces
            var pathToPackageJson = targetDirectory.Combine(PathTable, "package.json").ToString(PathTable);
            string mainJson = File.ReadAllText(pathToPackageJson);
            int closingBracket = mainJson.LastIndexOf('}');
            mainJson = mainJson.Insert(closingBracket, $@",
  ""workspaces"": {{
    ""packages"": [
      ""{packagesPattern}""
    ]}}");
            File.WriteAllText(pathToPackageJson, mainJson);

            return YarnRun(targetDirectory.ToString(PathTable), "install");
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
