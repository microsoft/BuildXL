// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Processes;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities;
using Test.DScript.Ast;
using Xunit;

namespace Test.BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Provides facilities to run the engine adding Download specific artifacts.
    /// </summary>
    public abstract class DownloadResolverIntegrationTestBase : DsTestWithCacheBase
    {
        private HttpListener m_listener;

        protected string TestServer
        {
            get
            {
                if (m_listener == null)
                {
                    // Publish the URL only after startup succeeds, and retain it across graph reloads.
                    m_listener = CreateAndStartListener();
                    TestRequestHandler.StartRequestHandler(m_listener, m_alternativeDataIndicator, m_webRequestCount);
                }

                return m_listener.Prefixes.Single();
            }
        }

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

        protected static int GetTestServerPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        protected HttpListener CreateAndStartListener(Func<int> getPort = null)
        {
            const int MaxAttempts = 10;
            getPort = getPort ?? GetTestServerPort;
            for (int attempt = 1; ; attempt++)
            {
                var listener = new HttpListener();
                try
                {
                    listener.Prefixes.Add($"http://localhost:{getPort()}/");
                    listener.Start();
                    return listener;
                }
                catch (HttpListenerException exception) when (attempt < MaxAttempts && IsAddressInUse(exception))
                {
                    Output.WriteLine($"HTTP test listener address conflict on attempt {attempt}/{MaxAttempts}: {exception}");
                }
                finally
                {
                    if (!listener.IsListening)
                    {
                        listener.Close();
                    }
                }
            }
        }

        private static bool IsAddressInUse(HttpListenerException exception)
        {
            const int ErrorAlreadyExists = 183;
            const int ErrorSharingViolation = 32;

            // Unix HttpListener reports duplicate prefixes as HTTP 400, or propagates a native socket bind error.
            return exception.NativeErrorCode == new SocketException((int)SocketError.AddressAlreadyInUse).NativeErrorCode
                || (OperatingSystemHelper.IsWindowsOS
                    ? exception.NativeErrorCode == ErrorAlreadyExists || exception.NativeErrorCode == ErrorSharingViolation
                    : exception.NativeErrorCode == (int)HttpStatusCode.BadRequest);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    m_listener?.Close();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(IDownloadFileSettings[] settings)
        {
            // Let's explicitly pass an environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultDownloadPrelude(settings));
        }

        /// <summary>
        /// Runs the engine with the HTTP server started when <see cref="TestServer"/> was first accessed.
        /// </summary>
        protected BuildXLEngineResult RunEngineWithServer(ICommandLineConfiguration config, TestCache testCache = null)
        {
            Assert.True(m_listener?.IsListening == true);
            return RunEngine(config, testCache);
        }

        /// <summary>
        /// Runs the engine for a given config
        /// </summary>
        protected BuildXLEngineResult RunEngine(ICommandLineConfiguration config, TestCache testCache = null, IDetoursEventListener detoursListener = null)
        {
            // Anonymous download tests must not discover host credential providers. Discovery tests supply their own paths.
            var commandLineConfig = (CommandLineConfiguration)config;
            if (!commandLineConfig.Startup.Properties.ContainsKey("NUGET_CREDENTIALPROVIDERS_PATH"))
            {
                commandLineConfig.Startup.Properties["NUGET_CREDENTIALPROVIDERS_PATH"] = string.Empty;
            }

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
    {(settings.RequiredFiles == null ? string.Empty : $"requiredFiles: [{string.Join(", ", settings.RequiredFiles.Select(file => $"r`{file.ToString(FrontEndContext.StringTable, global::BuildXL.Utilities.Core.PathFormat.Script)}`"))}],")}
}}";
        }
    }

}
