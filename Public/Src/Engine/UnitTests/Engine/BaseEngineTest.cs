// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.ViewModel;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
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

        protected List<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>> MainSourceResolverModules { get; private set; }

        private EngineTestHooksData m_testHooks;

        protected string TestRoot => Path.Combine(TemporaryDirectory, "src");

        protected ITestOutputHelper TestOutput { get; }

        protected IMutableFileSystem FileSystem { get; private set; }

        protected bool HasCacheInitializer { get; set; }

        protected BaseEngineTest(ITestOutputHelper output)
            : base(output)
        {
            TestOutput = output;
            m_ignoreWarnings = OperatingSystemHelper.IsUnixOS; // ignoring /bin/sh is being used as a source file 

            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);

            ParseAndEvaluateLogger = Logger.CreateLoggerWithTracking();
            InitializationLogger = InitializationLogger.CreateLogger();

            RestartEngine();
        }

        protected void RestartEngine()
        {
            var pathTable = new PathTable();
            FileSystem = new PassThroughMutableFileSystem(pathTable);
            Context = EngineContext.CreateNew(CancellationToken.None, pathTable, FileSystem);
            MainSourceResolverModules = new List<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>>();

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
                                Modules = new List<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>>
                                {
                                    new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                                        AbsolutePath.Create(Context.PathTable, Path.Combine(GetTestExecutionLocation(), "Sdk", "Prelude", "package.config.dsc"))),
                                    new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                                        AbsolutePath.Create(Context.PathTable, Path.Combine(GetTestExecutionLocation(), "Sdk", "Transformers", "package.config.dsc"))),
                                    new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                                        AbsolutePath.Create(Context.PathTable, Path.Combine(GetTestExecutionLocation(), "Sdk", "Deployment", "module.config.dsc"))),
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

            if (TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
            {
                // Directory translation is needed here particularly when the test temporary directory
                // is inside a directory that is actually a junction to another place. 
                // For example, the temporary directory is D:\src\BuildXL\Out\Object\abc\t_1, but
                // the path D:\src\BuildXL\Out or D:\src\BuildXL\Out\Object is a junction to K:\Out.
                // Some tool, like cmd, can access the path in K:\Out, and thus the test will have a DFA
                // if there's no directory translation.
                // This problem does not occur when only substs are involved, but no junctions. The method
                // TryGetSubstSourceAndTarget works to get translations due to substs or junctions.
                AbsolutePath substSourcePath = AbsolutePath.Create(Context.PathTable, substSource);
                AbsolutePath substTargetPath = AbsolutePath.Create(Context.PathTable, substTarget);
                Configuration.Engine.DirectoriesToTranslate.Add(
                    new TranslateDirectoryData(I($"{substSource}<{substTarget}"), substSourcePath, substTargetPath));
            }
        }

        protected AbsolutePath Combine(AbsolutePath parent, string name)
        {
            return parent.Combine(Context.PathTable, PathAtom.Create(Context.StringTable, name));
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
                    Modules = new List<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>>
                              {
                                  new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                                      AbsolutePath.Create(Context.PathTable, sdkLocation)),
                              },
                });
        }

        protected void LogTestMarker(string message)
        {
            TestOutput.WriteLine("################################################################################");
            TestOutput.WriteLine("## " + message);
            TestOutput.WriteLine("################################################################################");
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

                var frontEndFactory = FrontEndFactory.CreateInstanceForTesting(
                    () => new ConfigurationProcessor(new FrontEndStatistics(), ParseAndEvaluateLogger),
                    new DScriptFrontEnd(frontEndStatistics, ParseAndEvaluateLogger));

                var evaluationScheduler = new EvaluationScheduler(degreeOfParallelism: 1);
                return new FrontEndHostController(
                    frontEndFactory,
                    evaluationScheduler,
                    moduleRegistry,
                    new FrontEndStatistics(),
                    logger: InitializationLogger, 
                    collector: null,
                    collectMemoryAsSoonAsPossible: false);
            }

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(Configuration, Context.PathTable, bxlExeLocation: null, inTestMode: true);
            var successfulValidation = BuildXLEngine.PopulateAndValidateConfiguration(Configuration, Configuration, Context.PathTable, LoggingContext);
            Assert.True(successfulValidation);

            var engine = BuildXLEngine.Create(LoggingContext, Context, Configuration, new LambdaBasedFrontEndControllerFactory(Create), new BuildViewModel(), rememberAllChangedTrackedInputs: rememberAllChangedTrackedInputs);
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
            HasCacheInitializer = true;
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
            if (TryGetSubstSourceAndTarget(out var substSource, out var substTarget))
            {
                translator.AddTranslation(substTarget, substSource);
            }

            translator.Seal();

            Configuration.Cache.CacheConfigFile = cacheConfigPath;
            Configuration.Cache.CacheLogFilePath = AbsolutePath.Create(Context.PathTable, tempDir).Combine(Context.PathTable, "cache.log");

            var maybeCacheInitializer = CacheInitializer.GetCacheInitializationTask(
                LoggingContext,
                Context.PathTable,
                cacheDirectory,
                Configuration.Cache,
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
    ""UseStreamCAS"":  true,
    ""VfsCasRoot"": ""[VfsCasRoot]""
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

        public void AddModule(string moduleName, (string FileName, string Content) spec, bool placeInRoot = false, IMount[] moduleMounts = null)
        {
            AddModule(moduleName, new [] {spec}, placeInRoot, moduleMounts);
        }

        public void AddModule(string moduleName, IEnumerable<(string FileName, string Content)> specs, bool placeInRoot = false, IMount[] moduleMounts = null)
        {
            AddModule(
                moduleName,
                I($@"module({{
    name: ""{moduleName}"",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [{string.Join(",", specs.Select(s => "f`" + s.FileName + "`"))}],
    mounts: [{string.Join(",", moduleMounts?.Select(m => MountToExpression(m)) ?? CollectionUtilities.EmptyArray<string>())}],
}});
"),
                specs,
                placeInRoot);
        }

        private string MountToExpression(IMount mount)
        {
            return @$"{{
                name: a`{mount.Name.ToString(Context.StringTable)}`, 
                path: p`{mount.Path.ToString(Context.PathTable)}`,
                trackSourceFileChanges: {toBool(mount.TrackSourceFileChanges)},
                isWritable: {toBool(mount.IsWritable)},
                isReadable: {toBool(mount.IsReadable)},
                isScrubbable: {toBool(mount.IsScrubbable)},
            }}";

            string toBool(bool b) => b ? "true" : "false";
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

            MainSourceResolverModules.Add(new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                AbsolutePath.Create(Context.PathTable, moduleConfigFile)));
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

        public EngineState RunEngine(string testMarker = null, bool expectSuccess = true, bool rememberAllChangedTrackedInputs = false, bool captureFrontEndAbstraction = false, EngineState engineState = null)
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
            var result = engine.Run(LoggingContext, engineState: engineState);


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

            return result.EngineState;
        }
    }
}
