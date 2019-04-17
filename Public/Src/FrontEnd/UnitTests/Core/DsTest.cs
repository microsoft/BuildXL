// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Incrementality;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using JetBrains.Annotations;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.Utilities;
using TypeScript.Net.Utilities;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;
using FileType = BuildXL.FrontEnd.Script.FileType;
using InitializationLogger = global::BuildXL.FrontEnd.Core.Tracing.Logger;
using LogEventId = BuildXL.FrontEnd.Script.Tracing.LogEventId;
using Logger = BuildXL.FrontEnd.Script.Tracing.Logger;
using Test.DScript.Workspaces.Utilities;

namespace Test.BuildXL.FrontEnd.Core
{
    [SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
    public abstract class DsTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        static DsTest()
        {
            Contract.ContractFailed += (sender, contractFailedEventArgs) =>
            {
                Assert.False(true, I($"Got contract violation during test execution: {contractFailedEventArgs.Message}"));
            };

            // Change minimal number of threads for performance reasons.
            // 5 is a reasonable number that should prevent thread pool exhaustion and will not spawn too many threads.
            global::BuildXL.Utilities.ThreadPoolHelper.ConfigureWorkerThreadPools(Environment.ProcessorCount, 3);
        }

        /// <summary>
        /// Used to simplify expected result when doing toString on objects
        /// </summary>
        private static Regex DiscardTrivia { get; } = new Regex(@"(\t|\n|\r|\s)+");

        protected static void AssertEqualDiscardingTrivia<T>(T expected, T result)
        {
            var stringResult = result as string;
            if (stringResult == null)
            {
                Assert.Equal(expected, result);
                return;
            }

            Assert.Equal(expected, (object)DiscardTrivia.Replace(stringResult, string.Empty));
        }

        /// <summary>
        /// Checks that the given event id is equals to a given error code.
        /// </summary>
        protected static void EventIdEqual<T>(T t, int errorCode) where T : struct
        {
            T other = (T)(object)(Enum.Parse(typeof(T), errorCode.ToString()));
            Assert.Equal(t, other);
        }

        protected const string MainSpecRelativePath = SpecEvaluationBuilder.DefaultSpecName;

        /// <summary>
        /// Logger that is used for capturing diagnostics for parsing and evaluation phases.
        /// </summary>
        protected Logger ParseAndEvaluateLogger { get; } = Logger.CreateLogger(preserveLogEvents: true);

        /// <summary>
        /// Logger that is used for capturing diagnostics during initialization.
        /// </summary>
        private InitializationLogger InitializationLogger { get; } = InitializationLogger.CreateLogger(preserveLogEvents: true);

        private const string SourceResolverKind = "SourceResolver";

        protected IReadOnlyList<Diagnostic> CaptureEvaluationDiagnostics() => ParseAndEvaluateLogger.CapturedDiagnostics.ToList();

        protected IReadOnlyList<Diagnostic> CapturedWarningsAndErrors
            =>
                InitializationLogger
                    .CapturedDiagnostics
                    .Concat(ParseAndEvaluateLogger.CapturedDiagnostics)
                    .Where(d => d.Level == EventLevel.Warning || d.Level == EventLevel.Error || d.Level == EventLevel.Critical)
                    .ToList();

        private IReadOnlyList<Diagnostic> CapturedErrors
            =>
                CapturedWarningsAndErrors
                    .Where(d => d.Level == EventLevel.Error || d.Level == EventLevel.Critical)
                    .ToList();

        public ITestOutputHelper Output { get; }

        protected virtual FrontEndContext FrontEndContext { get; }

        protected StringTable StringTable => FrontEndContext.StringTable;

        protected PathTable PathTable => FrontEndContext.PathTable;

        protected internal string TestRoot { get; set; }

        protected FrontEndEngineAbstraction Engine { get; }

        public IMutableFileSystem FileSystem { get; protected set; }

        protected readonly EvaluationStatistics Statistics = new EvaluationStatistics();

        /// <summary>
        /// Source root relative to the test folder
        /// </summary>
        protected string RelativeSourceRoot = $"TestSln{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}";

        /// <nodoc />
        protected DsTest(ITestOutputHelper output, bool usePassThroughFileSystem = false)
            : base(output)
        {
            // This is not the best solution to rely on the global state, but this is not a long term solution.
            var pathTable = new PathTable();
            FileSystem = usePassThroughFileSystem
                ? (IMutableFileSystem)new PassThroughMutableFileSystem(pathTable)
                : new InMemoryFileSystem(pathTable);
            FrontEndContext = CreateFrontEndContext(pathTable, FileSystem);
            Engine = new BasicFrontEndEngineAbstraction(FrontEndContext.PathTable, FileSystem);
            Output = output;

            TestRoot = usePassThroughFileSystem ? Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString()) : AssemblyDirectory;
            RelativeSourceRoot = TestRoot;

#if DEBUG
            // TODO: This should go away when all statics become non-statics.
            FrontEndContext.SetContextForDebugging(FrontEndContext);
#endif
        }

        protected virtual FrontEndContext CreateFrontEndContext(PathTable pathTable, IFileSystem fileSystem)
        {
            return FrontEndContext.CreateInstanceForTesting(pathTable: pathTable, fileSystem: fileSystem);
        }

        protected virtual void BeforeBuildWorkspaceHook() { }
        protected virtual void BeforeAnalyzeHook() { }
        protected virtual void BeforeConvertHook() { }
        protected virtual void BeforeEvaluateHook() { }

        /// <summary>
        /// Returns new instance of the <see cref="SpecEvaluationBuilder"/> with a full set of default libraries already added.
        /// </summary>
        protected virtual SpecEvaluationBuilder Build()
        {
            return new SpecEvaluationBuilder(this).AddFullPrelude();
        }

        /// <summary>
        /// Returns new empty instance of the <see cref="SpecEvaluationBuilder"/>.
        /// </summary>
        protected virtual SpecEvaluationBuilder BuildWithoutDefautlLibraries()
        {
            //  The built-in prelude files from disks are used for configuration processing.
            return Build();
        }

        protected void PrettyPrint(string source, string expected = null, PrettyPrintedFileKind fileKind = PrettyPrintedFileKind.Project)
        {
            expected = expected ?? source;

            var testResult = EvaluateSpec(source, expression: null, parseOnly: true);

            SourceFile sourceFile = testResult.SourceFile;

            if (sourceFile == null)
            {
                Assert.False(true, "One or more error occurred. See output for more details.");
            }

            var specFullPath = sourceFile.Path;

            var writer = new StringWriter();

            const string PrettyPrintConfigAsPackagePath = global::BuildXL.FrontEnd.Script.Constants.Names.ConfigDsc;
            var configStringPath = Path.Combine(TemporaryDirectory, PrettyPrintConfigAsPackagePath);

            var moduleRegistry = new ModuleRegistry(FrontEndContext.SymbolTable);
            var frontEndHost = FrontEndHostController.CreateForTesting(FrontEndContext, Engine, moduleRegistry, configStringPath);

            var currentDirectory = specFullPath.GetParent(frontEndHost.FrontEndContext.PathTable);
            var currentRootDirectory = AbsolutePath.Create(FrontEndContext.PathTable, configStringPath).GetParent(frontEndHost.FrontEndContext.PathTable);
            var prettyPrinter = new PrettyPrinter(
                FrontEndContext,
                writer,
                currentDirectory,
                currentRootDirectory);

            sourceFile.Accept(prettyPrinter);

            var result = writer.GetStringBuilder().ToString();
            expected += Environment.NewLine;

            XAssert.EqualIgnoreWhiteSpace(expected, result);
        }

        /// <summary>
        /// Parses a build specification.
        /// </summary>
        /// <param name="spec">Build specification</param>
        /// <returns>Test result, see <see cref="TestResult" />.</returns>
        protected TestResult Parse(string spec)
        {
            Contract.Requires(spec != null);

            return Build()
                .AddSpec(spec)
                .ParseOnly()
                .Evaluate();
        }

        /// <summary>
        /// Parses a list of specs.
        /// </summary>
        protected internal TestResult ParseSpecs(string specFileToEvaluate, IEnumerable<BuildSpec> buildSpecs)
        {
            var testWriter = DsTestWriter.Create(RelativeSourceRoot, buildSpecs, FileSystem);

            return Evaluate(testWriter, specFileToEvaluate, new string[0], parseOnly: true);
        }

        protected Diagnostic ParseWithFirstError(string source)
        {
            return EvaluateWithDiagnostics(source, expression: null, parseOnly: true).First(d => d.Level.IsError());
        }

        protected Diagnostic ParseWithDiagnosticId<T>(string source, T diagnosticId) where T : struct
        {
            return EvaluateWithDiagnosticId(GetDefaultSpecPath(), source, diagnosticId, expression: null, parseOnly: true);
        }

        protected TestResult ParseOnly(string source)
        {
            return Parse(source);
        }

        private static string GetDefaultSpecPath()
        {
            var fileKind = PrettyPrintedFileKind.Project;

            var specPath = fileKind == PrettyPrintedFileKind.Configuration
                ? global::BuildXL.FrontEnd.Script.Constants.Names.ConfigDsc
                : (fileKind == PrettyPrintedFileKind.PackageDescriptor ? global::BuildXL.FrontEnd.Script.Constants.Names.PackageConfigDsc : MainSpecRelativePath);
            return specPath;
        }

        /// <summary>
        /// Provides degree of parallelism for test execution.
        /// </summary>
        protected virtual int DegreeOfParallelism => 1;

        /// <summary>
        /// Evaluates a list of expressions over a build specification.
        /// </summary>
        protected TestResult EvaluateSpec(string path, string spec, string[] expressions, string qualifier = null, bool isDebugged = false, bool useSerializedAst = false, bool parseOnly = false)
        {
            Contract.Requires(spec != null);
            Contract.Requires(expressions != null);
            Contract.RequiresForAll(expressions, e => !string.IsNullOrWhiteSpace(e));

            return Build()
                .AddFullPrelude()
                .AddSpec(path, spec)
                .Qualifier(qualifier)
                .ParseOnly(parseOnly)
                .UseSerializedAst(useSerializedAst)
                .IsDebugged(isDebugged)
                .Evaluate(expressions);
        }

        /// <summary>
        /// Evaluates a list of expressions over a build specification.
        /// </summary>
        protected TestResult EvaluateSpec(string spec, string[] expressions, string qualifier = null, bool isDebugged = false, bool useSerializedAst = false)
        {
            Contract.Requires(spec != null);
            Contract.Requires(expressions != null);
            Contract.RequiresForAll(expressions, e => !string.IsNullOrWhiteSpace(e));
            return EvaluateSpec(MainSpecRelativePath, spec, expressions, qualifier, isDebugged, useSerializedAst);
        }

        /// <summary>
        /// Evaluates set of specs with a list of expressions.
        /// </summary>
        protected internal TestResult EvaluateSpecs(string specFileToEvaluate, IEnumerable<BuildSpec> buildSpecs, string[] expressions, string qualifier = null, bool isDebugged = false, bool useSerializedAst = false, bool parseOnly = false)
        {
            var testWriter = DsTestWriter.Create(RelativeSourceRoot, buildSpecs, FileSystem);

            var result = DoEvaluate(testWriter, specFileToEvaluate, expressions, qualifier: qualifier, isDebugged: isDebugged, parseOnly: parseOnly);
            Output?.WriteLine(string.Join(Environment.NewLine, result.Errors.Select(e => e.FullMessage)));
            return result;
        }

        protected internal ICommandLineConfiguration WriteSpecsAndGetConfiguration(string specFileToEvaluate, IEnumerable<BuildSpec> buildSpecs, bool isDebugged, bool cleanExistingDirectory, bool enableSpecCache)
        {
            var testWriter = DsTestWriter.Create(RelativeSourceRoot, buildSpecs, FileSystem);

            return WriteSpecsToDiskAndGetConfiguration(testWriter, isDebugged, cleanExistingDirectory, enableSpecCache);
        }

        /// <summary>
        /// Evaluates <paramref name="expression"/> and provides just one value out of it.
        /// </summary>
        protected object EvaluateExpressionWithNoErrors(string spec, string expression, string qualifier = null)
        {
            Contract.Requires(spec != null);
            Contract.Requires(expression != null);

            var result = EvaluateExpressionsWithNoErrors(spec, expression);
            return result[expression];
        }

        /// <summary>
        /// Evaluates <paramref name="expression"/> and provides just one value out of it.
        /// </summary>
        protected T EvaluateExpressionWithNoErrors<T>(string spec, string expression, string qualifier = null)
        {
            return (T)EvaluateExpressionWithNoErrors(spec, expression, qualifier);
        }

        /// <summary>
        /// Evaluates a list of expressions over a build specification.
        /// </summary>
        protected EvaluatedValues EvaluateExpressionsWithNoErrors(string spec, params string[] expressions)
        {
            return Build()
                .AddFullPrelude()
                .AddSpec(spec)
                .EvaluateExpressionsWithNoErrors(expressions);
        }

        protected Diagnostic EvaluateWithFirstError(string spec, string expression = null)
        {
            var result = EvaluateSpec(spec, expression == null ? new string[0] : new[] { expression });
            if (result.Errors.Count == 0)
            {
                // var success = result.Values
                throw new InvalidOperationException($"Expected to have at least one error but evaluation succeed. Result: '{result}'");
            }

            return result.Errors[0];
        }

        protected IReadOnlyList<Diagnostic> EvaluateWithDiagnostics(string spec, string expression = null, bool parseOnly = false)
        {
            var result = EvaluateSpec(MainSpecRelativePath, spec, expressions: expression == null ? new string[0] : new[] { expression }, qualifier: null, isDebugged: false, useSerializedAst: false, parseOnly: true);
            if (result.Errors.Count == 0)
            {
                // var success = result.Values
                throw new InvalidOperationException($"Expected to have at least one error but evaluation succeed. Result: '{result}'");
            }

            return result.Errors;
        }

        protected TestResult EvaluateSpec(string spec, string expression = null, bool parseOnly = false)
        {
            return EvaluateSpec(MainSpecRelativePath, spec, expressions: expression == null ? new string[0] : new[] { expression }, qualifier: null, isDebugged: false, useSerializedAst: false, parseOnly: true);
        }

        protected Diagnostic[] EvaluateWithTypeCheckerDiagnostic(string spec, TypeScript.Net.Diagnostics.IDiagnosticMessage expected, params string[] args)
        {
            return Build().AddSpec(spec).EvaluateWithCheckerDiagnostic(expected, args);
        }

        protected Diagnostic EvaluateWithDiagnosticId<T>(string path, string spec, T diagnosticId, string expression = null, bool parseOnly = false) where T : struct
        {
            return EvaluateWithAllDiagnosticId(path, spec, diagnosticId, expression, parseOnly).First();
        }

        protected Diagnostic EvaluateWithDiagnosticId<T>(string spec, T diagnosticId, string expression = null) where T : struct
        {
            return EvaluateWithAllDiagnosticId(spec, diagnosticId, expression).First();
        }

        protected List<Diagnostic> AssertDiagnosticIdExists<T>(IReadOnlyCollection<Diagnostic> diagnostics, T diagnosticId) where T : struct
        {
            int id = Convert.ToInt32(diagnosticId);

            var matches = diagnostics.Where(d => d.ErrorCode == id).ToList();

            if (matches.Count == 0)
            {
                string availableDiagnostics = diagnostics.Count == 0
                    ? "'empty'"
                    : string.Join(", ", diagnostics.Select(d => I($"'{d.FullMessage}'")));

                string message = I($"Can't find diagnostic '{diagnosticId}'. Known diagnostics are: {availableDiagnostics}");
                XAssert.Fail(message);
            }

            return matches;
        }

        protected List<Diagnostic> EvaluateWithAllDiagnosticId<T>(string path, string spec, T diagnosticId, string expression = null, bool parseOnly = false) where T : struct
        {
            var result = EvaluateSpec(path, spec, expression == null ? new string[0] : new[] { expression }, useSerializedAst: false, parseOnly: parseOnly);

            return AssertDiagnosticIdExists(result.Diagnostics, diagnosticId);
        }

        protected List<Diagnostic> EvaluateWithAllDiagnosticId<T>(string spec, T diagnosticId, string expression = null) where T : struct
        {
            return EvaluateWithAllDiagnosticId(MainSpecRelativePath, spec, diagnosticId, expression);
        }

        internal Workspace BuildWorkspace(IEnumerable<BuildSpec> buildSpecs)
        {
            var testWriter = DsTestWriter.Create(RelativeSourceRoot, buildSpecs, FileSystem);

            CreateFrontEndHost(
                testWriter,
                null, // specRelativePath
                false, // isDebug
                out IConfiguration _,
                out AbsolutePath _,
                out FrontEndConfiguration _,
                out FrontEndHostController _,
                out var workspace);

            return workspace;
        }

        /// <summary>
        /// Evaluates a list of expressions over a build specification.
        /// </summary>
        protected virtual TestResult Evaluate(
            DsTestWriter testWriter,
            string specRelativePath,
            string[] expressions,
            string qualifier = null,
            bool parseOnly = false,
            bool isDebugged = false)
        {
            Contract.Requires(testWriter != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(specRelativePath));
            Contract.Requires(expressions != null);
            Contract.RequiresForAll(expressions, e => !string.IsNullOrWhiteSpace(e));

            AddPrelude(testWriter);

            return DoEvaluate(testWriter, specRelativePath, expressions, qualifier, parseOnly, isDebugged);
        }

        public static void AddPrelude(DsTestWriter testWriter)
        {
            testWriter.ConfigWriter.AddBuildSpec(R("Sdk.Prelude", "prelude.dsc"), SpecEvaluationBuilder.FullPreludeContent);
            testWriter.ConfigWriter.AddBuildSpec(R("Sdk.Prelude", "package.config.dsc"), CreatePackageConfig(FrontEndHost.PreludeModuleName, mainFile: "prelude.dsc"));
            testWriter.ConfigWriter.AddBuildSpec(R("Sdk.Transformers", "package.dsc"), SpecEvaluationBuilder.SdkTransformersContent);
            testWriter.ConfigWriter.AddBuildSpec(R("Sdk.Transformers", "package.config.dsc"), CreatePackageConfig("Sdk.Transformers", useImplicitReferenceSemantics: true));

        }

        /// <summary>
        /// Evaluates a list of expressions over build specifications included in a test writer.
        /// </summary>
        /// <param name="testWriter">A test writer that includes build specifications.</param>
        /// <param name="specRelativePath">The path to the build specification over which the expressions are to be evaluated.</param>
        /// <param name="expressions">List of expressions.</param>
        /// <param name="qualifier">Build qualifier.</param>
        /// <param name="parseOnly">Parse only.</param>
        /// <param name="isDebugged">Flag indicating if the spec is being debugged.</param>
        /// <returns>Test result, see <see cref="TestResult" />.</returns>
        /// <remarks>
        /// Currently this method requires the path to the build spec that is going to be evaluated.
        /// TODO: Relieve the above requirement.
        /// </remarks>
        private TestResult DoEvaluate(
            DsTestWriter testWriter,
            string specRelativePath,
            string[] expressions,
            string qualifier = null,
            bool parseOnly = false,
            bool isDebugged = false)
        {
            Contract.Requires(testWriter != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(specRelativePath));
            Contract.Requires(expressions != null);
            Contract.RequiresForAll(expressions, e => !string.IsNullOrWhiteSpace(e));

            var sharedModuleRegistry = CreateFrontEndHost(
                testWriter,
                specRelativePath,
                isDebugged,
                out IConfiguration finalConfig,
                out AbsolutePath specFullPath,
                out FrontEndConfiguration frontEndConfiguration,
                out FrontEndHostController frontEndHost,
                out var workspace);

            XAssert.IsTrue(frontEndHost == null || workspace != null, "Workspace must be non-null when frontEndHost is non-null");

            if (frontEndHost != null)
            {
                BeforeConvertHook();
                frontEndHost.DoPhaseConvert(evaluationFilter: null);
            }

            // Should not evaluate if parsing errors occurred, because otherwise evaluation can fail with assumption violation.
            if (parseOnly || CapturedErrors.Count != 0)
            {
                var uninstantiatedModuleInfo = sharedModuleRegistry.GetUninstantiatedModuleInfoByPathForTests(specFullPath);
                return TestResult.Create(new object[0], CapturedWarningsAndErrors, uninstantiatedModuleInfo?.FileModuleLiteral, uninstantiatedModuleInfo?.SourceFile);
            }

            if (CapturedWarningsAndErrors.Any(e => e.Level.IsError()))
            {
                return TestResult.FromErrors(CapturedWarningsAndErrors);
            }

            if (frontEndHost == null)
            {
                // Unfortunately, we can't print other errors, most likely engine construction failed.
                XAssert.Fail("Failed to create FrontEndHost.");
                throw new InvalidOperationException("The code is unreachable");
            }

            // We clone module registry (i.e., serialize then deserialize evaluation AST) only to
            // ensure that evaluation AST serialization/deserialization is sound
            CloneModuleRegistry(sharedModuleRegistry);

            Package packageForTest = CreatePackageFromConfig(frontEndHost, specFullPath);

            QualifierId qualifierId = FrontEndContext.QualifierTable.EmptyQualifierId;

            if (!string.IsNullOrWhiteSpace(qualifier))
            {
                qualifier = qualifier.Trim();

                if (qualifier.StartsWith("{", StringComparison.Ordinal) && qualifier.EndsWith("}", StringComparison.Ordinal))
                {
                    qualifierId = ParseAndEvaluateQualifier(
                        frontEndHost,
                        sharedModuleRegistry,
                        packageForTest,
                        specFullPath,
                        qualifier);
                }
                else
                {
                    if (!TryFindNamedQualifier(frontEndHost, qualifier, finalConfig, out qualifierId))
                    {
                        XAssert.Fail("Qualifier '{0}' is not found", qualifier);
                    }
                }
            }

            RegisterConfigurationFile(packageForTest, sharedModuleRegistry, frontEndConfiguration);
            var evaluateFullBuildExtent = specRelativePath == global::BuildXL.FrontEnd.Script.Constants.Names.ConfigDsc || specRelativePath == global::BuildXL.FrontEnd.Script.Constants.Names.ConfigBc;
            var evaluationFilter = evaluateFullBuildExtent
                ? EvaluationFilter.Empty
                : EvaluationFilter.FromSingleSpecPath(frontEndHost.FrontEndContext.SymbolTable, frontEndHost.FrontEndContext.PathTable, specFullPath);

            BeforeEvaluateHook();
            frontEndHost.DoPhaseEvaluate(evaluationFilter, new[] { qualifierId });

            if (expressions.Length == 0)
            {
                frontEndHost.NotifyResolversEvaluationIsFinished();
                return TestResult.Create(new object[0], CapturedWarningsAndErrors);
            }

            FileModuleLiteral module = GetQualifiedFileModule(frontEndHost, sharedModuleRegistry, specFullPath, qualifierId);

            if (module == null)
            {
                if (CapturedWarningsAndErrors.Count(e => e.Level.IsError()) != 0)
                {
                    // Most likely we've got parsing error and can't move forward.
                    // Just returning errors back
                    frontEndHost.NotifyResolversEvaluationIsFinished();
                    return TestResult.FromErrors(CapturedWarningsAndErrors);
                }

                Assert.True(false, I($"Can't find instantiated module for spec '{specFullPath.ToString(FrontEndContext.PathTable)}'."));
            }

            // Parse evaluated expressions.
            Expression[] evalExpressions = ParseEvaluatedExpressions(
                frontEndHost,
                packageForTest,
                specFullPath,
                expressions,
                finalConfig.FrontEnd.EnabledPolicyRules);

            var result = EvaluateExpressions(
                frontEndHost,
                FrontEndContext,
                module,
                evalExpressions);

            frontEndHost.NotifyResolversEvaluationIsFinished();

            return TestResult.Create(result, CapturedWarningsAndErrors, module);
        }

        ///<nodoc/>///
        protected DsTestWriter CreateTestWriter(string relativePath = null)
        {
            var pathTable = FrontEndContext.PathTable;
            return new DsTestWriter(pathTable, FileSystem);
        }

        protected ModuleRegistry CreateFrontEndHost(
            DsTestWriter testWriter,
            [CanBeNull]string specRelativePath,
            bool isDebugged,
            out IConfiguration finalConfig,
            out AbsolutePath specFullPath,
            out FrontEndConfiguration frontEndConfiguration,
            out FrontEndHostController frontEndHost,
            out Workspace workspace)
        {
            var config = WriteSpecsToDiskAndGetConfiguration(
                testWriter,
                isDebugged,
                cleanExistingDirectory: true,
                enableSpecCache: false);

            frontEndConfiguration = config.FrontEnd;
            var sharedModuleRegistry = new ModuleRegistry(FrontEndContext.SymbolTable);

            var workspaceFactory = CreateWorkspaceFactoryForTesting(FrontEndContext, ParseAndEvaluateLogger);
            var frontEndFactory = CreateFrontEndFactoryForEvaluation(workspaceFactory, ParseAndEvaluateLogger);

            specFullPath = string.IsNullOrEmpty(specRelativePath) ? AbsolutePath.Invalid : CreateAbsolutePathFor(testWriter, specRelativePath);

            // Prepare infrastructure.
            frontEndHost = CreateFrontEndHost(config, frontEndFactory, workspaceFactory, sharedModuleRegistry, specFullPath, out finalConfig, out workspace);
            return sharedModuleRegistry;
        }

        private string GetWriterRoot(DsTestWriter testWriter)
        {
            return testWriter.RootPath != null ? testWriter.RootPath : TestRoot;
        }

        private AbsolutePath CreateAbsolutePathFor(DsTestWriter testWriter, string relativePath)
        {
            var pathTable = FrontEndContext.PathTable;
            var writerRoot = GetWriterRoot(testWriter);
            return AbsolutePath.Create(pathTable, Path.Combine(writerRoot, relativePath));
        }

        protected void AssertCanonicalEquality(string expectedWindowsRelativePath, string actualFullPath)
        {
            var canonicalActualPath = actualFullPath.Replace("/", "\\").ToLowerInvariant();
            var canonicalExpectedPath = ((TestRoot + "\\").Replace("/", "\\") + expectedWindowsRelativePath).ToLowerInvariant();
            Assert.Equal(canonicalExpectedPath, canonicalActualPath);
        }

        private CommandLineConfiguration WriteSpecsToDiskAndGetConfiguration(
            DsTestWriter testWriter,
            bool isDebugged,
            bool cleanExistingDirectory,
            bool enableSpecCache)
        {
            // Write specs to files. // TODO:

            FileSystem.CreateDirectory(AbsolutePath.Create(FrontEndContext.PathTable, TestRoot));

            testWriter.Write(TestRoot, cleanExistingDirectory);

            var configFilePath = CreateAbsolutePathFor(testWriter, testWriter.ConfigWriter.PrimaryConfigurationFileName);
            return GetConfiguration(configFilePath, isDebugged, enableSpecCache);

        }

        protected CommandLineConfiguration GetConfiguration(AbsolutePath? configFilePath = null, bool isDebugged = false, bool enableSpecCache = false)
        {
            var configFile = configFilePath ?? AbsolutePath.Create(FrontEndContext.PathTable, TestOutputDirectory)
                                 .Combine(FrontEndContext.PathTable, "config.dsc");
            var config = new CommandLineConfiguration
                         {
                             Startup =
                             {
                                 ConfigFile = configFile,
                             },
                             FrontEnd = GetFrontEndConfiguration(isDebugged),
                             Engine =
                             {
                                 TrackBuildsInUserFolder = false,
                             },
                             Schedule =
                             {
                                 MaxProcesses = DegreeOfParallelism,
                                 DisableProcessRetryOnResourceExhaustion = true
                             },
                             Layout =
                             {
                                 SourceDirectory = configFile.GetParent(PathTable),
                                 OutputDirectory = configFile.GetParent(PathTable).GetParent(PathTable).Combine(PathTable, "Out")
                             },
                             Cache = {
                                         CacheSpecs = enableSpecCache ? SpecCachingOption.Enabled : SpecCachingOption.Disabled
                                     },
                         };

            if (enableSpecCache)
            {
                config.FrontEnd.EnableIncrementalFrontEnd = true;
            }

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(config, FrontEndContext.PathTable, bxlExeLocation: null, inTestMode: true);
            return config;
        }

        private void CloneModuleRegistry(ModuleRegistry sharedModuleRegistry)
        {
            using (var memoryStream = new MemoryStream())
            {
                var serializer = new ModuleRegistrySerializer(sharedModuleRegistry.GlobalLiteral, PathTable);

                serializer.Write(memoryStream, sharedModuleRegistry);

                var oldUninstantiatedModules = sharedModuleRegistry.UninstantiatedModules.ToDictionary(kvp => kvp);

                sharedModuleRegistry.UninstantiatedModules.Clear();
                memoryStream.Position = 0;

                serializer.Read(memoryStream, sharedModuleRegistry);

                ConstructorTests.ValidateEqual(
                    null,
                    sharedModuleRegistry.UninstantiatedModules.GetType(),
                    sharedModuleRegistry.UninstantiatedModules,
                    oldUninstantiatedModules,
                    nameof(ModuleRegistry) + "." + nameof(ModuleRegistry.UninstantiatedModules),
                    null);
            }
        }

        private void RegisterConfigurationFile(
            Package packageForTest,
            ModuleRegistry sharedModuleRegistry,
            FrontEndConfiguration frontEndConfiguration)
        {
            var configModule = ModuleLiteral.CreateFileModule(
                packageForTest.Path,
                new GlobalModuleLiteral(FrontEndContext.SymbolTable),
                packageForTest,
                sharedModuleRegistry,
                new LineMap(new int[] { 1, 2, 3 }, backslashesAllowedInPathInterpolation: true));

            sharedModuleRegistry.AddUninstantiatedModuleInfo(
                new UninstantiatedModuleInfo(
                    null, /* sourceFile */
                    configModule,
                    FrontEndContext.QualifierTable.EmptyQualifierSpaceId));
        }

        /// <summary>
        /// Default front-end configuration. But subclasses return specific ones.
        /// </summary>
        protected virtual FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            return new FrontEndConfiguration
            {
                DebugScript = isDebugged,
                PreserveFullNames = true,
                MaxFrontEndConcurrency = DegreeOfParallelism,
                UseSpecPublicFacadeAndAstWhenAvailable = false,
                CycleDetectorStartupDelay = 1,
                EnableIncrementalFrontEnd = false,
            };
        }

        protected virtual FrontEndFactory CreateFrontEndFactoryForParsingConfig(
            DScriptWorkspaceResolverFactory workspaceResolverFactory, Logger logger)
        {
            return CreateFrontEndFactory(workspaceResolverFactory, logger, DecoratorForParsingConfig);
        }

        protected virtual FrontEndFactory CreateFrontEndFactoryForEvaluation(
             DScriptWorkspaceResolverFactory workspaceResolverFactor, Logger logger)
        {
            return CreateFrontEndFactory(workspaceResolverFactor, logger, DecoratorForEvaluation);
        }

        protected virtual IDecorator<EvaluationResult> DecoratorForParsingConfig => null;

        protected virtual IDecorator<EvaluationResult> DecoratorForEvaluation => null;

        protected FrontEndFactory CreateFrontEndFactory(
            DScriptWorkspaceResolverFactory workspaceResolverFactory, Logger logger, IDecorator<EvaluationResult> decorator)
        {
            return FrontEndFactory.CreateInstanceForTesting(
                () => new ConfigurationProcessor(new FrontEndStatistics(), logger),
                new DScriptFrontEnd(FrontEndStatistics, logger, decorator));
        }

        private bool TryFindNamedQualifier(FrontEndHostController frontEndHost, string qualifier, IConfiguration finalConfig, out QualifierId qualifierId)
        {
            qualifierId = QualifierId.Invalid;

            if (finalConfig.Qualifiers == null || finalConfig.Qualifiers.NamedQualifiers == null)
            {
                return false;
            }

            IReadOnlyDictionary<string, string> qMap;

            if (!finalConfig.Qualifiers.NamedQualifiers.TryGetValue(qualifier, out qMap))
            {
                return false;
            }

            qualifierId = FrontEndContext.QualifierTable.CreateQualifier(qMap);
            return true;
        }

        private object[] EvaluateExpressions(
            FrontEndHostController frontEndHostController,
            FrontEndContext frontEndContext,
            FileModuleLiteral module,
            Expression[] expressions)
        {
            var results = new object[expressions.Length];

            for (int i = 0; i < expressions.Length; ++i)
            {
                using (var contextTree = new ContextTree(
                    frontEndHostController,
                    frontEndContext,
                    ParseAndEvaluateLogger,
                    Statistics,
                    new QualifierValueCache(),
                    false,
                    null,
                    module,
                    EvaluatorConfiguration,
                    EvaluationScheduler,
                    FileType.Expression))
                {
                    results[i] = expressions[i].Eval(contextTree.RootContext, module, EvaluationStackFrame.Empty()).Value;
                }
            }

            return results;
        }

        private EvaluatorConfiguration EvaluatorConfiguration { get; } = new EvaluatorConfiguration(
            callStackThreshold: 100,
            trackMethodInvocations: true,
            cycleDetectorStartupDelay: TimeSpan.FromSeconds(1));

        private EvaluationScheduler EvaluationScheduler { get; } = new EvaluationScheduler(degreeOfParallelism: 1);

        private Expression[] ParseEvaluatedExpressions(
            FrontEndHostController frontEndHostController,
            Package package,
            AbsolutePath specPath,
            string[] expressions,
            IReadOnlyList<string> customRules = null)
        {
            var frontEndHostForExpressions = FrontEndHostController.CreateForTesting(
                frontEndHostController.FrontEndContext,
                Engine,
                frontEndHostController.ModuleRegistry,
                frontEndHostController.PrimaryConfigFile.ToString(frontEndHostController.FrontEndContext.PathTable));

            var frontEnd = CreateScriptFrontEndForTesting(frontEndHostForExpressions, FrontEndContext);

            var results = new Expression[expressions.Length];

            var translator = CreateAstTranslater(frontEndHostController.FrontEndConfiguration, customRules);

            for (int i = 0; i < expressions.Length; ++i)
            {
                var parserContext = new RuntimeModelContext(
                    frontEndHostForExpressions,
                    FrontEndContext,
                    ParseAndEvaluateLogger,
                    package);

                var expression = translator.ParseExpression(parserContext, specPath, expressions[i]);

                results[i] = expression;
            }

            return results;
        }

        private QualifierId ParseAndEvaluateQualifier(
            FrontEndHostController frontEndHostController,
            ModuleRegistry moduleRegistry,
            Package package,
            AbsolutePath specPath,
            string qualifier)
        {
            Contract.Requires(frontEndHostController != null);
            Contract.Requires(moduleRegistry != null);
            Contract.Requires(package != null);
            Contract.Requires(specPath.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(qualifier));

            var expression = ParseEvaluatedExpressions(frontEndHostController, package, specPath, new[] { qualifier })[0];
            var dummyModule =
                ModuleLiteral.CreateFileModule(
                    AbsolutePath.Create(frontEndHostController.FrontEndContext.PathTable, Path.Combine(TemporaryDirectory, "DummyModule")),
                    moduleRegistry,
                    package,
                    new LineMap(new int[] { 1, 2, 3 }, backslashesAllowedInPathInterpolation: true));
            var dummyModuleInstance = dummyModule.InstantiateFileModuleLiteral(moduleRegistry, QualifierValue.CreateEmpty(FrontEndContext.QualifierTable));
            using (var contextTree = new ContextTree(
                frontEndHostController,
                frontEndHostController.FrontEndContext,
                ParseAndEvaluateLogger,
                Statistics,
                new QualifierValueCache(),
                false,
                null,
                dummyModuleInstance,
                EvaluatorConfiguration,
                EvaluationScheduler,
                FileType.Project))
            {
                var context = contextTree.RootContext;
                var evaluatedExpression = expression.Eval(context, dummyModuleInstance, EvaluationStackFrame.Empty()).Value;
                QualifierValue qv;

                if (!QualifierValue.TryCreate(context, dummyModuleInstance, evaluatedExpression, out qv))
                {
                    Assert.True(false, "Failed parsing and evaluating qualifier '" + qualifier + "'");
                }

                return qv.QualifierId;
            }
        }

        protected enum PrettyPrintedFileKind
        {
            /// <summary>
            /// Configuration config.dsc.
            /// </summary>
            Configuration,

            /// <summary>
            /// Package descriptor package.config.dsc.
            /// </summary>
            PackageDescriptor,

            /// <summary>
            /// Project.
            /// </summary>
            Project
        }

        protected virtual IPipGraph GetPipGraph() => null; // if this is DisallowedGraph(), then evaluations involving sealed directories will fail.

        protected FrontEndHostController CreateFrontEndHost(
            ICommandLineConfiguration config,
            FrontEndFactory frontEndFactory,
            DScriptWorkspaceResolverFactory workspaceFactory,
            ModuleRegistry moduleRegistry,
            AbsolutePath fileToProcess,
            out IConfiguration finalConfig,
            out Workspace workspace,
            QualifierId[] requestedQualifiers = null)
        {
            Contract.Requires(config != null);

            workspace = null;
            finalConfig = null;

            var engineContext = new EngineContext(FrontEndContext.CancellationToken, FrontEndContext.PathTable, FrontEndContext.SymbolTable, FrontEndContext.QualifierTable, FrontEndContext.FileSystem, new TokenTextTable());

            var controller = new FrontEndHostController(
                frontEndFactory, 
                workspaceFactory, 
                EvaluationScheduler,
                moduleRegistry,
                new FrontEndStatistics(),
                InitializationLogger, 
                collector: null,
                collectMemoryAsSoonAsPossible: false);
            var engine = BuildXLEngine.Create(LoggingContext, engineContext, config, new LambdaBasedFrontEndControllerFactory((_, __) => controller));

            if (engine == null)
            {
                return null;
            }

            var frontEndEngineAbstraction = new BasicFrontEndEngineAbstraction(FrontEndContext.PathTable, engineContext.FileSystem, engine.Configuration);
            controller.SetState(frontEndEngineAbstraction, GetPipGraph(), config);

            var evaluationFilter = fileToProcess.IsValid ? EvaluationFilter.FromSingleSpecPath(FrontEndContext.SymbolTable, FrontEndContext.PathTable, fileToProcess) : EvaluationFilter.Empty;

            var requestedQualifiersOrDefault = requestedQualifiers ?? new QualifierId[] { engine.Context.QualifierTable.EmptyQualifierId };

            workspace = BuildAndAnalyzeWorkspace(controller, engine.Configuration, frontEndEngineAbstraction, evaluationFilter, requestedQualifiersOrDefault);

            bool initFrontEnds = controller.TryInitializeFrontEndsAndResolvers(engine.Configuration, requestedQualifiers: requestedQualifiersOrDefault);
            if (!initFrontEnds)
            {
                return null;
            }

            finalConfig = engine.Configuration;
            return controller;
        }

        protected virtual bool FilterWorkspaceForConversion => false;

        private Workspace BuildAndAnalyzeWorkspace(FrontEndHostController controller, IConfiguration configuration, BasicFrontEndEngineAbstraction frontEndEngineAbstraction, EvaluationFilter evaluationFilter, QualifierId[] requestedQualifiers)
        {
            BeforeBuildWorkspaceHook();
            var workspace = controller.DoPhaseBuildWorkspace(configuration, frontEndEngineAbstraction, evaluationFilter, requestedQualifiers: requestedQualifiers);

            if (workspace.Succeeded)
            {
                BeforeAnalyzeHook();
                workspace = controller.DoPhaseAnalyzeWorkspace(configuration, workspace);
            }

            if (FilterWorkspaceForConversion && workspace.Succeeded)
            {
                controller.FilterWorkspace(workspace, evaluationFilter);
            }

            controller.SetWorkspaceForTesting(workspace);
            return workspace;
        }

        public static Package CreatePackageFromConfig(FrontEndHostController frontEndHostController, AbsolutePath path)
        {
            Contract.Requires(frontEndHostController != null);
            Contract.Requires(path.IsValid);

            var pathStr = path.ToString(frontEndHostController.FrontEndContext.PathTable);
            var id = PackageId.Create(StringId.Create(frontEndHostController.FrontEndContext.StringTable, pathStr));
            var desc = new PackageDescriptor
            {
                Name = global::BuildXL.FrontEnd.Script.Constants.Names.ConfigAsPackageName
            };

            return Package.Create(id, path, desc);
        }

        private FileModuleLiteral GetQualifiedFileModule(
            FrontEndHostController frontEndHost,
            ModuleRegistry moduleRegistry,
            AbsolutePath specPath,
            QualifierId qualifierId)
        {
            Contract.Requires(frontEndHost != null);
            Contract.Requires(moduleRegistry != null);
            Contract.Requires(specPath.IsValid);
            Contract.Requires(qualifierId.IsValid);

            if (!moduleRegistry.TryGetUninstantiatedModuleInfoByPath(specPath, out var moduleInfo))
            {
                return null;
            }

            if (
                !frontEndHost.FrontEndContext.QualifierTable.TryCreateQualifierForQualifierSpace(
                    FrontEndContext.PathTable,
                    FrontEndContext.LoggingContext,
                    qualifierId,
                    moduleInfo.QualifierSpaceId,
                    frontEndHost.ShouldUseDefaultsOnCoercion(specPath),
                    out var coercedQualifierId,
                    error: out var error))
            {
                var location = LocationData.Create(specPath);
                global::BuildXL.FrontEnd.Sdk.Tracing.Logger.Log.ErrorUnsupportedQualifierValue(
                FrontEndContext.LoggingContext,
                location.ToLogLocation(FrontEndContext.PathTable),
                error);

                // Returning null, because it can happen in a real test case.
                return null;
            }

            QualifiedModuleId qualifiedModuleId = QualifiedModuleId.Create(ModuleLiteralId.Create(specPath), coercedQualifierId);

            if (!moduleRegistry.TryGetInstantiatedModule(qualifiedModuleId, out var qualifiedFileModule))
            {
                var q = frontEndHost.FrontEndContext.QualifierTable.GetQualifier(coercedQualifierId).ToDisplayString(FrontEndContext.StringTable);
                XAssert.Fail(
                "Fail to get qualified file module '{0}' with qualifier '{1}' because such a module has not been instantiated",
                specPath.ToString(FrontEndContext.PathTable),
                q);
                return null;
            }

            return qualifiedFileModule;
        }

        protected string TemporaryDirectory => TestOutputDirectory;

        protected StringId CreateString(string value)
        {
            Contract.Requires(value != null);
            return StringId.Create(FrontEndContext.StringTable, value);
        }

        protected AbsolutePath CreatePath(string value)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(value));

            return !AbsolutePath.TryCreate(FrontEndContext.PathTable, value, out var path) ? AbsolutePath.Invalid : path;
        }

        protected PathAtom CreatePathAtom(string value)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(value));

            return PathAtom.Create(FrontEndContext.StringTable, value);
        }

        protected RelativePath CreateRelativePath(string relative)
        {
            return RelativePath.Create(StringTable, relative);
        }

        protected string ToString(AbsolutePath path)
        {
            return PathUtil.NormalizePath(path.ToString(FrontEndContext.PathTable));
        }

        protected void AssertSyntaxError(Diagnostic result)
        {
            Assert.Equal((int)LogEventId.TypeScriptSyntaxError, result.ErrorCode);
        }

        private RuntimeModelFactory CreateAstTranslater(IFrontEndConfiguration configuration, IEnumerable<string> customRules = null, Workspace workspace = null)
        {
            var conversionConfiguration = new AstConversionConfiguration(
                policyRules: customRules ?? new string[] { },
                degreeOfParallelism: 1,
                disableLanguagePolicies: configuration.DisableLanguagePolicyAnalysis(),
                useLegacyOfficeLogic: false)
            {
                PreserveFullNameSymbols = true,
            };

            var result = new RuntimeModelFactory(
                ParseAndEvaluateLogger,
                FrontEndContext.LoggingContext,
                FrontEndStatistics,
                conversionConfiguration,
                workspace: workspace);
            return result;
        }

        /// <summary>
        /// Creates a workspace factory for the DScript front end for testing.
        /// Known DScript-related source resolvers are registered.
        /// </summary>
        public static DScriptWorkspaceResolverFactory CreateWorkspaceFactoryForTesting(FrontEndContext context, Logger logger = null)
        {
            var workspaceFactory = new DScriptWorkspaceResolverFactory();
            workspaceFactory.RegisterResolver(KnownResolverKind.DScriptResolverKind,
                () => new WorkspaceSourceModuleResolver(context.StringTable, new FrontEndStatistics(), logger));
            workspaceFactory.RegisterResolver(KnownResolverKind.SourceResolverKind,
                () => new WorkspaceSourceModuleResolver(context.StringTable, new FrontEndStatistics(), logger));
            workspaceFactory.RegisterResolver(KnownResolverKind.DefaultSourceResolverKind,
                () => new WorkspaceDefaultSourceModuleResolver(context.StringTable, new FrontEndStatistics(), logger));

            return workspaceFactory;
        }

        /// <summary>
        /// Creates a front-end for testing.
        /// </summary>
        internal DScriptFrontEnd CreateScriptFrontEndForTesting(
            FrontEndHost frontEndHost,
            FrontEndContext context,
            Logger logger = null,
            IDScriptResolverSettings resolverSettings = null)
        {
            Contract.Requires(frontEndHost != null);
            Contract.Requires(context != null);

            var workspaceFactory = CreateWorkspaceFactoryForTesting(FrontEndContext, logger);

            var frontEnd = new DScriptFrontEnd(new FrontEndStatistics(), logger, evaluationDecorator: null);
            frontEnd.InitializeFrontEnd(frontEndHost, context, new ConfigurationImpl());

            // If there is no resolver settings, we create an empty one.
            if (resolverSettings == null)
            {
                var sourceResolverSettings = new SourceResolverSettings
                {
                    Modules = new List<AbsolutePath>(),
                    Kind = KnownResolverKind.DScriptResolverKind
                };
                resolverSettings = sourceResolverSettings;
            }

            workspaceFactory.Initialize(context, frontEndHost, frontEndHost.Configuration, requestedQualifiers: new QualifierId[] { context.QualifierTable.EmptyQualifierId });
            var workspaceResolver = workspaceFactory.TryGetResolver(resolverSettings).Result;

            var resolver = frontEnd.CreateResolver(resolverSettings.Kind);
            bool success = resolver.InitResolverAsync(resolverSettings, workspaceResolver).GetAwaiter().GetResult();
            Contract.Assert(success);

            return frontEnd;
        }

        /// <summary>
        /// Creates a package configuration file.
        /// </summary>
        public static string CreatePackageConfig(
            string packageName,
            bool useImplicitReferenceSemantics = false,
            List<string> projects = null,
            List<string> allowedDependencies = null,
            List<string> cyclicalFriendModules = null,
            string mainFile = null)
        {
            string @implicit =
                useImplicitReferenceSemantics
                ? "NameResolutionSemantics.implicitProjectReferences,"
                : "NameResolutionSemantics.explicitProjectReferences,";

            string projectsField = projects != null ? I($"\r\n\tprojects: [{string.Join(", ", projects)}],") : string.Empty;

            var dependencies = allowedDependencies != null
                ? I($"\r\n\tallowedDependencies: [{string.Join(", ", allowedDependencies.Select(dep => I($@"""{dep}""")))}],")
                : string.Empty;

            var cyclicalFriends = cyclicalFriendModules != null
                ? I($"\r\n\tcyclicalFriendModules: [{string.Join(", ", cyclicalFriendModules.Select(dep => I($@"""{dep}""")))}],")
                : string.Empty;

            mainFile = string.IsNullOrEmpty(mainFile) ? string.Empty : $"main: f`{mainFile}`,";
            var moduleCall = string.IsNullOrEmpty(mainFile) ? "module" : "package";

            return $@"
{moduleCall}({{
    name: ""{packageName}"",
    nameResolutionSemantics: {@implicit}
    {mainFile}
    {projectsField}
    {dependencies}
    {cyclicalFriends}
}});";
        }

        protected string CreatePackageConfig(string packageName, bool useImplicitReferenceSemantics, params string[] projects)
        {
            string @implicit =
                useImplicitReferenceSemantics
                ? "NameResolutionSemantics.implicitProjectReferences"
                : "NameResolutionSemantics.explicitProjectReferences";

            var projectFiles = projects.Select(p => $"p`{p}`").ToList();
            var projectsProperty = string.Join(", ", projectFiles);
            return $@"
module({{
    name: ""{packageName}"",
    nameResolutionSemantics: {@implicit},
    projects: [{projectsProperty}],
}});";
        }

        private IFrontEndStatistics FrontEndStatistics { get; } = new FrontEndStatistics();

        protected static string CommandLineApi
        {
            get
            {
                var files = new[]
                {
                    "Annotation.dsc",
                    "Artifact.dsc",
                    "Cmd.dsc",
                    "Tool.dsc",
                    "Transformer.dsc",
                    "Transformer.Copy.dsc",
                    "Transformer.Dependencies.dsc",
                    "Transformer.Execute.dsc",
                    "Transformer.Ipc.dsc",
                    "Transformer.SealedDirectories.dsc",
                    "Transformer.Service.dsc",
                    "Transformer.ToolDefinition.dsc",
                    "Transformer.Write.dsc",
                };

                return string
                    .Join(
                        Environment.NewLine,
                        files
                            .Select(fileName => PathGeneratorUtilities.GetRelativePath("Sdk", "Sdk.Transformers", fileName))
                            .Select(relativePath => Path.Combine(AssemblyDirectory, relativePath))
                            .Select(filePath => File.ReadAllText(filePath)))
                    .Replace("@@public", string.Empty);
            }
        }

        public static string AssemblyDirectory
        {
            get
            {
                string location = AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly(), computeAssemblyLocation: true);
                if (OperatingSystemHelper.IsUnixOS)
                {
                    location = "file://" + location;
                }
                UriBuilder uri = new UriBuilder(location);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        public static void ValidateErrorText(string expected, string found, params string[] args)
        {
            string expectedFormatted = string.Format(expected, args);

            Assert.True(found.EndsWith(expectedFormatted, StringComparison.Ordinal), $"Expected error text '{expectedFormatted}' to match the end of found text '{found}'.");
        }

        protected static void CheckArray<T>(object expected, object actual)
        {
            var expectedArray = expected as ArrayLiteral;
            var actualArray = actual as ArrayLiteral;
            Assert.NotNull(expectedArray);
            Assert.NotNull(actualArray);
            Assert.Equal(expectedArray.Count, actualArray.Length);

            for (var i = 0; i < expectedArray.Length; ++i)
            {
                Assert.IsType<T>(expectedArray[i].Value);
                Assert.IsType<T>(actualArray[i].Value);
                Assert.Equal(expectedArray[i].Value, (T)actualArray[i].Value);
            }
        }

        protected static void CheckArray<T>(T[] expected, object actual)
        {
            var expectedArray = expected;
            var actualArray = actual as ArrayLiteral;
            Assert.NotNull(expectedArray);
            Assert.NotNull(actualArray);
            Assert.Equal(expectedArray.Length, actualArray.Length);

            for (var i = 0; i < expectedArray.Length; ++i)
            {
                Assert.IsType<T>(expectedArray[i]);
                Assert.IsType<T>(actualArray[i].Value);
                Assert.Equal(expectedArray[i], (T)actualArray[i].Value);
            }
        }

        protected static ArrayLiteral CreateArrayLiteral(object[] arrays)
        {
            return ArrayLiteral.CreateWithoutCopy(arrays.Select(o => EvaluationResult.Create(o)).ToArray(), default(TypeScript.Net.Utilities.LineInfo), AbsolutePath.Invalid);
        }

        protected string RetrieveProcessArguments(global::BuildXL.Pips.Operations.Process process)
        {
            string arguments = process.Arguments.ToString(PathTable);

            // If there is a response file, just concatenate all arguments together
            if (process.ResponseFileData.IsValid)
            {
                arguments += process.ResponseFileData.ToString(PathTable);
            }

            return arguments;
        }
    }
}
