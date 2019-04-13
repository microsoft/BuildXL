// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using TypeScript.Net;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using ConversionException = BuildXL.FrontEnd.Script.Util.ConversionException;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using SourceFile = BuildXL.FrontEnd.Script.SourceFile;
using IFileSystem = global::BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem;


namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Base class for DScript-based front-ends and resolvers.
    /// </summary>
    public class DScriptInterpreterBase
    {
        /// <nodoc/>
        protected IFrontEndStatistics FrontEndStatistics { get; }

        /// <summary>
        /// Gets the empty qualifier.
        /// </summary>
        /// <remarks>
        /// Many non-project modules do not have qualifier space (e.g., configuration, package configuration).
        /// We instantiate them simply using the empty qualifier.
        /// </remarks>
        protected QualifierValue EmptyQualifier { get; private set; }
        /// <nodoc />
        protected QualifierValueCache QualifierValueCache { get; } = new QualifierValueCache();

        ///<nodoc/>
        protected IFileSystem m_fileSystem => Context.FileSystem;

        private IConfiguration m_configuration;
       
        /// <summary>
        /// Gets the configuration used for evaluating configs:
        ///   - doesn't track method invocations
        ///   - uses the same cycle detector startup delay as specified in <see cref="IFrontEndConfiguration.CycleDetectorStartupDelay"/>
        /// </summary>
        protected EvaluatorConfiguration EvaluatorConfigurationForConfig => new EvaluatorConfiguration(
            trackMethodInvocations: false,
            cycleDetectorStartupDelay: TimeSpan.FromSeconds(m_configuration.FrontEnd.CycleDetectorStartupDelay()));

        /// <nodoc />
        protected DScriptInterpreterBase(GlobalConstants constants, ModuleRegistry sharedModuleRegistry, IFrontEndStatistics statistics, Logger logger)
        {
            Contract.Requires(constants != null);
            Contract.Requires(sharedModuleRegistry != null);
            Contract.Requires(statistics != null);

            Constants = constants;
            SharedModuleRegistry = sharedModuleRegistry;
            Name = "DScript";
            FrontEndStatistics = statistics;
            Logger = logger ?? Logger.CreateLogger();
            Statistics = new EvaluationStatistics();
        }

        ///<inhertdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        protected DScriptInterpreterBase(GlobalConstants constants, ModuleRegistry sharedModuleRegistry, IFrontEndStatistics statistics, Logger logger,
            FrontEndHost host, FrontEndContext context, IConfiguration configuration)
            : this(constants, sharedModuleRegistry, statistics, logger)
        {
            // ReSharper disable once VirtualMemberCallInConstructor
            // The call is safe and used only for testing purposes.
            InitializeInterpreter(host, context, configuration);
        }

        /// <nodoc/>
        public virtual void InitializeInterpreter(FrontEndHost host, FrontEndContext context, IConfiguration configuration)
        {
            FrontEndHost = host;
            Context = context;
            m_configuration = configuration;
            EmptyQualifier = QualifierValue.CreateEmpty(context.QualifierTable);
            IsBeingDebugged = configuration.FrontEnd.DebugScript();
        }

        /// <nodoc />
        protected IFrontEndConfiguration FrontEndConfiguration
        {
            get
            {
                Contract.Assume(m_configuration != null, "Initialize method should be called before accessing front-end configuration.");
                return m_configuration.FrontEnd;
            }
        }

        /// <nodoc />
        protected IConfiguration Configuration
        {
            get
            {
                Contract.Assume(m_configuration != null, "Initialize method should be called before accessing the configuration.");
                return m_configuration;
            }
        }

        /// <nodoc />
        protected EvaluationStatistics Statistics { get; }

        /// <nodoc />
        protected GlobalConstants Constants { get; }

        // TODO: Solve the entanglement of all frontends needing to share the module registry.
        // This should move to the Host.

        /// <nodoc />
        protected ModuleRegistry SharedModuleRegistry { get; }

        /// <nodoc />
        protected Logger Logger { get; }

        /// <summary>
        /// Gets a value indicating whether DScript evaluation is being debugged or not.
        /// It uses this information to decide what kind of context to create, i.e., one that that
        /// creates StackEntry elements with more information (to support the debugger) or less
        /// information (only what's needed for evaluation).
        ///
        /// Note that this assembly has no knowledge of the actual debugger, and does not
        /// depend on the <code>BuildXL.FrontEnd.Script.Debugger.dll</code> assembly.
        /// </summary>
        protected bool IsBeingDebugged { get; private set; }

        /// <summary>
        /// Gets or sets the name of the front-end.
        /// </summary>
        public string Name { get; protected set; }

        /// <nodoc />
        public FrontEndHost FrontEndHost { get; private set; }

        /// <nodoc />
        public FrontEndContext Context { get; private set; }

        /// <nodoc/>
        protected virtual FrontEndEngineAbstraction Engine => FrontEndHost.Engine;

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected FileModuleLiteral InstantiateModuleWithDefaultQualifier(FileModuleLiteral module)
        {
            return module.InstantiateFileModuleLiteral(SharedModuleRegistry, EmptyQualifier);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected FileModuleLiteral InstantiateModule(FileModuleLiteral module, QualifierValue qualifier)
        {
            return module.InstantiateFileModuleLiteral(SharedModuleRegistry, qualifier);
        }

        private DiagnosticAnalyzer m_linter;

        /// <summary>
        /// Creates a linter given <see cref="AstConversionConfiguration"/>.
        /// </summary>
        protected DiagnosticAnalyzer CreateLinter(AstConversionConfiguration configuration)
        {
            return DiagnosticAnalyzer.Create(
                Logger,
                Context.LoggingContext,
                new HashSet<string>(configuration.PolicyRules),
                configuration.DisableLanguagePolicies);
        }

        /// <summary>
        /// If a linter has already been created by this method (for any given configuration),
        /// returns the existing linter; otherwise, creates a linter given <see cref="AstConversionConfiguration"/>,
        /// saves it, and returns it.
        /// </summary>
        protected DiagnosticAnalyzer GetOrCreateLinter(AstConversionConfiguration configuration)
        {
            return m_linter ?? (m_linter = CreateLinter(configuration));
        }

        /// <summary>
        /// Creates a parser based on the front end configuration passed at initialization time.
        /// </summary>
        /// <remarks>
        /// The created parser schedules parsing imports as it finds them
        /// </remarks>
        protected RuntimeModelFactory CreateRuntimeModelFactory(Workspace workspace)
        {
            var configuration = AstConversionConfiguration.FromConfiguration(FrontEndConfiguration);

            return new RuntimeModelFactory(
                FrontEndStatistics,
                configuration,
                GetOrCreateLinter(configuration),
                workspace);
        }

        /// <summary>
        /// Registers parsed module.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected void RegisterSuccessfullyParsedModule(SourceFile sourceFile, FileModuleLiteral moduleLiteral, Package package)
        {
            Contract.Requires(moduleLiteral != null);
            Contract.Requires(package != null);

            var moduleData = new UninstantiatedModuleInfo(
                sourceFile,
                moduleLiteral,
                Context.QualifierTable.EmptyQualifierSpaceId);

            RegisterModuleData(moduleData);
        }

        /// <summary>
        /// Registers module data.
        /// </summary>
        protected void RegisterModuleData(UninstantiatedModuleInfo moduleInfo)
        {
            Contract.Requires(moduleInfo != null);
            SharedModuleRegistry.AddUninstantiatedModuleInfo(moduleInfo);
        }

        /// <summary>
        /// Registers parsed module.
        /// </summary>
        protected void RegisterSuccessfullyParsedModule<T>(SourceFile sourceFile, T parseResult, Package package) where T : SourceFileParseResult
        {
            Contract.Requires(parseResult != null);
            Contract.Requires(parseResult.Success);
            Contract.Requires(package != null);

            var moduleData = new UninstantiatedModuleInfo(
                sourceFile,
                parseResult.Module,
                parseResult.QualifierSpaceId.IsValid ? parseResult.QualifierSpaceId : Context.QualifierTable.EmptyQualifierSpaceId);

            RegisterModuleData(moduleData);
        }

        /// <nodoc />
        [Pure]
        protected bool IsConfigFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            var name = path.GetName(Context.PathTable).ToString(Context.StringTable);
            return ExtensionUtilities.IsGlobalConfigurationFile(name);
        }

        /// <nodoc />
        protected ContextTree CreateContext(
            FileModuleLiteral instantiatedModule,
            IDecorator<EvaluationResult> decorator,
            EvaluatorConfiguration configuration,
            FileType fileType)
        {
            Contract.Requires(instantiatedModule != null);

            return new ContextTree(
                FrontEndHost,
                Context,
                Constants,
                SharedModuleRegistry,
                Logger,
                Statistics,
                qualifierValueCache: QualifierValueCache,
                isBeingDebugged: IsBeingDebugged,
                decorator: decorator,
                module: instantiatedModule,
                configuration: configuration,
                evaluationScheduler: EvaluationScheduler.Default,
                fileType: fileType);
        }

        /// <nodoc />
        protected ContextTree CreateContext(
            FileModuleLiteral instantiatedModule,
            IEvaluationScheduler evaluationScheduler,
            IDecorator<EvaluationResult> decorator,
            EvaluatorConfiguration configuration,
            FileType fileType)
        {
            Contract.Requires(instantiatedModule != null);
            Contract.Requires(evaluationScheduler != null);

            return new ContextTree(
                FrontEndHost,
                Context,
                Constants,
                SharedModuleRegistry,
                Logger,
                Statistics,
                qualifierValueCache: QualifierValueCache,
                isBeingDebugged: IsBeingDebugged,
                decorator: decorator,
                module: instantiatedModule,
                configuration: configuration,
                evaluationScheduler: evaluationScheduler,
                fileType: fileType);
        }

        /// <nodoc />
        protected RuntimeModelContext CreateParserContext(IResolver resolver, Package package, LocationData? origin)
        {
            return new RuntimeModelContext(
                FrontEndHost,
                frontEndContext: Context,
                logger: Logger,
                package: package,
                globals: Constants.Global,
                moduleRegistry: SharedModuleRegistry,
                origin: origin ?? default(LocationData));
        }

        /// <nodoc/>
        protected Package CreateDummyPackageFromPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            var pathStr = path.ToString(Context.PathTable);
            var id = PackageId.Create(StringId.Create(Context.StringTable, pathStr));
            var desc = new PackageDescriptor
            {
                Name = path.ToString(Context.PathTable),
            };

            return Package.Create(id, path, desc);
        }

        /// <nodoc/>
        public string GetConversionExceptionMessage(AbsolutePath path, ConversionException exception)
        {
            var arrayLiteral = exception.ErrorContext.ObjectCtx as ArrayLiteral;
            var objectLiteral = exception.ErrorContext.ObjectCtx as ObjectLiteral;
            var location = default(LineInfo);

            if (arrayLiteral != null)
            {
                location = arrayLiteral.Location;
            }
            else if (objectLiteral != null)
            {
                location = objectLiteral.Location;
            }

            string message = I($"{path.ToString(Context.PathTable)}({location.Line.ToString()},{location.Position.ToString()}): ");

            if (arrayLiteral == null && objectLiteral == null)
            {
                return I($"{message}{exception.Message}");
            }

            if (arrayLiteral != null)
            {
                string elementMessage = exception.ErrorContext.Pos >= 0
                    ? string.Format(
                        CultureInfo.InvariantCulture, " (for the {0}-th element of array literal)",
                        exception.ErrorContext.Pos.ToString())
                    : string.Empty;

                return string.Format(
                    CultureInfo.InvariantCulture, "{0}{1}{2}",
                    message,
                    exception.Message,
                    elementMessage);
            }

            string propertyMessage = exception.ErrorContext.Name.IsValid
                ? string.Format(
                    CultureInfo.InvariantCulture, " (for the property '{0}' of object literal)",
                    exception.ErrorContext.Name.ToString(Context.StringTable))
                : string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture, "{0}{1}{2}",
                message,
                exception.Message,
                propertyMessage);
        }

        /// <summary>
        /// Default DScript implementation of <see cref="IWorkspaceModuleResolver.TryParseAsync"/>.
        /// </summary>
        public virtual async Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOptions = null)
        {
            Contract.Requires(pathToParse.IsValid);

            var specPathString = pathToParse.ToString(Context.PathTable);

            // If the option to use the public facade of a spec is on, we try to honor it
            if (parsingOptions?.UseSpecPublicFacadeAndAstWhenAvailable == true)
            {
                var facadeWithAst = await FrontEndHost.FrontEndArtifactManager.TryGetPublicFacadeWithAstAsync(pathToParse);
                if (facadeWithAst != null)
                {
                    return new Possible<ISourceFile>(
                        ParseSourceFileContent(
                            parsingOptions,
                            specPathString,
                            facadeWithAst.PublicFacadeContent,
                            () =>
                            {
                                using (FrontEndStatistics.CounterWithRootCause.Start())
                                {
                                    return CreateTextSource(facadeWithAst.PublicFacadeContent);
                                }
                            },
                            FrontEndStatistics,
                            facadeWithAst.SerializedAst));
                }
            }

            var maybeContent = await Engine.GetFileContentAsync(pathToParse);
            if (!maybeContent.Succeeded)
            {
                return HandleIoError(pathToParse, moduleOrConfigPathPromptingParse, maybeContent.Failure.Exception);
            }

            if (!maybeContent.Result.IsValid)
            {
                return HandleContentIsInvalid(pathToParse, moduleOrConfigPathPromptingParse);
            }

            var sourceFile = ParseSourceFileContent(
                parsingOptions,
                specPathString,
                maybeContent.Result,
                () =>
                {
                    using (FrontEndStatistics.CounterWithRootCause.Start())
                    {
                        // Need to reparse the file content in the error case.
                        return CreateTextSource(Engine.GetFileContentAsync(pathToParse).GetAwaiter().GetResult().Result);
                    }
                },
                FrontEndStatistics,
                serializedAst: ByteContent.Invalid);

            return new Possible<ISourceFile>(sourceFile);
        }

        private Possible<ISourceFile> HandleContentIsInvalid(
            AbsolutePath pathToParse,
            AbsolutePath moduleOrConfigPathPromptingParse)
        {
            var specPathString = pathToParse.ToString(Context.PathTable);
            var location = new Location() { File = moduleOrConfigPathPromptingParse.ToString(Context.PathTable) };

            Logger.ReportFailReadFileContent(
                Context.LoggingContext,
                location,
                specPathString,
                "File content is unavailable.");

            return new CannotReadSpecFailure(specPathString, CannotReadSpecFailure.CannotReadSpecReason.ContentUnavailable);
        }

        private Possible<ISourceFile> HandleIoError(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, BuildXLException failureException)
        {
            var specPathString = pathToParse.ToString(Context.PathTable);
            var location = new Location() { File = moduleOrConfigPathPromptingParse.ToString(Context.PathTable) };

            if (failureException.InnerException is IOException)
            {
                if (m_fileSystem.Exists(AbsolutePath.Create(Context.PathTable, specPathString)))
                {
                    Logger.ReportFailReadFileContent(
                        Context.LoggingContext,
                        location,
                        specPathString,
                        "File not found.");

                    return new CannotReadSpecFailure(specPathString, CannotReadSpecFailure.CannotReadSpecReason.SpecDoesNotExist);
                }
            }

            if (failureException.InnerException is UnauthorizedAccessException)
            {
                if (Directory.Exists(specPathString))
                {
                    Logger.ReportFailReadFileContent(
                        Context.LoggingContext,
                        location,
                        specPathString,
                        "Expected a file path, parser can not parse a directory.");

                    return new CannotReadSpecFailure(specPathString, CannotReadSpecFailure.CannotReadSpecReason.PathIsADirectory);
                }
            }

            Logger.ReportFailReadFileContent(
                Context.LoggingContext,
                location,
                specPathString,
                "Unexpected error: " + failureException);

            return new CannotReadSpecFailure(specPathString, CannotReadSpecFailure.CannotReadSpecReason.IoException);
        }

        /// <summary>
        /// Parses a given file content.
        /// </summary>
        public ISourceFile ParseSourceFileContent(
            ParsingOptions parsingOptions,
            string specPathString,
            FileContent content,
            Func<TextSource> textSourceProvider,
            IFrontEndStatistics frontEndStatistics,
            ByteContent serializedAst)
        {
            frontEndStatistics = frontEndStatistics ?? new FrontEndStatistics();
            var parser = serializedAst.IsValid
                ? new PublicSurfaceParser(Context.PathTable, serializedAst.Content, serializedAst.Length)
                : parsingOptions.ConvertPathLikeLiteralsAtParseTime
                    ? new DScriptParser(Context.PathTable)
                    : new TypeScript.Net.Parsing.Parser();

            ISourceFile sourceFile;
            using (frontEndStatistics.SpecParsing.Start(specPathString))
            {
                sourceFile = parser.ParseSourceFileContent(specPathString, CreateTextSource(content), new FuncBasedTextSourceProvider(textSourceProvider), parsingOptions);
            }

            // add ast-level stats
            frontEndStatistics.SourceFileIdentifiers.Increment(sourceFile.IdentifierCount, specPathString);
            frontEndStatistics.SourceFileLines.Increment(sourceFile.LineMap.Map.LongLength, specPathString);
            frontEndStatistics.SourceFileChars.Increment(content.Length, specPathString);
            frontEndStatistics.SourceFileNodes.Increment(sourceFile.NodeCount, specPathString);

            return sourceFile;
        }

        /// <summary>
        /// Factory method for creating a <see cref="TextSource"/> from <see cref="FileContent"/>.
        /// </summary>
        protected static TextSource CreateTextSource(FileContent content)
        {
            return TextSource.FromCharArray(content.Content, content.Length);
        }

        /// <summary>
        /// A <see cref="Func{TextSource}"/>-backed implementation of <see cref="ITextSourceProvider"/>.
        /// </summary>
        protected sealed class FuncBasedTextSourceProvider : ITextSourceProvider
        {
            private readonly Func<TextSource> m_textSourceFunc;

            /// <nodoc/>
            public FuncBasedTextSourceProvider(Func<TextSource> textSourceFunc)
            {
                m_textSourceFunc = textSourceFunc;
            }

            /// <nodoc/>
            public TextSource ReadTextSource()
            {
                return m_textSourceFunc();
            }
        }
    }
}
