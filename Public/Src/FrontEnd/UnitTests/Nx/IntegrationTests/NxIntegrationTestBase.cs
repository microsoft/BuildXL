// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities;
using Test.DScript.Ast;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Nx
{
    /// <summary>
    /// Provides facilities to run the engine adding Nx specific artifacts.
    /// </summary>
    public abstract class NxIntegrationTestBase : DsTestWithCacheBase
    {
        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected string PathToNode => Path.Combine(TestDeploymentDir, "node", OperatingSystemHelper.IsLinuxOS ? "bin/node" : "node.exe").Replace("\\", "/");

        private string PathToNxDeployment => Path.Combine(TestDeploymentDir, "nx-deployment");
        /// <summary>
        /// Keep in sync with deployment.
        /// </summary>
        protected virtual string PathToNx => Path.Combine(PathToNxDeployment, "nx", ".bin", OperatingSystemHelper.IsWindowsOS ? "nx.cmd" : "nx").Replace("\\", "/");

        /// <nodoc/>
        protected string PathToNodeFolder => Path.GetDirectoryName(PathToNode).Replace("\\", "/");

        /// <nodoc/>
        protected virtual string PathToNxFolder => Path.Combine(PathToNxDeployment, "nx").Replace("\\", "/");

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

        protected NxIntegrationTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Nx.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.JavaScript.ETWLogger.Log);

            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
            OutDir = "target";
        }

        /// <nodoc/>
        protected SpecEvaluationBuilder Build(
        Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment = null,
            string moduleName = "Test",
            string root = "d`.`",
            string executeCommands = null,
            string nxLibLocation = null)
        {
            environment ??= new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
                ["NODE_PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNxDeployment),
            };

            // On Linux/Mac, Lage depends on low level tools like sed, readlink, etc. If the value for path is not configured as a passthrough, inject /usr/bin
            if (!OperatingSystemHelper.IsWindowsOS && environment.TryGetValue("PATH", out var value) && value.GetValue() is string path)
            { 
                environment["PATH"] = new DiscriminatingUnion<string, UnitValue>(path + Path.PathSeparator + "/usr/bin");
            }

            nxLibLocation ??= PathToNxFolder;

            // Let's explicitly pass an environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultNxPrelude(
                    environment: environment,
                    moduleName: moduleName,
                    root: root,
                    executeCommands: executeCommands,
                    nxLibLocation: nxLibLocation));
        }

        protected BuildXLEngineResult RunNxProjects(
            ICommandLineConfiguration config,
            TestCache testCache = null, 
            IDetoursEventListener detoursListener = null)
        {
            BootstrapNx(config);

            return RunEngine(config, testCache, detoursListener);
        }

        protected void BootstrapNx(ICommandLineConfiguration config)
        {
            // This bootstraps the 'repo'
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, "package.json").ToString(PathTable), $@"
            {{
                ""private"": true,
                ""name"": ""NxTest"",
                ""version"": ""1.0.0"",
                ""workspaces"": {{
                    ""packages"": [
                      ""src/*""
                    ]
                }},
                ""scripts"": {{
                    ""nx"" : ""{PathToNx}""
                }}
            }}");

            // Minimal nx.json that sets up a default target dependency (so builds will work out of the box)
            File.WriteAllText(Path.Combine(config.Layout.SourceDirectory.ToString(PathTable), "nx.json"),
                 @"{
                    ""$schema"": ""./node_modules/nx/schemas/nx-schema.json"",
                    ""targetDefaults"": {
                        ""build"": {
                            ""dependsOn"": [""^build""]
                        }
                    }
                }");
        }

        /// <summary>
        /// Runs the engine for a given config, assuming the Nx repo is already initialized
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

        private string DefaultNxPrelude(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment,
            string moduleName,
            string root,
            string executeCommands,
            string nxLibLocation) => $@"
config({{
    resolvers: [
        {{
            kind: 'Nx',
            moduleName: '{moduleName}',
            root: {root},
            nodeExeLocation: f`{PathToNode}`,
            // Just for debugging purposes
            keepProjectGraphFile: true,
            {DictionaryToExpression("environment", environment)}
            {(executeCommands == null ? string.Empty : $"execute: {executeCommands},")}
            {(nxLibLocation == null ? string.Empty : $"nxLibLocation: d`{nxLibLocation}`,")}
            // Npm insists on writing logs to the user profile folder
            untrackedDirectoryScopes: [ d`{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.npm`]
        }}
    ],
}});";

        private static string DictionaryToExpression(string memberName, Dictionary<string, DiscriminatingUnion<string, UnitValue>> dictionary)
        {
            return (dictionary == null ?
                string.Empty :
                $"{memberName}: Map.empty<string, (PassthroughEnvironmentVariable | string)>(){ string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', {(property.Value?.GetValue() is UnitValue ? "Unit.unit()" : $"'{property.Value?.GetValue()}'")})")) },");
        }
    }
}
