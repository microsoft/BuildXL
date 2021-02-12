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
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public abstract class CustomJavaScriptIntegrationTestBase : DsTestWithCacheBase
    {
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

        protected CustomJavaScriptIntegrationTestBase(ITestOutputHelper output) : base(output, true)
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

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(
            string customGraph,
            string customScripts,
            string moduleName = "Test")
        {
            // Let's explicitly pass an environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultCustomJavaScriptPrelude(
                    customGraph: customGraph,
                    customScripts: customScripts,
                    moduleName: moduleName));
        }

        protected BuildXLEngineResult RunCustomJavaScriptProjects(
            ICommandLineConfiguration config,
            TestCache testCache = null, 
            IDetoursEventListener detoursListener = null)
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

        private string DefaultCustomJavaScriptPrelude(
            string moduleName,
            string customGraph,
            string customScripts) => $@"
config({{
    resolvers: [
        {{
            kind: 'CustomJavaScript',
            moduleName: '{moduleName}',
            root: d`.`,
            customProjectGraph: {customGraph},
            {(customScripts != null? $"customScripts: {customScripts}," : string.Empty)}
        }}
    ]
}});";
    }
}
