// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Processes;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities;
using Test.DScript.Ast;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Provides facilities to run the engine adding Download specific artifacts.
    /// </summary>
    public abstract class DownloadResolverIntegrationTestBase : DsTestWithCacheBase
    {
        private const string TestServer = "http://localhost:9753/";

        private readonly RequestCount m_webRequestCount = new RequestCount();
        private readonly AlternativeDataIndicator m_alternativeDataIndicator = new AlternativeDataIndicator();

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

        protected DownloadResolverIntegrationTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Download.ETWLogger.Log);

            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
            OutDir = "target";
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(IDownloadFileSettings[] settings)
        {
            // Let's explicitly pass an environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultDownloadPrelude(settings));
        }

        /// <summary>
        /// Runs the engine for a given config startig the http server before the test runs
        /// </summary>
        protected BuildXLEngineResult RunEngineWithServer(ICommandLineConfiguration config)
        {
            return StartServerAndRunTest(() => RunEngine(config));
        }

        /// <summary>
        /// Runs the engine for a given config
        /// </summary>
        protected BuildXLEngineResult RunEngine(ICommandLineConfiguration config, TestCache testCache = null, IDetoursEventListener detoursListener = null)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                ((CommandLineConfiguration)config).Engine.Phase = Phase;
                ((CommandLineConfiguration)config).Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;
                ((CommandLineConfiguration)config).Sandbox.OutputReportingMode = OutputReportingMode.FullOutputAlways;
                ((CommandLineConfiguration)config).FrontEnd.AllowMissingSpecs = true;

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

        protected IDownloadFileSettings GetSampleData(string url, DownloadArchiveType archiveType, ContentHash? contentHash = null)
        {
            return new DownloadFileSettings
            {
                ModuleName = "TestDownload",
                ArchiveType = archiveType,
                Url = url,
                Hash = contentHash?.ToString()
            };
        }

        private string DefaultDownloadPrelude(
            IDownloadFileSettings[] settings) => $@"
config({{
    resolvers: [
        {{
            kind: 'Download',
            downloads: [
                {string.Join(',', settings.Select(setting => DownloadSettingsToDScript(setting)))}
            ]
        }},
        {{
            kind: 'DScript',
            modules: [{{moduleName: 'Test', projects: [f`test.dsc`] }}]
        }},
    ],
}}); ";

        private string DownloadSettingsToDScript(IDownloadFileSettings settings)
        {
            return $@"
{{
    moduleName: '{settings.ModuleName}',
    url: '{settings.Url}',
    archiveType: '{settings.ArchiveType.ToString().ToLower(CultureInfo.InvariantCulture)}',
    {(!string.IsNullOrEmpty(settings.Hash) ? $"hash: '{settings.Hash}'," : string.Empty)}
    {(!string.IsNullOrEmpty(settings.ExtractedValueName) ? $"extractedValueName: '{settings.ExtractedValueName}'," : string.Empty)}
    {(!string.IsNullOrEmpty(settings.DownloadedValueName) ? $"downloadedValueName: '{settings.DownloadedValueName}'," : string.Empty)}
}}";
        }

        /// <summary>
        /// Starts an HTTP server for testing purposes and runs a test in its context
        /// </summary>
        protected BuildXLEngineResult StartServerAndRunTest(Func<BuildXLEngineResult> performTest)
        {
            using (var listener = new HttpListener())
            {
                // This test relies on the mutex in the build engine to only run one unittest at a time and this assembly to be single thread
                // if any of those assumptions will be broken we will have to either dynamically (remind you globally) get unique ports.
                // HttpListner doesn't have this built-in so there will always be a race. Just spam the ports until one doesn't fail
                // use a global mutex (This is not honored by qtest since it can run in a different session on cloudbuild).
                listener.Prefixes.Add(TestServer);
                listener.Start();

                TestRequestHandler.StartRequestHandler(listener, m_alternativeDataIndicator, m_webRequestCount);

                try
                {
                    return performTest();
                }
                finally 
                {
                    listener.Stop();
                    listener.Close();
                }
            }
        }
    }

}
