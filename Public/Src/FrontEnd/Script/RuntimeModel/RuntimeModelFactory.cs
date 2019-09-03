// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Incrementality;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using Diagnostic = TypeScript.Net.Diagnostics.Diagnostic;
using Expression = BuildXL.FrontEnd.Script.Expressions.Expression;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Top-level type responsible for translating parse AST into evaluation AST.
    /// </summary>
    public sealed class RuntimeModelFactory
    {
        private readonly AstConversionConfiguration m_conversionConfiguration;
        private readonly IFrontEndStatistics m_statistics;
        private readonly DiagnosticAnalyzer m_linter;
        // Can be null for parsing an expression.
        private readonly Workspace m_workspace;

        /// <nodoc/>
        public RuntimeModelFactory(
            Logger logger,
            LoggingContext loggingContext,
            IFrontEndStatistics statistics,
            AstConversionConfiguration conversionConfiguration,
            [CanBeNull]Workspace workspace)
            : this(
                statistics,
                conversionConfiguration,
                DiagnosticAnalyzer.Create(
                    logger,
                    loggingContext,
                    new HashSet<string>(conversionConfiguration.PolicyRules),
                    conversionConfiguration.DisableLanguagePolicies),
                workspace)
        {
        }

        /// <nodoc/>
        public RuntimeModelFactory(
            IFrontEndStatistics statistics,
            AstConversionConfiguration conversionConfiguration,
            DiagnosticAnalyzer linter,
            Workspace workspace)
        {
            Contract.Requires(statistics != null);
            Contract.Requires(conversionConfiguration != null);

            m_statistics = statistics;
            m_conversionConfiguration = conversionConfiguration;
            m_linter = linter;
            m_workspace = workspace;
        }

        /// <nodoc />
        public Task<SourceFileParseResult> ConvertSourceFileAsync(RuntimeModelContext runtimeModelContext, ISourceFile sourceFile)
        {
            Contract.Requires(runtimeModelContext != null);
            Contract.Requires(sourceFile != null);
            Contract.Assert(m_workspace != null);

            var path = AbsolutePath.Create(runtimeModelContext.PathTable, sourceFile.FileName);
            return Task.FromResult(ValidateAndConvert(runtimeModelContext, path, sourceFile));
        }

        private SourceFileParseResult ValidateAndConvert(RuntimeModelContext runtimeModelContext, AbsolutePath path, ISourceFile parsedSourceFile)
        {
            // AddConfigurationDeclaration
            runtimeModelContext.CancellationToken.ThrowIfCancellationRequested();

            ISourceFile fileForAnalysis = parsedSourceFile;

            if (!m_conversionConfiguration.UnsafeOptions.DisableAnalysis)
            {
                // Validate
                fileForAnalysis = ReportDiagnosticsAndValidateSourceFile(runtimeModelContext, path, parsedSourceFile);
            }

            // Convert
            return ConvertSourceFile(runtimeModelContext, path, fileForAnalysis);
        }

        private SourceFileParseResult ConvertSourceFile(RuntimeModelContext runtimeModelContext, AbsolutePath path, ISourceFile sourceFile)
        {
            runtimeModelContext.CancellationToken.ThrowIfCancellationRequested();

            // This means that if there is any parse or binding errors conversion won't happen.
            if (runtimeModelContext.Logger.HasErrors)
            {
                return new SourceFileParseResult(runtimeModelContext.Logger.ErrorCount);
            }

            string specPath = sourceFile.Path.AbsolutePath;

            // If the serialized AST is available for this file, retrieve it and return instead of converting it
            if (sourceFile.IsPublicFacade)
            {
                var ast = ByteContent.Create(sourceFile.SerializedAst.Item1, sourceFile.SerializedAst.Item2);
                using (m_statistics.SpecAstDeserialization.Start(specPath))
                {
                    return DeserializeAst(runtimeModelContext, ast);
                }
            }

            Contract.Assert(!sourceFile.IsPublicFacade, "We are about to do AST conversion, so the corresponding spec file can't be a public facade, we need the real thing");

            var converter = CreateAstConverter(sourceFile, runtimeModelContext, path, m_conversionConfiguration, m_workspace);

            SourceFileParseResult result;
            using (m_statistics.SpecAstConversion.Start(specPath))
            {
                result = converter.ConvertSourceFile();
            }

            if (runtimeModelContext.Logger.HasErrors)
            {
                // Skip serialization step if the error has occurred.
                return result;
            }

            // At this point we have the computed AST, so we are in a position to generate the public surface of the file (if possible)
            // and serialize the AST for future reuse.
            var semanticModel = m_workspace.GetSemanticModel();

            Contract.Assert(semanticModel != null);

            // Observe that here instead of using FrontEndHost.CanUseSpecPublicFacadeAndAst (that we checked for retrieving) we only require
            // that the associated flag is on. This is because, even though a partial reuse may not have happened,
            // saving is always a safe operation and the saved results may be available for future builds
            if (runtimeModelContext.FrontEndHost.FrontEndConfiguration.UseSpecPublicFacadeAndAstWhenAvailable())
            {
                FileContent publicFacadeContent = CreatePublicFacadeContent(sourceFile, semanticModel);
#pragma warning disable 4014
                ScheduleSavePublicFacadeAndAstAsync(runtimeModelContext, path, sourceFile.Path.AbsolutePath, publicFacadeContent, result).ContinueWith(
                    t =>
                    {
                        runtimeModelContext.Logger.ReportFailedToPersistPublicFacadeOrEvaluationAst(
                            runtimeModelContext.LoggingContext,
#pragma warning disable SA1129 // Do not use default value type constructor
                            new Location(),
#pragma warning restore SA1129 // Do not use default value type constructor
                            t.Exception.ToString());
                    }, TaskContinuationOptions.OnlyOnFaulted);
#pragma warning restore 4014
            }

            return result;
        }

        private Task ScheduleSavePublicFacadeAndAstAsync(
            RuntimeModelContext runtimeModelContext,
            AbsolutePath path,
            string specPath,
            FileContent publicFacadeContent,
            SourceFileParseResult result)
        {
            Task savePublicFacadeTask = null;

            // Serializing public facade if available
            if (publicFacadeContent.IsValid)
            {
                m_statistics.PublicFacadeSerializationBlobSize.AddAtomic(publicFacadeContent.Length);
                using (m_statistics.PublicFacadeSaves.Start(specPath))
                {
                    savePublicFacadeTask = runtimeModelContext.FrontEndHost.FrontEndArtifactManager.SavePublicFacadeAsync(path, publicFacadeContent);
                }
            }
            else
            {
                savePublicFacadeTask = Task.FromResult<object>(null);
            }

            // Serializing AST
            var serializeAstTask = SerializeAndSaveAst(result, runtimeModelContext, specPath, path);

            return Task.WhenAll(savePublicFacadeTask, serializeAstTask);
        }

        private FileContent CreatePublicFacadeContent(ISourceFile sourceFile, ISemanticModel semanticModel)
        {
            var specPath = sourceFile.Path.AbsolutePath;
            using (var sw = m_statistics.PublicFacadeComputation.Start(specPath))
            {
                using (var writer = new ScriptWriter())
                {
                    var printer = new PublicSurfacePrinter(writer, semanticModel);
                    if (!printer.TryPrintPublicSurface(sourceFile, out var publicFacadeContent))
                    {
                        m_statistics.PublicFacadeGenerationFailures.Increment(sw.Elapsed);
                    }

                    return publicFacadeContent;
                }
            }
        }

        private async Task SerializeAndSaveAst(SourceFileParseResult result, RuntimeModelContext runtimeModelContext, string specPath, AbsolutePath path)
        {
            // Pooled stream can be released only when the blob is saved to disk
            using (var pooledStream = BuildXL.Utilities.Pools.MemoryStreamPool.GetInstance())
            {
                byte[] serializedAst;
                using (m_statistics.SpecAstSerialization.Start(specPath))
                {
                    var stream = pooledStream.Instance;

                    // TODO: change to a regular BuildXLWriter when we start serializing the qualifier table
                    using (var writer = new QualifierTableAgnosticWriter(
                        runtimeModelContext.QualifierTable,
                        debug: false,
                        stream: stream,
                        leaveOpen: true,
                        logStats: false))
                    {
                        result.Serialize(writer);
                        writer.Flush();

                        serializedAst = stream.GetBuffer();
                    }
                }

                // Saving AST
                using (m_statistics.AstSerializationSaves.Start(specPath))
                {
                    m_statistics.AstSerializationBlobSize.AddAtomic(pooledStream.Instance.Position);
                    await runtimeModelContext.FrontEndHost.FrontEndArtifactManager.SaveAstAsync(path, ByteContent.Create(serializedAst, pooledStream.Instance.Position));
                }
            }
        }

        private static SourceFileParseResult DeserializeAst(RuntimeModelContext runtimeModelContext, ByteContent ast)
        {
            var stream = new MemoryStream(ast.Content, 0, ast.Length);

            var moduleRegistry = (ModuleRegistry)runtimeModelContext.FrontEndHost.ModuleRegistry;

            // TODO: change to a regular BuildXLReader when we start serializing the qualifier table
            using (var reader = new QualifierTableAgnosticReader(
                runtimeModelContext.QualifierTable,
                stream: stream,
                debug: false,
                leaveOpen: false))
            {
                var deserializedResult = SourceFileParseResult.Read(
                    reader,
                    moduleRegistry.GlobalLiteral,
                    moduleRegistry,
                    runtimeModelContext.PathTable);

                return deserializedResult;
            }
        }

        private static IAstConverter CreateAstConverter(ISourceFile sourceFile, FileModuleLiteral module,
            RuntimeModelContext runtimeModelContext, AbsolutePath specPath, AstConversionConfiguration conversionConfiguration, Workspace workspace)
        {
            var conversionContext = new AstConversionContext(runtimeModelContext, specPath, sourceFile, module);

            return AstConverter.Create(runtimeModelContext.QualifierTable, conversionContext, conversionConfiguration, workspace);
        }

        private IAstConverter CreateAstConverter(ISourceFile sourceFile, RuntimeModelContext runtimeModelContext, AbsolutePath path, AstConversionConfiguration conversionConfiguration, Workspace workspace)
        {
            var module = ModuleLiteral.CreateFileModule(path, runtimeModelContext.FrontEndHost.ModuleRegistry, runtimeModelContext.Package, sourceFile.LineMap);
            return CreateAstConverter(sourceFile, module, runtimeModelContext, path, conversionConfiguration, workspace);
        }

        /// <nodoc />
        public Expression ParseExpression(RuntimeModelContext runtimeModelContext, AbsolutePath path, string expression)
        {
            Contract.Requires(runtimeModelContext != null);
            Contract.Requires(expression != null);

            return ParseExpression(runtimeModelContext, path, expression, localScope: FunctionScope.Empty(), useSemanticNameResolution: true);
        }

        /// <nodoc/>
        public Expression ParseExpression(RuntimeModelContext runtimeModelContext, AbsolutePath path, string spec, FunctionScope localScope, bool useSemanticNameResolution)
        {
            Contract.Requires(runtimeModelContext != null);
            Contract.Requires(spec != null);

            var parser = new TypeScript.Net.Parsing.Parser();

            // Wrap expression in a function call to make the parser happy.
            // Currently an object literal '{...};' is not a valid statement.
            var sourceFile = parser.ParseSourceFileContent(
                path.ToString(runtimeModelContext.PathTable),
                @"function createExpression(a: any) {}
createExpression(" + spec + ");");

            if (sourceFile.ParseDiagnostics.Count != 0)
            {
                ReportParseDiagnosticsIfNeeded(runtimeModelContext, sourceFile, path);
                return null;
            }

            var workspaceConfiguration = WorkspaceConfiguration.CreateForTesting();
            var workspace = new Workspace(
                provider: null,
                workspaceConfiguration: workspaceConfiguration,
                modules: new[] {CreateModuleFor(runtimeModelContext, sourceFile)},
                failures: Enumerable.Empty<Failure>(),
                preludeModule: null,
                configurationModule: null);

            workspace = SemanticWorkspaceProvider.ComputeSemanticWorkspace(runtimeModelContext.PathTable, workspace, workspaceConfiguration).GetAwaiter().GetResult();

            // Because we just created source file to parse, we know exactly what the AST is there.
            // This information helped to simplify analysis logic and migrate to semantic-base name resolution.
            var invocation = sourceFile.Statements[1].Cast<IExpressionStatement>().Expression.Cast<ICallExpression>();

            // Only for expressions, full names should be preserved.
            // Only for expressions, no checks for definition before use to avoid contract assertion in location computation.
            m_conversionConfiguration.UnsafeOptions.DisableDeclarationBeforeUseCheck = true;
            var converter = CreateAstConverter(sourceFile, runtimeModelContext, path, m_conversionConfiguration, workspace: workspace);

            return converter.ConvertExpression(invocation, localScope, useSemanticNameResolution);
        }

        private ISourceFile ReportDiagnosticsAndValidateSourceFile(RuntimeModelContext runtimeModelContext, AbsolutePath path,
            ISourceFile result)
        {
            ReportParseDiagnosticsIfNeeded(runtimeModelContext, result, path);

            return ValidateSourceFile(path, result, runtimeModelContext, runtimeModelContext.LoggingContext);
        }

        private ISourceFile ValidateSourceFile(AbsolutePath path, ISourceFile sourceFile, RuntimeModelContext runtimeModelContext, LoggingContext loggingContext)
        {
            var sw = Stopwatch.StartNew();

            // Should not analyze file with parser errors, but it is ok to analyze with binding errors.
            if (!sourceFile.HasDiagnostics())
            {
                m_linter.AnalyzeSpecFile(sourceFile, runtimeModelContext.Logger, loggingContext, runtimeModelContext.PathTable, m_workspace);
            }

            m_statistics.AnalysisCompleted(path, sw.Elapsed);

            return sourceFile;
        }

        private static void ReportParseDiagnosticsIfNeeded(RuntimeModelContext runtimeModelContext, ISourceFile parsedSourceFile, AbsolutePath path)
        {
            foreach (var diagnostic in parsedSourceFile.ParseDiagnostics.AsStructEnumerable())
            {
                var location = GetLocation(diagnostic, runtimeModelContext, parsedSourceFile, path);
                runtimeModelContext.Logger.ReportTypeScriptSyntaxError(runtimeModelContext.LoggingContext, location, diagnostic.MessageText.ToString());
            }

            foreach (var diagnostic in parsedSourceFile.BindDiagnostics)
            {
                var location = GetLocation(diagnostic, runtimeModelContext, parsedSourceFile, path);
                runtimeModelContext.Logger.ReportTypeScriptBindingError(runtimeModelContext.LoggingContext, location, diagnostic.MessageText.ToString());
            }
        }

        private static Location GetLocation(Diagnostic diagnostic, RuntimeModelContext runtimeModelContext, ISourceFile parsedSourceFile, AbsolutePath path)
        {
            var lineAndColumn = diagnostic.GetLineAndColumn(parsedSourceFile);
            var location = new Location
            {
                Line = lineAndColumn.Line,
                Position = lineAndColumn.Character,
                File = path.ToString(runtimeModelContext.PathTable),
            };

            return location;
        }

        private static ParsedModule CreateModuleFor(RuntimeModelContext context, ISourceFile sourceFile)
        {
            const string ModuleName = "ModuleWith1File";
            var specPath = sourceFile.GetAbsolutePath(context.PathTable);

            var moduleRootDirectory = specPath.GetParent(context.PathTable);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                new ModuleDescriptor(
                    id: ModuleId.Create(context.StringTable, ModuleName),
                    name: ModuleName,
                    displayName: ModuleName,
                    version: "1.0.0", 
                    resolverKind: KnownResolverKind.SourceResolverKind, 
                    resolverName: "DScriptExpression"), 
                moduleRootDirectory: moduleRootDirectory,
                moduleConfigFile: moduleRootDirectory.Combine(context.PathTable, "package.config.dsc"),
                specs: new [] {specPath},
                allowedModuleDependencies: null,
                cyclicalFriendModules: null
                );

            return new ParsedModule(moduleDefinition, new Dictionary<AbsolutePath, ISourceFile>() {[specPath] = sourceFile});
        }
    }
}
