// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Testing.Helper.Ambients;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.TestUtilities;
using Xunit.Sdk;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <summary>
    /// Class that helps run DScript tests
    /// </summary>
    public sealed class TestRunner
    {
        // Setup Logging
        private readonly BuildXL.FrontEnd.Core.Tracing.Logger m_tracingLogger;
        private readonly Script.Tracing.Logger m_astLogger;
        private readonly BuildXL.Scheduler.Tracing.Logger m_schedulerLogger;
        private readonly Action<Diagnostic> m_diagnosticHandler;
        private readonly bool m_updateFailedTests;

        /// <nodoc />
        public TestRunner(Action<Diagnostic> diagnosticHandler, bool updateFailedTests)
        {
            m_tracingLogger = BuildXL.FrontEnd.Core.Tracing.Logger.CreateLogger(true);
            m_astLogger = Script.Tracing.Logger.CreateLogger(true);
            m_schedulerLogger = BuildXL.Scheduler.Tracing.Logger.CreateLogger(true);
            m_diagnosticHandler = diagnosticHandler;
            m_updateFailedTests = updateFailedTests;
            BuildXL.Storage.ContentHashingUtilities.SetDefaultHashType();
        }

        /// <summary>
        /// Run the test
        /// </summary>
        public bool Run(string testFolder, string specFile, string fullIdentifier, string shortName, string lkgFile, params string[] sdksToResolve)
        {
            Contract.Requires(!string.IsNullOrEmpty(testFolder));
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(sdksToResolve != null);

            // Sadly the frontend doesn't use the engine abstractions file api's so we have to materialize stuff on disk for now...
            // TODO: Fix this code once the frontend supports a proper virtual FileSystem.
            // TODO: Change the package semantics to implicit when we expose a way to evaluate a single value
            var testFileName = Path.GetFileName(specFile);
            var mainFileName = "testMain.bp";
            var testMainFile = Path.Combine(testFolder, mainFileName);
            Directory.CreateDirectory(testFolder);
            File.WriteAllText(Path.Combine(testFolder, Names.ModuleConfigBm), I($@"module(
{{
    name: 'TestPackage',
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences, 
    projects: [
        f`{mainFileName}`,
        f`{testFileName}`,
    ],
}});"));
            File.WriteAllText(testMainFile, I($@"
export const testFolder = d`{Path.GetDirectoryName(specFile).Replace('\\', '/')}`;

@@public
export const main = {fullIdentifier}();"));
            File.Copy(specFile, Path.Combine(testFolder, testFileName));

            // Create a fake package for Sdk.TestRunner so that you can safely test packages that have the tests embedded in them.
            var testRunnerFolder = Path.Combine(testFolder, "Sdk.TestRunner");
            Directory.CreateDirectory(testRunnerFolder);
            File.WriteAllText(Path.Combine(testRunnerFolder, Names.ModuleConfigBm), I($"module({{\n\tname: 'Sdk.TestRunner',\n}});"));
            File.WriteAllText(Path.Combine(testRunnerFolder, "package" + Names.DotDscExtension), I($@"
export interface TestArguments {{
    testFiles: File[];
    sdkFolders?: (Directory|StaticDirectory)[];
    autoFixLkgs?: boolean;
}}
export interface TestResult {{
    xmlResults: File;
}}
export function test(args: TestArguments): TestResult {{
    Contract.fail(""Can't run a DScript UnitTest inside of a DScript UnitTest"");
}}"));

            // Setup Context and configuration
            var frontEndContext = FrontEndContext.CreateInstanceForTesting();
            var pipContext = new SchedulerContext(CancellationToken.None, frontEndContext.StringTable, frontEndContext.PathTable, frontEndContext.SymbolTable, frontEndContext.QualifierTable);
            var pathTable = frontEndContext.PathTable;
            var testFolderPath = AbsolutePath.Create(pathTable, testFolder);

            var configuration = CreateConfiguration(sdksToResolve.Union(new[] { testRunnerFolder }), pathTable, testFolderPath);

            var engineAbstraction = new TestEngineAbstraction(pathTable, frontEndContext.StringTable, testFolderPath, new PassThroughFileSystem(pathTable));

            var frontEndStatistics = new FrontEndStatistics();

            if (!CreateFactories(
                frontEndContext, 
                engineAbstraction, 
                frontEndStatistics, 
                configuration, 
                out var ambientTesting, 
                out var workspaceFactory, 
                out var moduleRegistry, 
                out var frontEndFactory))
            {
                return false;
            }

            // Set the timeout to a large number to avoid useless performance collections in tests.
            using (var performanceCollector = new PerformanceCollector(TimeSpan.FromHours(1)))
            using (var frontEndHostController = new FrontEndHostController(
                    frontEndFactory,
                    workspaceFactory,
                    new EvaluationScheduler(1),
                    moduleRegistry,
                    frontEndStatistics,
                    m_tracingLogger,
                    performanceCollector,
                    collectMemoryAsSoonAsPossible: true))
            {
                var frontEndController = (IFrontEndController)frontEndHostController;
                frontEndController.InitializeHost(frontEndContext, configuration);
                frontEndController.ParseConfig(configuration);

                // Populate the graph
                using (var pipTable = new PipTable(
                    pipContext.PathTable,
                    pipContext.SymbolTable,
                    initialBufferSize: 16384,
                    maxDegreeOfParallelism: 1,
                    debug: true))
                {
                    var mountPathExpander = new MountPathExpander(pathTable);
                    mountPathExpander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(frontEndContext.StringTable, "testFolder"), testFolderPath, allowHashing: true, readable: true, writable: false));
                    mountPathExpander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(frontEndContext.StringTable, "src"), testFolderPath.Combine(pathTable, "src"), allowHashing: true, readable: true, writable: true));
                    mountPathExpander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(frontEndContext.StringTable, "out"), testFolderPath.Combine(pathTable, "out"), allowHashing: true, readable: true, writable: true));
                    mountPathExpander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(frontEndContext.StringTable, "noRead"), testFolderPath.Combine(pathTable, "noRead"), allowHashing: true, readable: false, writable: true));
                    mountPathExpander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(frontEndContext.StringTable, "temp"), engineAbstraction.Layout.TempDirectory, allowHashing: true, readable: true, writable: true));
                    mountPathExpander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(frontEndContext.StringTable, "obj"), engineAbstraction.Layout.ObjectDirectory, allowHashing: true, readable: true, writable: true));

                    var graph = new PipGraph.Builder(
                        pipTable,
                        pipContext,
                        m_schedulerLogger,
                        frontEndContext.LoggingContext,
                        configuration,
                        mountPathExpander);

                    using (var cacheLayer = new EngineCache(
                        new InMemoryArtifactContentCache(),
                        new InMemoryTwoPhaseFingerprintStore()))
                    {
                        var cache = Task.FromResult(Possible.Create(cacheLayer));
                        try
                        {
                            var evaluationFilter = new EvaluationFilter(
                                pipContext.SymbolTable,
                                pipContext.PathTable,
                                new FullSymbol[0],
                                new[]
                                {
                                    AbsolutePath.Create(frontEndContext.PathTable, testMainFile),
                                },
                                CollectionUtilities.EmptyArray<StringId>());
                            if (!frontEndController.PopulateGraph(cache, graph, engineAbstraction, evaluationFilter, configuration, configuration.Startup))
                            {
                                HandleDiagnostics();
                                return false;
                            }
                        }
                        catch (AggregateException e)
                        {
                            var baseException = e.GetBaseException();
                            if (baseException is XunitException)
                            {
                                // If it is an XUnit assert, then unwrap the exception and throw that because XUnit other doesn't display the error nicely.
                                ExceptionDispatchInfo.Capture(baseException).Throw();
                            }

                            throw;
                        }
                    }

                    if (!ValidatePips(frontEndContext, graph, testFolderPath, specFile, shortName, lkgFile, ambientTesting.DontValidatePipsEnabled))
                    {
                        return false;
                    }
                }
            }

            HandleDiagnostics();
            return true;
        }

        private bool CreateFactories(
            FrontEndContext frontEndContext,
            TestEngineAbstraction engineAbstraction,
            FrontEndStatistics frontEndStatistics,
            ICommandLineConfiguration configuration,
            out AmbientTesting ambientTesting,
            out DScriptWorkspaceResolverFactory workspaceFactory,
            out ModuleRegistry moduleRegistry,
            out FrontEndFactory frontEndFactory)
        {
            moduleRegistry = new ModuleRegistry(frontEndContext.SymbolTable);
            ambientTesting = new AmbientTesting(engineAbstraction, GetAllDiagnostics, moduleRegistry.PrimitiveTypes);
            ambientTesting.Initialize(moduleRegistry.GlobalLiteral);

            var ambientAssert = new AmbientAssert(moduleRegistry.PrimitiveTypes);
            ambientAssert.Initialize(moduleRegistry.GlobalLiteral);

            workspaceFactory = new DScriptWorkspaceResolverFactory();
            workspaceFactory.RegisterResolver(
                KnownResolverKind.DScriptResolverKind,
                () => new WorkspaceSourceModuleResolver(frontEndContext.StringTable, frontEndStatistics));
            workspaceFactory.RegisterResolver(
                KnownResolverKind.SourceResolverKind,
                () => new WorkspaceSourceModuleResolver(frontEndContext.StringTable, frontEndStatistics));
            workspaceFactory.RegisterResolver(
                KnownResolverKind.DefaultSourceResolverKind,
                () => new WorkspaceDefaultSourceModuleResolver(frontEndContext.StringTable, frontEndStatistics));

            // Create the controller
            frontEndFactory = new FrontEndFactory();
            frontEndFactory.SetConfigurationProcessor(new TestConfigProcessor(configuration));
            frontEndFactory.AddFrontEnd(
                new DScriptFrontEnd(
                    frontEndStatistics,
                    logger: m_astLogger));

            if (!frontEndFactory.TrySeal(frontEndContext.LoggingContext))
            {
                HandleDiagnostics();
                return false;
            }

            return true;
        }

        private static ICommandLineConfiguration CreateConfiguration(
            IEnumerable<string> sdksToResolve,
            PathTable pathTable,
            AbsolutePath testFolderPath)
        {
            var configFilePath = testFolderPath.Combine(pathTable, Names.ConfigDsc);
            var packageFilePath = testFolderPath.Combine(pathTable, Names.ModuleConfigBm);

            var sdkPackages = sdksToResolve
                .SelectMany(folder => 
                    Directory.EnumerateFiles(folder, Names.PackageConfigDsc, SearchOption.AllDirectories).Concat(
                        Directory.EnumerateFiles(folder, Names.ModuleConfigBm, SearchOption.AllDirectories).Concat(
                            Directory.EnumerateFiles(folder, Names.ModuleConfigDsc, SearchOption.AllDirectories))))
                .Select(packageFile => AbsolutePath.Create(pathTable, packageFile))
                .ToList();

            var configuration =
                new CommandLineConfiguration
                {
                    // Have to new up the list because we have some bad semantics dealing with the list being null or not.
                    Packages = new List<AbsolutePath>
                    {
                        packageFilePath,
                    },
                    Resolvers =
                    {
                        new SourceResolverSettings
                        {
                            Kind = "SourceResolver",
                            Modules = sdkPackages,
                        },
                    },
                    FrontEnd =
                    {
                        MaxFrontEndConcurrency = 1, // Single threaded for deterministic evaluation

                        NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,

                        // PreserveFullNames = true, Some comment in code as not to turn on, check with folks....
                        UsePartialEvaluation = true,
                        UseSpecPublicFacadeAndAstWhenAvailable = false,
                        ConstructAndSaveBindingFingerprint = false,
                        // Some of the DS tests fail when incremental frontend is not used
                        EnableIncrementalFrontEnd = true,
                    },
                    Engine =
                    {
                        TrackBuildsInUserFolder = false,
                    },
                    Layout =
                    {
                        OutputDirectory = testFolderPath.Combine(pathTable, "Out"),
                        ObjectDirectory = testFolderPath.Combine(pathTable, "Out").Combine(pathTable, "Objects"),
                        PrimaryConfigFile = configFilePath,
                        SourceDirectory = testFolderPath,
                        TempDirectory = testFolderPath.Combine(pathTable, "Out").Combine(pathTable, "Temp"),
                    },
                    Mounts =
                    {
                    },
                    Startup =
                    {
                        ConfigFile = configFilePath,
                    },
                };
            return configuration;
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        private bool ValidatePips(FrontEndContext context, PipGraph.Builder pipGraph, AbsolutePath testFolder, string specFile, string shortName, string lkgFile, bool dontValidatePipsEnabled)
        {
            bool hasLkgFile = !string.IsNullOrEmpty(lkgFile);
            var hasPips = pipGraph.PipTable.Keys.Count(pipId => !pipGraph.PipTable.GetPipType(pipId).IsMetaPip()) > 0;

            if (dontValidatePipsEnabled)
            {
                if (hasLkgFile)
                {
                    m_diagnosticHandler(
                        new Diagnostic(
                            0,
                            EventLevel.Error,
                            C($"Test calls 'Testing.dontValidatePips', but lkg file '{lkgFile}' is present. Either remove that call or delete the file.{GetAutoFixString()}"),
                            default(Location)));
                    return FailOrDeleteLkg(lkgFile);
                }

                if (!hasPips)
                {
                    m_diagnosticHandler(
                        new Diagnostic(
                            0,
                            EventLevel.Error,
                            C($"Test calls 'Testing.dontValidatePips', but no pips were created. Remove that call.{GetAutoFixString()}"),
                            default(Location)));
                    return FailOrDeleteLkg(lkgFile);
                }
            }
            else
            {
                if (!hasPips)
                {
                    if (hasLkgFile)
                    {
                        m_diagnosticHandler(
                            new Diagnostic(
                                0,
                                EventLevel.Error,
                                C($"No pips were created in the test, but lkg file '{lkgFile}' is present. Delete the file.{GetAutoFixString()}"),
                                default(Location)));
                        return FailOrDeleteLkg(lkgFile);
                    }
                }
                else
                {
                    var printer = new TestPipPrinter(context.PathTable, context.StringTable, testFolder);
                    var actual = printer.Print(pipGraph);

                    if (!hasLkgFile)
                    {
                        lkgFile = Path.Combine(Path.GetDirectoryName(specFile), Path.GetFileNameWithoutExtension(specFile), shortName + ".lkg");
                        m_diagnosticHandler(
                            new Diagnostic(
                                0,
                                EventLevel.Error,
                                C($"This test creates pip, but no lkgFile was encountered. Either create file '{lkgFile}' or add a call to 'Testing.dontValidatePips'.{GetAutoFixString()}"),
                                default(Location)));
                        return FailOrUpdateLkg(lkgFile, actual);
                    }

                    if (!File.Exists(lkgFile))
                    {
                        m_diagnosticHandler(
                            new Diagnostic(
                                0,
                                EventLevel.Error,
                                C($"File '{lkgFile}' not found.{GetAutoFixString()}"),
                                default(Location)));

                        return FailOrUpdateLkg(lkgFile, actual);
                    }

                    var expected = File.ReadAllText(lkgFile);
                    string message;
                    if (!FileComparison.ValidateContentsAreEqual(expected, actual, lkgFile, out message))
                    {
                        m_diagnosticHandler(
                            new Diagnostic(
                                0,
                                EventLevel.Error,
                                C($"Pips don't match '{lkgFile}: {message}'.{GetAutoFixString()}"),
                                default(Location)));

                        return FailOrUpdateLkg(lkgFile, actual);
                    }
                }
            }

            return true;
        }

        private string GetAutoFixString()
        {
            if (m_updateFailedTests)
            {
                return " These are automatically fixed.";
            }
            else
            {
                return " Pass 'autoFixLkgs: true' to the the call to 'Testing.test' in your project file to automatically update the lkg.";
            }
        }

        private bool FailOrUpdateLkg(string lkgFile, string actual)
        {
            if (m_updateFailedTests)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(lkgFile));
                    File.WriteAllText(lkgFile, actual);
                    return true;
                }
                catch (IOException e)
                {
                    m_diagnosticHandler(
                        new Diagnostic(
                            0,
                            EventLevel.Error,
                            C($"Failed to automatically update LKG file '{lkgFile}': {e.Message}."),
                            default(Location)));
                }
                catch (UnauthorizedAccessException e)
                {
                    m_diagnosticHandler(
                        new Diagnostic(
                            0,
                            EventLevel.Error,
                            C($"Failed to automatically update LKG file '{lkgFile}': {e.Message}."),
                            default(Location)));
                }
            }

            return false;
        }

        private bool FailOrDeleteLkg(string lkgFile)
        {
            if (m_updateFailedTests)
            {
                try
                {
                    File.Delete(lkgFile);
                    return true;
                }
                catch (IOException e)
                {
                    m_diagnosticHandler(
                        new Diagnostic(
                            0,
                            EventLevel.Error,
                            C($"Failed to automatically delete LKG file '{lkgFile}': {e.Message}."),
                            default(Location)));
                }
                catch (UnauthorizedAccessException e)
                {
                    m_diagnosticHandler(
                        new Diagnostic(
                            0,
                            EventLevel.Error,
                            C($"Failed to automatically delete LKG file '{lkgFile}': {e.Message}."),
                            default(Location)));
                }
            }

            return false;
        }

        private IEnumerable<Diagnostic> GetAllDiagnostics()
        {
            return
                new[]
                {
                    m_astLogger.CapturedDiagnostics,
                    m_tracingLogger.CapturedDiagnostics,
                    m_schedulerLogger.CapturedDiagnostics,
                }.SelectMany(x => x);
        }

        private void HandleDiagnostics()
        {
            foreach (var diagnostic in GetAllDiagnostics())
            {
                m_diagnosticHandler(diagnostic);
            }
        }
    }
}
