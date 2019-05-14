// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using InitializationLogger = global::BuildXL.FrontEnd.Core.Tracing.Logger;

namespace Test.BuildXL.Engine
{
    public class BaseEngineTest : TemporaryStorageTestBase
    {
        protected EngineContext Context { get; set; }

        protected CommandLineConfiguration Configuration { get; private set; }

        private Logger ParseAndEvaluateLogger { get; }

        private InitializationLogger InitializationLogger { get; }

        protected List<AbsolutePath> MainSourceResolverModules { get; }

        private EngineTestHooksData m_testHooks;

        protected string TestRoot => Path.Combine(TemporaryDirectory, "src");

        private readonly ITestOutputHelper m_testOutput;

        protected IMutableFileSystem FileSystem { get; }

        protected BaseEngineTest(ITestOutputHelper output)
            : base(output)
        {
            m_testOutput = output;
            m_ignoreWarnings = OperatingSystemHelper.IsUnixOS; // ignoring /bin/sh is being used as a source file 

            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);

            ParseAndEvaluateLogger = Logger.CreateLogger();
            InitializationLogger = InitializationLogger.CreateLogger();

            var pathTable = new PathTable();
            FileSystem = new PassThroughMutableFileSystem(pathTable);
            Context = EngineContext.CreateNew(CancellationToken.None, pathTable, FileSystem);
            MainSourceResolverModules = new List<AbsolutePath>();

            var rootPath = AbsolutePath.Create(Context.PathTable, TestRoot);
            var logsPath = Combine(AbsolutePath.Create(Context.PathTable, TemporaryDirectory), "logs");

            Configuration = new CommandLineConfiguration()
            {
                DisableDefaultSourceResolver = true,
                Resolvers = new List<IResolverSettings>
                        {
                            new SourceResolverSettings
                            {
                                Kind = "SourceResolver",
                                Modules = MainSourceResolverModules,
                            },
                            new SourceResolverSettings
                            {
                                Kind = "SourceResolver",
                                Modules = new List<AbsolutePath>
                                {
                                    AbsolutePath.Create(Context.PathTable, Path.Combine(GetTestExecutionLocation(), "Sdk", "Prelude", "package.config.dsc")),
                                    AbsolutePath.Create(Context.PathTable, Path.Combine(GetTestExecutionLocation(), "Sdk", "Transformers", "package.config.dsc")),
                                    AbsolutePath.Create(Context.PathTable, Path.Combine(GetTestExecutionLocation(), "Sdk", "Deployment", "module.config.dsc")),
                                },
                            },
                        },
                Layout =
                    {
                        SourceDirectory = rootPath,
                        OutputDirectory = Combine(rootPath, "out"),
                        ObjectDirectory = Combine(rootPath, "obj"),
                        CacheDirectory = Combine(AbsolutePath.Create(Context.PathTable, TemporaryDirectory), "cache"),
                    },
                Cache =
                    {
                        CacheSpecs = SpecCachingOption.Disabled,
                        CacheLogFilePath = logsPath.Combine(Context.PathTable, PathAtom.Create(Context.StringTable, "cache.log")),
                    },
                Engine =
                    {
                        ReuseEngineState = false,
                        LogStatistics = false,
                        TrackBuildsInUserFolder = false,
                    },
                FrontEnd =
                {
                    MaxFrontEndConcurrency = 1,
                    LogStatistics = false,
                },
                Schedule =
                {
                    MaxIO = 1,
                    MaxProcesses = 1,
                },
                Sandbox =
                {
                    FileSystemMode = FileSystemMode.RealAndMinimalPipGraph,
                    OutputReportingMode = OutputReportingMode.FullOutputOnError,
                },
                Logging =
                    {
                        LogsDirectory = logsPath,
                        LogStats = false,
                        LogExecution = false,
                        LogCounters = false,
                        LogMemory = false,
                        StoreFingerprints = false,
                        NoWarnings =
                        {
                            909, // Disable warnings about experimental feature
                        },
                    }
            };

            AbsolutePath Combine(AbsolutePath parent, string name)
            {
                return parent.Combine(Context.PathTable, PathAtom.Create(Context.StringTable, name));
            }
        }

        protected string GetOsShellCmdToolDefinition()
        {
            return $@"<Transformer.ToolDefinition>{{
                exe: f`{CmdHelper.OsShellExe}`,
                dependsOnWindowsDirectories: true,
                runtimeDependencies: { CmdHelper.GetCmdDependenciesAsArrayLiteral(Context.PathTable) },
                untrackedDirectoryScopes: { CmdHelper.GetOsShellDependencyScopesAsArrayLiteral(Context.PathTable)}
            }}";
        }

        protected string GetExecuteFunction()
        {
            return @"
function execute(args: Transformer.ExecuteArguments): Transformer.ExecuteResult {{
    Debug.writeLine(Debug.dumpData(args.tool.exe) + ' ' + Debug.dumpArgs(args.arguments));
    return Transformer.execute(args);
}}";
        }


        protected void AddSdk(string sdkLocation)
        {
            Configuration.Resolvers.Add(
                new SourceResolverSettings
                {
                    Kind = "SourceResolver",
                    Modules = new List<AbsolutePath>
                              {
                                  AbsolutePath.Create(Context.PathTable, sdkLocation),
                              },
                });
        }

        protected void LogTestMarker(string message)
        {
            m_testOutput.WriteLine("################################################################################");
            m_testOutput.WriteLine("## " + message);
            m_testOutput.WriteLine("################################################################################");
        }

        protected EngineTestHooksData TestHooks
        {
            get
            {
                if (m_testHooks != null)
                {
                    return m_testHooks;
                }

                // Write out a dummy deployment manifest for the test to use
                string manifestPath = Path.Combine(TemporaryDirectory, AppDeployment.DeploymentManifestFileName);
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(manifestPath), AppDeployment.DeploymentManifestFileName),
                    AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                var appDeployment = AppDeployment.ReadDeploymentManifest(
                    Path.GetDirectoryName(manifestPath),
                    AppDeployment.DeploymentManifestFileName,
                    skipManifestCheckTestHook: true);

                m_testHooks = new EngineTestHooksData()
                              {
                                  AppDeployment = appDeployment,
                                  DoWarnForVirusScan = false,
                                  CacheFactory = () => new EngineCache(
                                     new InMemoryArtifactContentCache(),
                                     new InMemoryTwoPhaseFingerprintStore())
                              };

                return m_testHooks;
            }
        }

        private BuildXLEngine CreateEngine(bool rememberAllChangedTrackedInputs)
        {
            IFrontEndController Create(PathTable pathTable, SymbolTable symbolTable)
            {
                var frontEndStatistics = new FrontEndStatistics();
                var moduleRegistry = new ModuleRegistry(symbolTable);

                var workspaceFactory = new DScriptWorkspaceResolverFactory();
                workspaceFactory.RegisterResolver(KnownResolverKind.SourceResolverKind,
                    () => new WorkspaceSourceModuleResolver(pathTable.StringTable, frontEndStatistics, ParseAndEvaluateLogger));
                workspaceFactory.RegisterResolver(KnownResolverKind.DScriptResolverKind,
                    () => new WorkspaceSourceModuleResolver(pathTable.StringTable, frontEndStatistics, ParseAndEvaluateLogger));
                workspaceFactory.RegisterResolver(KnownResolverKind.DefaultSourceResolverKind,
                    () => new WorkspaceDefaultSourceModuleResolver(pathTable.StringTable, frontEndStatistics, ParseAndEvaluateLogger));

                var frontEndFactory = FrontEndFactory.CreateInstanceForTesting(
                    () => new ConfigurationProcessor(new FrontEndStatistics(), ParseAndEvaluateLogger),
                    new DScriptFrontEnd(frontEndStatistics, ParseAndEvaluateLogger));

                var evaluationScheduler = new EvaluationScheduler(degreeOfParallelism: 1);
                return new FrontEndHostController(
                    frontEndFactory,
                    workspaceFactory,
                    evaluationScheduler,
                    moduleRegistry,
                    new FrontEndStatistics(),
                    logger: InitializationLogger, 
                    collector: null,
                    collectMemoryAsSoonAsPossible: false);
            }

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(Configuration, Context.PathTable, bxlExeLocation: null, inTestMode: true);
            var successfulValdiation = BuildXLEngine.PopulateAndValidateConfiguration(Configuration, Configuration, Context.PathTable, LoggingContext);
            Assert.True(successfulValdiation);

            var engine = BuildXLEngine.Create(LoggingContext, Context, Configuration, new LambdaBasedFrontEndControllerFactory(Create), rememberAllChangedTrackedInputs: rememberAllChangedTrackedInputs);

            engine.TestHooks = TestHooks;

            return engine;
        }

        protected void ConfigureInMemoryCache(TestCache testCache)
        {
            TestHooks.CacheFactory = () => new EngineCache(
                testCache.GetArtifacts(),
                testCache.Fingerprints);
        }

        protected void ConfigureCache(CacheInitializer cacheInitializer)
        {
            TestHooks.CacheFactory = () => cacheInitializer.CreateCacheForContext();
        }

        /// <summary>
        /// Returns a cache initializer to a real instance of the cache
        /// </summary>
        protected CacheInitializer GetRealCacheInitializerForTests()
        {
            var tempDir = OperatingSystemHelper.IsUnixOS ? "/tmp/buildxl-temp" : TemporaryDirectory;
            string cacheDirectory = Path.Combine(tempDir, "cache");
            AbsolutePath cacheConfigPath = WriteTestCacheConfigToDisk(cacheDirectory);

            var translator = new RootTranslator();
            translator.Seal();

            var maybeCacheInitializer = CacheInitializer.GetCacheInitializationTask(
                LoggingContext,
                Context.PathTable,
                cacheDirectory,
                new CacheConfiguration
                {
                    CacheLogFilePath = AbsolutePath.Create(Context.PathTable, tempDir).Combine(Context.PathTable, "cache.log"),
                    CacheConfigFile = cacheConfigPath
                },
                translator,
                recoveryStatus: false,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            if (!maybeCacheInitializer.Succeeded)
            {
                throw new BuildXLException("Unable to initialize the real cache: " + maybeCacheInitializer.Failure.DescribeIncludingInnerFailures());
            }

            return maybeCacheInitializer.Result;
        }

        /// <summary>
        /// Writes a real cache configuration to disk an returns its location
        /// </summary>
        protected AbsolutePath WriteTestCacheConfigToDisk(string cacheDirectory, string cacheConfigJson = null)
        {
            if (cacheConfigJson == null)
            {
                cacheConfigJson = GetTestCacheConfigContent(cacheDirectory);
            }

            var cacheConfigPath = Path.Combine(TemporaryDirectory, "cache.config.json");

            File.WriteAllText(cacheConfigPath, cacheConfigJson);

            return AbsolutePath.Create(Context.PathTable, cacheConfigPath);
        }

        /// <summary>
        /// Provides a simple configuration for a real instance of the cache
        /// </summary>
        private static string GetTestCacheConfigContent(string cacheDirectory)
        {
            return $@"{{
    ""MaxCacheSizeInMB"":  1024,
    ""CacheId"":  ""TestCache"",
    ""Assembly"":  ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""CacheLogPath"":  ""[BuildXLSelectedLogPath]"",
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
    ""CacheRootPath"":  ""{cacheDirectory.Replace("\\", "\\\\")}"",
    ""UseStreamCAS"":  true
}}";
        }

        public void SetConfig(string configFileContents = null)
        {
            Directory.CreateDirectory(TestRoot);
            var configFile = Path.Combine(TestRoot, "config.dsc");

            // Write the file
            File.WriteAllText(configFile, configFileContents ?? "config({});");

            var configFilePath = AbsolutePath.Create(Context.PathTable, configFile);
            Configuration.Startup.ConfigFile = configFilePath;
            Configuration.Layout.PrimaryConfigFile = configFilePath;
        }

        public void AddModule(string moduleName, (string FileName, string Content) spec, bool placeInRoot = false)
        {
            AddModule(moduleName, new [] {spec}, placeInRoot);
        }

        public void AddModule(string moduleName, IEnumerable<(string FileName, string Content)> specs, bool placeInRoot = false)
        {
            AddModule(
                moduleName,
                I($@"module({{
    name: ""{moduleName}"",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [{string.Join(",", specs.Select(s => "f`" + s.FileName + "`"))}]
}});
"),
                specs,
                placeInRoot);
        }

        public void AddModule(string moduleName, string moduleContent, IEnumerable<(string FileName, string Content)> specs, bool placeInRoot = false)
        {
            var moduleFolder = placeInRoot 
                ? TestRoot
                : Path.Combine(TestRoot, moduleName);
            var moduleConfigFile = Path.Combine(moduleFolder, "module.config.dsc");

            // Write the files
            Directory.CreateDirectory(moduleFolder);
            File.WriteAllText(moduleConfigFile, moduleContent);
            foreach (var spec in specs)
            {
                var filePath = Path.Combine(moduleFolder, spec.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, spec.Content);
            }

            MainSourceResolverModules.Add(AbsolutePath.Create(Context.PathTable, moduleConfigFile));
        }

        public void AddFile(string filePath, string fileContents)
        {
            var fullFilePath = Path.Combine(TestRoot, filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullFilePath));
            File.WriteAllText(fullFilePath, fileContents);
        }

        protected void AppendNewLine(AbsolutePath file)
        {
            AppendNewLine(file.ToString(Context.PathTable));
        }

        protected void AppendNewLine(string filePath)
        {
            using (var writer = File.AppendText(filePath))
            {
                writer.WriteLine();
            }
        }

        public void RunEngine(string testMarker = null, bool expectSuccess = true, bool rememberAllChangedTrackedInputs = false, bool captureFrontEndAbstraction = false)
        {
            if (!string.IsNullOrEmpty(testMarker))
            {
                LogTestMarker(testMarker);
            }

            if (!Configuration.Startup.ConfigFile.IsValid)
            {
                SetConfig();
            }

            if (captureFrontEndAbstraction)
            {
                TestHooks.FrontEndEngineAbstraction = new BoxRef<FrontEndEngineAbstraction>();
            }

            var engine = CreateEngine(rememberAllChangedTrackedInputs);
            var result = engine.Run(LoggingContext, engineState: null);


            var assertMarker =  testMarker == null ? string.Empty : " (" + testMarker + ")";
            // Little bit more verbose but the xunit log will be much clearer about what the expected outcome is.
            if (expectSuccess)
            {
                Assert.True(result.IsSuccess, "Expecting build to pass successfully." + assertMarker);
            }
            else
            {
                Assert.False(result.IsSuccess, "Expecting build to fail." + assertMarker);
            }

            // The Engine might have had a graph cache hit, and replaced the context, so swizzle it out as well as the configuration
            Context = engine.Context;
        }
    }
}
