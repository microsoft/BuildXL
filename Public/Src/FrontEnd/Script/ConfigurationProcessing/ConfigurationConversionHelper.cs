// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using Binder = TypeScript.Net.Binding.Binder;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Helper for parsing, validating, and converting config files (both primary and module configurations)
    /// </summary>
    /// <remarks>
    /// This class is only responsible for steps up to and including conversion.  Class <see cref="ConfigurationProcessor"/>,
    /// which makes use of this class, additionally implements the <see cref="IConfigurationProcessor"/> interface which is concerned
    /// with interpreting the content of the primary configuration file.  Similarly, class <see cref="WorkspaceSourceModuleResolver"/>
    /// also makes use of this class and additionally implements the <see cref="IWorkspaceModuleResolver"/> interface which also has to deal
    /// with interpreting the content of module configuration files.
    /// </remarks>
    public class ConfigurationConversionHelper : DScriptInterpreterBase
    {
        /// <summary>
        /// Configuration file kind.
        /// </summary>
        public enum ConfigurationKind
        {
            /// <summary>
            /// Primary configuration.
            /// </summary>
            PrimaryConfig,

            /// <summary>
            /// Module configuration.
            /// </summary>
            ModuleConfig,
        }

        private ConfigurationKind Kind { get; }

        private AstConversionConfiguration ConversionConfiguration { get; }

        private Lazy<DiagnosticAnalyzer> Linter { get; }

        private int DegreeOfParallelism => Configuration.FrontEnd.MaxFrontEndConcurrency();

        private string UnimplementedOperationForConfigKindErrorMessage => "Unimplemented support for ConfigurationKind." + Kind;

        /// <summary>
        /// During configuration parsing <see cref="FrontEndHost.Engine"/> is null, so we create an instance
        /// of <see cref="SimpleFrontEndEngineAbstraction"/> just for the sake of being able to read files.
        /// </summary>
        protected override FrontEndEngineAbstraction Engine { get; }

        /// <nodoc />
        public ConfigurationConversionHelper(
            [CanBeNull] FrontEndEngineAbstraction engine,
            ConfigurationKind kind,
            Logger logger,
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IFrontEndStatistics statistics)
            : base(statistics, logger, host, context, configuration)
        {
            Engine = engine ?? new SimpleFrontEndEngineAbstraction(context.PathTable, context.FileSystem, configuration);
            Kind = kind;
            ConversionConfiguration = AstConversionConfiguration.ForConfiguration(FrontEndConfiguration);
            Linter = Lazy.Create(() => CreateLinter(ConversionConfiguration));
        }

        private static ParsingOptions ParsingOptions { get; } = ParsingOptions.GetPreludeParsingOptions(escapeIdentifiers: true);

        private static WorkspaceConfiguration WorkspaceConfiguration { get; } = new WorkspaceConfiguration(
            resolverSettings: new List<IResolverSettings>(0),
            constructFingerprintDuringParsing: false,
            maxDegreeOfParallelismForParsing: 1,
            maxDegreeOfParallelismForTypeChecking: 1,
            parsingOptions: ParsingOptions,
            cancelOnFirstFailure: false,
            includePreludeWithName: null,
            trackFileToFileDepedendencies: false,
            cancellationToken: null);

        /// <summary>
        /// Parses config only for the purpose of getting the configuration object.
        /// </summary>
        public async Task<Workspace> ParseAndValidateConfigFileAsync(AbsolutePath configPath, bool typecheck)
        {
            Contract.Requires(configPath.IsValid);

            // parse and validate config
            var specFileMap = await ParseConfigFileAsync(configPath);
            var specFile = specFileMap[configPath];
            if (specFile == null)
            {
                Contract.Assume(Logger.HasErrors, "Error should have been logged for missing config file");
                return null;
            }

            ValidateConfigFile(specFileMap[configPath]);

            if (Logger.HasErrors)
            {
                return null;
            }

            // create workspace and check for errors
            return await CreateWorkspaceAsync(configPath, specFileMap, typecheck);
        }

        /// <summary>
        /// Parses, validates, and converts a given configuration file for the purpose of getting the configuration object.
        /// </summary>
        public async Task<ConfigConversionResult> ParseValidateAndConvertConfigFileAsync(AbsolutePath configPath)
        {
            var configStatistics = FrontEndStatistics.LoadConfigStatistics;
            using (configStatistics.TotalDuration.Start())
            {
                Workspace workspace;
                using (configStatistics.ParseDuration.Start())
                {
                    workspace = await ParseAndValidateConfigFileAsync(configPath, typecheck: true);

                    if (workspace == null)
                    {
                        return new ConfigConversionResult(Logger.ErrorCount);
                    }
                }

                if (!workspace.Succeeded)
                {
                    ReportErrorDiagnostics(workspace.GetAllParsingAndBindingErrors());
                    ReportErrorDiagnostics(workspace.GetSemanticModel()?.GetAllSemanticDiagnostics());
                    ReportConfigParsingFailed(workspace.Failures.Select(f => f.Describe()));
                    return new ConfigConversionResult(workspace.Failures.Count);
                }

                // convert every spec in the workspace
                IReadOnlyCollection<FileModuleLiteral> convertedModules;

                using (configStatistics.ConversionDuration.Start())
                {
                    convertedModules = ConvertWorkspaceInParallel(workspace, configPath);

                    if (Logger.HasErrors)
                    {
                        return new ConfigConversionResult(Logger.ErrorCount);
                    }
                }

                configStatistics.FileCountCounter.Increment(workspace.AllSpecCount);

                FileModuleLiteral configModule = convertedModules.First(m => m.Path == configPath);
                SymbolAtom configKeyword = GetConfigKeyword(workspace.ConfigurationModule.Specs[configPath].SourceFile);
                return new ConfigConversionResult(configModule, configKeyword);
            }
        }

        /// <summary>
        /// Starts by parsing <paramref name="configPath"/>, recursively continuing to parse any files imported via an 'importFile' call.
        ///
        /// Any errors are logged to <see cref="Logger"/>.
        ///
        /// Returns a map of parsed files; the result is never null, but in case of an error the content may be unspecified.
        /// </summary>
        private async Task<IReadOnlyDictionary<AbsolutePath, ISourceFile>> ParseConfigFileAsync(AbsolutePath configPath)
        {
            // Set of specs being processed or queued for processing
            var queuedSpecs = new ConcurrentDictionary<AbsolutePath, Unit>() { { configPath, Unit.Void } };

            // Set of parsed files
            var result = new ConcurrentDictionary<AbsolutePath, ISourceFile>();
            await ParallelAlgorithms.WhenDoneAsync(
                DegreeOfParallelism,
                Context.CancellationToken,
                async (addItem, path) =>
                {
                    // TODO: File bug to ensure we fail on errors.
                    var parseResult = await ParseFileAndDiscoverImportsAsync(path);

                    var numberOfProcessedConfigs = FrontEndStatistics.ConfigurationProcessing.Increment();

                    NotifyProgress(numberOfProcessedConfigs);

                    result[path] = parseResult.Source;

                    if (parseResult.Imports?.Count > 0)
                    {
                        foreach (var dependency in parseResult.Imports)
                        {
                            // Add the dependency for parsing only if the dependency was not processed or scheduled for processing.
                            if (queuedSpecs.TryAdd(dependency, Unit.Void))
                            {
                                addItem(dependency);
                            }
                        }
                    }
                }, 
                configPath);

            return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private void NotifyProgress(int processedSpecNumber)
        {
            FrontEndStatistics.WorkspaceProgress?.Invoke(this, WorkspaceProgressEventArgs.Create(ProgressStage.BuildingWorkspaceDefinition, processedSpecNumber));
        }

        private class ParseResult
        {
            public AbsolutePath Path { get; }

            public ISourceFile Source { get; }

            public IReadOnlyList<AbsolutePath> Imports { get; }

            public ParseResult(AbsolutePath path, ISourceFile source, IReadOnlyList<AbsolutePath> imports)
            {
                Path = path;
                Source = source;
                Imports = imports;
            }
        }

        /// <summary>
        /// Parsed the <paramref name="configPath"/> file using special <see cref="TypeScript.Net.DScript.ParsingOptions.CollectImportFile"/>
        /// option to collect all invocations of the 'importFile' function.  Returns the parsed file (as an <see cref="ISourceFile"/>)
        /// and a list of <see cref="AbsolutePath"/>s representing files imported from the config.  If any error happens, uses the
        /// <see cref="Logger"/> to report it.  In the case of an error, the returned value is unspecified.
        /// </summary>
        private async Task<ParseResult> ParseFileAndDiscoverImportsAsync(AbsolutePath configPath)
        {
            var maybeParsedConfig = await TryParseAsync(configPath, configPath, ParsingOptions.WithCollectImportFile(true));

            if (!maybeParsedConfig.Succeeded)
            {
                ReportConfigParsingFailed(maybeParsedConfig.Failure.Describe());
                return new ParseResult(configPath, null, null);
            }

            var configSourceFile = maybeParsedConfig.Result;
            if (configSourceFile.ParseDiagnostics.Count != 0)
            {
                ReportErrorDiagnostics(configSourceFile.ParseDiagnostics);
                return new ParseResult(configPath, configSourceFile, null);
            }

            // Files should be bound for the following ast conversion.
            Binder.Bind(configSourceFile, CompilerOptions.Empty);
            if (configSourceFile.BindDiagnostics.Count != 0)
            {
                ReportErrorDiagnostics(configSourceFile.BindDiagnostics);
                return new ParseResult(configPath, configSourceFile, null);
            }

            var configDirectory = configPath.GetParent(Context.PathTable);
            var importedFiles = configSourceFile.LiteralLikeSpecifiers
                .Select(ConvertPathFromLiteralToAbsolutePath)
                .Where(path => path.IsValid)
                .ToList();

            return new ParseResult(configPath, configSourceFile, importedFiles);

            AbsolutePath ConvertPathFromLiteralToAbsolutePath(ILiteralExpression literal)
            {
                if (AbsolutePath.TryCreate(Context.PathTable, literal.Text, out AbsolutePath absolutePath))
                {
                    return absolutePath;
                }
                else if (RelativePath.TryCreate(Context.StringTable, literal.Text, out RelativePath importRelativePath))
                {
                    return configDirectory.Combine(Context.PathTable, importRelativePath);
                }
                else
                {
                    var location = literal.GetLineInfo(configSourceFile).ToLocationData(configPath).ToLogLocation(Context.PathTable);
                    ReportConfigParsingFailed("Invalid path: " + literal.Text, location);
                    return AbsolutePath.Invalid;
                }
            }
        }

        private async Task<Workspace> CreateWorkspaceAsync(AbsolutePath configPath, IReadOnlyDictionary<AbsolutePath, ISourceFile> specFileMap, bool typecheck)
        {
            var moduleDefinition = ModuleDefinition.CreateConfigModuleDefinition(Context.PathTable, configPath, specFileMap.Keys);
            var parsedModule = new ParsedModule(moduleDefinition, specFileMap);

            // if type checking is not requested --> directly create a workspace from the given source file map
            if (!typecheck)
            {
                return Workspace.CreateConfigurationWorkspace(WorkspaceConfiguration, parsedModule, preludeModule: null);
            }

            // otherwise, request a prelude module from the PreludeManager and proceed to type checking
            var maybePrelude = await GetPreludeParsedModule(configPath);
            if (!maybePrelude.Succeeded)
            {
                return Workspace.Failure(null, WorkspaceConfiguration, maybePrelude.Failure);
            }

            var preludeModule = (ParsedModule)maybePrelude.Result;
            var workspace = Workspace.CreateConfigurationWorkspace(WorkspaceConfiguration, parsedModule, preludeModule);
            return await TypeCheckWorkspaceAsync(workspace);
        }

        /// <remarks>
        /// TODO: consider reusing a single prelude manager for all configuration conversion tasks, 
        ///       throughout the whole BuildXL execution, instead of recreating it every time in this method.
        ///
        /// The reason why this is currently not done that way is that reusing <see cref="SourceFile"/> objects
        /// across different type checking sessions can cause problems, because of the counters maintained by the
        /// type checker (<see cref="TypeScript.Net.TypeChecking.Checker.GetCurrentNodeId()"/>).
        /// </remarks>
        private async Task<Possible<ParsedModule>> GetPreludeParsedModule(AbsolutePath configPath)
        {
            var preludeManager = new PreludeManager(
                Engine,
                Context.PathTable,
                parser: path => TryParseAsync(path, path, ParsingOptions),
                maxParseConcurrency: Configuration.FrontEnd.MaxFrontEndConcurrency(),
                useOfficeBackCompatHacks: Configuration.FrontEnd.UseOfficeBackCompatPreludeHacks());

            using (FrontEndStatistics.PreludeProcessing.Start($"For {Kind} '{configPath.ToString(Context.PathTable)}'"))
            {
                return await preludeManager.GetOrCreateParsedPreludeModuleAsync();
            }
        }

        private async Task<Workspace> TypeCheckWorkspaceAsync(Workspace workspace)
        {
            var frontEndStatistics = new NullFrontEndStatistics(); // don't pollute global statistics with this
            var semanticWorkspaceProvider = new SemanticWorkspaceProvider(frontEndStatistics, WorkspaceConfiguration);
            workspace = await semanticWorkspaceProvider.ComputeSemanticWorkspaceAsync(Context.PathTable, workspace);

            if (workspace.GetSemanticModel().GetAllSemanticDiagnostics().Any())
            {
                return workspace.WithExtraFailures(new[] { new Failure<string>("Failed to type check configuration") });
            }

            return workspace;
        }

        private void ReportErrorDiagnostics(IEnumerable<TypeScript.Net.Diagnostics.Diagnostic> errors)
        {
            // Configuration processing follows slightly different logic regarding error reporting and validation.

            // If the parsing fails, we need to emit special error message and haven't even try to analyze the file
            // If the parsing succeeded we need to validate the structure first and if the structure is incorrect
            // (like something except 'config' call) then we need to emit another special error.
            // And only if the file looks good we need to run all other validation techniques.
            if (errors?.Any() == true)
            {
                // Need to emit special error (extracting just a first message to avoid too long messages on the screen)
                var firstDiagnostic = errors.FirstOrDefault(e => e.File != null);
                var lineAndCol = firstDiagnostic.GetLineAndColumn(firstDiagnostic.File);
                var location = new Location()
                {
                    File = firstDiagnostic.File.FileName,
                    Line = lineAndCol.Line,
                };

                ReportConfigParsingFailed(firstDiagnostic.MessageText.ToString(), location);
            }
        }

        private void ValidateConfigFile(ISourceFile sourceFile)
        {
            switch (Kind)
            {
                case ConfigurationKind.PrimaryConfig:
                    Linter.Value.AnalyzeRootConfigurationFile(sourceFile, Logger, Context.LoggingContext, Context.PathTable);
                    break;
                case ConfigurationKind.ModuleConfig:
                    Linter.Value.AnalyzePackageConfigurationFile(sourceFile, Logger, Context.LoggingContext, Context.PathTable);
                    break;
                default:
                    throw Contract.AssertFailure(UnimplementedOperationForConfigKindErrorMessage);
            }
        }

        private IReadOnlyCollection<FileModuleLiteral> ConvertWorkspaceInParallel(Workspace workspace, AbsolutePath configPath)
        {
            var package = CreateDummyPackageFromPath(configPath);
            var parserContext = CreateParserContext(resolver: null, package: package, origin: null);

            // Need to use ConfigurationModule and not a set of source specs.
            // We convert configuration which is not a source specs.
            Contract.Assert(workspace.ConfigurationModule != null);
            var specs = workspace.ConfigurationModule.Specs.ToList();

            return ParallelAlgorithms.ParallelSelect(
                specs,
                kvp => ConvertAndRegisterSourceFile(parserContext, workspace, sourceFile: kvp.Value, path: kvp.Key, isConfig: kvp.Key == configPath),
                DegreeOfParallelism,
                Context.CancellationToken);
        }

        private FileModuleLiteral ConvertAndRegisterSourceFile(RuntimeModelContext runtimeModelContext, Workspace workspace, ISourceFile sourceFile, AbsolutePath path, bool isConfig)
        {
            var moduleLiteral = ModuleLiteral.CreateFileModule(path, FrontEndHost.ModuleRegistry, runtimeModelContext.Package, sourceFile.LineMap);

            var conversionContext = new AstConversionContext(runtimeModelContext, path, sourceFile, moduleLiteral);
            var converter = AstConverter.Create(Context.QualifierTable, conversionContext, ConversionConfiguration, workspace);

            Script.SourceFile convertedSourceFile = null;
            if (!isConfig)
            {
                convertedSourceFile = converter.ConvertSourceFile().SourceFile;
            }
            else if (Kind == ConfigurationKind.PrimaryConfig)
            {
                converter.ConvertConfiguration();
            }
            else if (Kind == ConfigurationKind.ModuleConfig)
            {
                converter.ConvertPackageConfiguration();
            }
            else
            {
                throw Contract.AssertFailure(UnimplementedOperationForConfigKindErrorMessage);
            }

            runtimeModelContext.Package.AddParsedProject(path);

            if (!Logger.HasErrors)
            {
                RegisterSuccessfullyParsedModule(convertedSourceFile, moduleLiteral, runtimeModelContext.Package);
            }

            return moduleLiteral;
        }

        private SymbolAtom GetConfigKeyword(ISourceFile sourceFile)
        {
            string configKeywordString = sourceFile.Statements[0].TryGetFunctionNameInCallExpression();
            Contract.Assert(configKeywordString != null, I($"Configuration validation for '{Kind}' should have caught that the first statement is not a call expression"));

            switch (Kind)
            {
                case ConfigurationKind.PrimaryConfig:
                    Contract.Assert(
                        configKeywordString == Script.Constants.Names.ConfigurationFunctionCall,
                        I($"Configuration validation for '{Kind}' should have caught that a wrong configuration keyword was used ('{configKeywordString}' instead of '{Script.Constants.Names.ConfigurationFunctionCall}')"));
                    return SymbolAtom.Create(Context.StringTable, configKeywordString);

                case ConfigurationKind.ModuleConfig:
                    Contract.Assert(
                        configKeywordString == Script.Constants.Names.ModuleConfigurationFunctionCall || configKeywordString == Script.Constants.Names.LegacyModuleConfigurationFunctionCall,
                        I($"Configuration validation for '{Kind}' should have caught that a wrong configuration keyword was used ('{configKeywordString}' instead of either '{Script.Constants.Names.ModuleConfigurationFunctionCall}' or '{Script.Constants.Names.LegacyModuleConfigurationFunctionCall}')"));
                    return SymbolAtom.Create(Context.StringTable, configKeywordString);
                default:
                    throw Contract.AssertFailure(UnimplementedOperationForConfigKindErrorMessage);
            }
        }

        private void ReportConfigParsingFailed(IEnumerable<string> errors)
        {
            foreach (var error in errors)
            {
                ReportConfigParsingFailed(error);
            }
        }

        private void ReportConfigParsingFailed(string errorMessage, Location location = default(Location))
        {
            switch (Kind)
            {
                case ConfigurationKind.PrimaryConfig:
                    Logger.ReportConfigurationParsingFailed(Context.LoggingContext, location, errorMessage);
                    return;
                case ConfigurationKind.ModuleConfig:
                    Logger.ReportPackageConfigurationParsingFailed(Context.LoggingContext, location, errorMessage);
                    return;
                default:
                    throw Contract.AssertFailure(UnimplementedOperationForConfigKindErrorMessage);
            }
        }
    }
}
