// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Expressions.CompositeExpressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Qualifier;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using BinaryExpression = BuildXL.FrontEnd.Script.Expressions.BinaryExpression;
using CaseClause = BuildXL.FrontEnd.Script.Statements.CaseClause;
using ConditionalExpression = BuildXL.FrontEnd.Script.Expressions.ConditionalExpression;
using DefaultClause = BuildXL.FrontEnd.Script.Statements.DefaultClause;
using EnumDeclaration = BuildXL.FrontEnd.Script.Declarations.EnumDeclaration;
using ExportDeclaration = BuildXL.FrontEnd.Script.Declarations.ExportDeclaration;
using Expression = BuildXL.FrontEnd.Script.Expressions.Expression;
using ExpressionStatement = BuildXL.FrontEnd.Script.Statements.ExpressionStatement;
using ForOfStatement = BuildXL.FrontEnd.Script.Statements.ForOfStatement;
using ForStatement = BuildXL.FrontEnd.Script.Statements.ForStatement;
using FunctionDeclaration = BuildXL.FrontEnd.Script.Declarations.FunctionDeclaration;
using IfStatement = BuildXL.FrontEnd.Script.Statements.IfStatement;
using ImportDeclaration = BuildXL.FrontEnd.Script.Declarations.ImportDeclaration;
using InterfaceDeclaration = BuildXL.FrontEnd.Script.Declarations.InterfaceDeclaration;
using ISymbol = TypeScript.Net.Types.ISymbol;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using ModuleDeclaration = BuildXL.FrontEnd.Script.Declarations.ModuleDeclaration;
using NamespaceImport = BuildXL.FrontEnd.Script.Declarations.NamespaceImport;
using ObjectType = BuildXL.FrontEnd.Script.Types.ObjectType;
using PropertySignature = BuildXL.FrontEnd.Script.Types.PropertySignature;
using ReturnStatement = BuildXL.FrontEnd.Script.Statements.ReturnStatement;
using Signature = BuildXL.FrontEnd.Script.Types.Signature;
using Statement = BuildXL.FrontEnd.Script.Statements.Statement;
using SwitchExpression = BuildXL.FrontEnd.Script.Expressions.SwitchExpression;
using SwitchExpressionClause = BuildXL.FrontEnd.Script.Expressions.SwitchExpressionClause;
using SwitchStatement = BuildXL.FrontEnd.Script.Statements.SwitchStatement;
using TupleType = BuildXL.FrontEnd.Script.Types.TupleType;
using Type = BuildXL.FrontEnd.Script.Types.Type;
using TypeAliasDeclaration = BuildXL.FrontEnd.Script.Declarations.TypeAliasDeclaration;
using TypeParameter = BuildXL.FrontEnd.Script.Types.TypeParameter;
using UnionType = BuildXL.FrontEnd.Script.Types.UnionType;
using WhileStatement = BuildXL.FrontEnd.Script.Statements.WhileStatement;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Converter that translates syntax AST into evaluation AST.
    ///
    /// Note on error handling: null is returned when conversion fails for a given node. Consumers are responsible for dealing with null.
    /// Errors are reported at the same time nulls are returned via ParserErrors.reportError. When an error is reported, conversion is eventually aborted,
    /// but returning null allows conversion to continue nevertheless and report as many errors as possible.
    /// It is not the responsibility of callers report errors. Errors are reported as soon as they are detected by the callee.
    /// All errors here should be errors that can only be detected at evaluation time. Purely syntactic errors should be handled by lint rules, that ran before reaching this stage.
    /// The only temporary exception are non-implemented (yet) syntactic constructs, which usually fall in a general catch-all and reported. These should eventually go away.
    /// </summary>
    internal sealed class AstConverter : IAstConverter
    {
        private static readonly Dictionary<TypeScript.Net.Types.SyntaxKind, UnaryOperator> s_syntaxKindToPrefixUnaryOperatorMapping = CreateUnaryOperatorMapping();
        private static readonly Dictionary<TypeScript.Net.Types.SyntaxKind, BinaryOperator> s_syntaxKindToBinaryOperatorMapping = CreateBinaryOperatorMapping();
        private static readonly Dictionary<TypeScript.Net.Types.SyntaxKind, AssignmentOperator> s_syntaxKindToAssignmentOperatorMapping = CreateAssignmentOperatorMapping();
        private static readonly Dictionary<(IncrementDecrementOperator, TypeScript.Net.Types.SyntaxKind), IncrementDecrementOperator> s_syntaxKindToIncrementDecrementOperator = CreateIncrementDecrementOperatorMapping();

        private readonly V2QualifiersConverter m_conversionHelper;
        private readonly AstConversionContext m_conversionContext;
        private readonly AstConversionConfiguration m_conversionConfiguration;

        // Can be null for expression parsing
        private readonly ISemanticModel m_semanticModel;

        // Can be null if there is no designated prelude
        private readonly ParsedModule m_prelude;

        private readonly Binder m_binder;
        private readonly InterpolationConverter m_interpolationConverter;

        [NotNull]
        private readonly Workspace m_workspace;

        // Set of properties that simplify conversion process
        private RuntimeModelContext RuntimeModelContext => m_conversionContext.RuntimeModelContext;

        // Module that represents current file to be converted
        private FileModuleLiteral CurrentFileModule => m_conversionContext.CurrentFileModule;

        private AbsolutePath CurrentSpecPath => m_conversionContext.CurrentSpecPath;

        private ISourceFile CurrentSourceFile => m_conversionContext.CurrentSourceFile;

        private StringTable StringTable => m_conversionContext.RuntimeModelContext.StringTable;

        private BuildXL.Utilities.SymbolTable SymbolTable => m_conversionContext.RuntimeModelContext.SymbolTable;

        private AstConverter(QualifierTable qualifierTable, AstConversionContext conversionContext, AstConversionConfiguration conversionConfiguration, [CanBeNull]Workspace workspace)
        {
            Contract.Requires(conversionContext != null);
            Contract.Requires(conversionConfiguration != null);
            Contract.Requires(workspace != null);

            m_workspace = workspace;
            m_semanticModel = m_workspace.GetSemanticModel();
            Contract.Assert(m_semanticModel != null);

            m_prelude = m_workspace.PreludeModule;

            m_conversionHelper = new V2QualifiersConverter(qualifierTable, m_semanticModel);

            m_conversionContext = conversionContext;
            m_conversionConfiguration = conversionConfiguration;

            m_binder = Binder.Create(conversionContext.RuntimeModelContext);
            m_interpolationConverter = new InterpolationConverter(this, m_conversionContext);
        }

        /// <summary>
        /// Factory method that creates an AstConverter.
        /// </summary>
        public static IAstConverter Create(QualifierTable qualifierTable, AstConversionContext conversionContext, AstConversionConfiguration conversionConfiguration, Workspace workspace = null)
        {
            return new AstConverterWithExceptionWrappingDecorator(conversionContext, new AstConverter(qualifierTable, conversionContext, conversionConfiguration, workspace ?? conversionContext.RuntimeModelContext.FrontEndHost.Workspace as Workspace));
        }

        private TypeOrNamespaceModuleLiteral CreateTypeModule(ModuleLiteral module, SymbolAtom name, in UniversalLocation location)
        {
            var fullName = CreateFullName(module, new List<SymbolAtom> { name });

            FileModuleLiteral owningFileModuleLiteral = module.CurrentFileModule;
            Contract.Assume(owningFileModuleLiteral != null);

            owningFileModuleLiteral.AddType(fullName, location, qualifierSpaceId: null, module: out TypeOrNamespaceModuleLiteral newModule);

            return newModule;
        }

        private TypeOrNamespaceModuleLiteral CreateNamespaceModule(ModuleLiteral module, List<SymbolAtom> names, in UniversalLocation location, QualifierSpaceId qualifierSpaceId)
        {
            var fullName = CreateFullName(module, names);

            FileModuleLiteral owningFileModuleLiteral = module.CurrentFileModule;
            Contract.Assume(owningFileModuleLiteral != null);

            owningFileModuleLiteral.AddNamespace(fullName, location, qualifierSpaceId, out TypeOrNamespaceModuleLiteral newModule);

            return newModule;
        }

        private FullSymbol CreateFullName(ModuleLiteral module, List<SymbolAtom> names)
        {
            PartialSymbol partialName = PartialSymbol.Create(names.ToArray());

            // Full name should be prepended with namespace name only when the module we're dealing with
            // is not a file.
            // If the module is a top most namespace (_$ is used for its name), we skip that name so it doesn't
            // leak into namespace names
            var fullName = module.IsFileModule || module.Name.GetName(SymbolTable) == m_conversionContext.RuntimeRootNamespaceSymbol ?
                FullSymbol.Invalid :
                module.Name;

            fullName = fullName.Combine(RuntimeModelContext.SymbolTable, partialName);
            return fullName;
        }

        /// <inheritdoc />
        public ConfigurationDeclaration ConvertConfiguration()
        {
            var configurationLiteral = ConfigurationConverter.ExtractConfigurationLiteral(CurrentSourceFile);

            var context = new ConversionContext(EmptyFunctionScope(), QualifierSpaceId.Invalid);
            var convertedObjectLiteral = ConvertObjectLiteral(configurationLiteral, context);

            if (convertedObjectLiteral == null)
            {
                // Errors have been logged.
                return null;
            }

            // At this point we know that the configuration is already validated, so we can safely retrieve the used configuration keyword
            // We do this since the configuration keyword is not fixed and we currently support two of them
            // TODO: remove this logic when we stop supporting the legacy configuration keyword
            var configurationKeyword = CurrentSourceFile.Statements[0].GetConfigurationKeywordFromConfigurationDeclaration();

            var keyword = SymbolAtom.Create(StringTable, configurationKeyword.Text);
            var location = Location(configurationKeyword);
            ConfigurationDeclaration result = new ConfigurationDeclaration(keyword, convertedObjectLiteral, location);

            // Need to register configuration invocation in the module. This is important!
            m_binder.AddExportBinding(CurrentFileModule, keyword, result.ConfigExpression, location);

            return result;
        }

        private bool IsEmpty(QualifierSpaceId qualifierSpaceId)
        {
            return qualifierSpaceId == RuntimeModelContext.QualifierTable.EmptyQualifierSpaceId;
        }

        private bool IsInvalidOrEmpty(QualifierSpaceId qualifierSpaceId)
        {
            return !qualifierSpaceId.IsValid || IsEmpty(qualifierSpaceId);
        }

        private QualifierSpaceId ComputeQualifierSpaceId(
            QualifierSpaceId semanticQualifierId,
            QualifierSpaceId legacyQualifierId)
        {
            // Current logic responsible for qualifier space id computation is fairly sophisticated.
            // In the meanwhile we need to support mixed mode evaluation:

            // Otherwise legacy logic needs to be used: use legacy qualifier if valid, or package qualifier otherwise.
            if (semanticQualifierId.IsValid)
            {
                return semanticQualifierId;
            }

            // Legacy logic:
            return legacyQualifierId;
        }

        /// <nodoc />
        public SourceFileParseResult ConvertSourceFile()
        {
            // Null statements represent the error nodes.
            // In a semantic name resolution mode, we need to register a top-most scope as a faked namespace.
            // This is required for proper behavior of qualifiers:
            // In V1 world only files were qualified (this means only FileModuleLiteral was 'qualifiable').
            // But in V2 world namespaces are qualifiable as well, because namespace defines a qualifier, but not a file.
            // Root of the file is a special case: there is no explicit namespace so we need to invent one.
            var semanticQualifierId = ExtractSourceQualifierSpace(CurrentSourceFile.Symbol);

            ModuleLiteral rootModule = CreateNamespaceModule(
                CurrentFileModule,
                new List<SymbolAtom> { m_conversionContext.RuntimeRootNamespaceSymbol },
                Location(CurrentSourceFile),
                semanticQualifierId);

            var topLevelNamespace = new NamespaceScope(new List<SymbolAtom>(), CurrentSourceFile.Locals, CurrentSourceFile, RuntimeModelContext.PathTable, StringTable, parent: null);
            var processedSourceFile =
                new SourceFile(
                    CurrentSpecPath,
                    ConvertDeclarationStatements(
                        CurrentSourceFile.Statements,
                        rootModule,
                        topLevelNamespace,
                        qualifierSpaceId: semanticQualifierId));

            if (RuntimeModelContext.Logger.HasErrors)
            {
                return new SourceFileParseResult(RuntimeModelContext.Logger.ErrorCount);
            }

            return new SourceFileParseResult(
                processedSourceFile,
                CurrentFileModule,
                ComputeQualifierSpaceId(semanticQualifierId, QualifierSpaceId.Invalid));
        }

        private IReadOnlyList<Declaration> ConvertDeclarationStatements(NodeArray<IStatement> statements, ModuleLiteral module, NamespaceScope @namespace, QualifierSpaceId qualifierSpaceId)
        {
            return statements.Count > 1 && m_conversionConfiguration.ConvertInParallel
                ? ConvertDeclarationStatementsInParallel(statements, module, @namespace, qualifierSpaceId)
                : ConvertDeclarationStatementsSynchronously(statements, module, @namespace, qualifierSpaceId);
        }

        private IReadOnlyList<Declaration> ConvertDeclarationStatementsSynchronously(NodeArray<IStatement> statements, ModuleLiteral module, NamespaceScope namespaces, QualifierSpaceId qualifierSpaceId)
        {
            return statements.Select(s => ConvertDeclarationStatement(s, module, namespaces, qualifierSpaceId)).Where(s => s != null).ToList();
        }

        private IReadOnlyList<Declaration> ConvertDeclarationStatementsInParallel(NodeArray<IStatement> statements, ModuleLiteral module, NamespaceScope namespaces, QualifierSpaceId qualifierSpaceId)
        {
            var results = new Declaration[statements.Count];
            Parallel.For(
                0,
                statements.Count,
                new ParallelOptions { MaxDegreeOfParallelism = m_conversionConfiguration.DegreeOfParalellism },
                i =>
                {
                    results[i] = ConvertDeclarationStatement(statements[i], module, namespaces, qualifierSpaceId);
                });

            return results.Where(d => d != null).ToList();
        }

        private QualifierSpaceId ExtractSourceQualifierSpaceForV2(INode root)
        {
            return m_conversionHelper.ConvertQualifierType(root);
        }

        /// <nodoc />
        public PackageDeclaration ConvertPackageConfiguration()
        {
            ISourceFile sourceFile = CurrentSourceFile;

            var configurationLiterals = ConfigurationConverter.ExtractPackageConfigurationLiterals(sourceFile);

            var convertedObjectLiterals = ConvertPackageConfigurationLiterals(configurationLiterals);
            if (convertedObjectLiterals == null)
            {
                // Errors were logged
                return null;
            }

            Contract.Assert(sourceFile.Statements.Count > 0, "Package configuration should always have one statement");

            var isLegacyKeyword = sourceFile.Statements[0].IsLegacyPackageConfigurationDeclaration();

            var bindingName = isLegacyKeyword ? m_conversionContext.LegacyPackageKeyword : m_conversionContext.ModuleKeyword;

            // Need to register package invocation in the module
            m_binder.AddExportBinding(
                CurrentFileModule,
                bindingName,
                ArrayLiteral.Create(convertedObjectLiterals, Location(CurrentSourceFile), CurrentFileModule.Path),
                Location(configurationLiterals[0]));

            return new PackageDeclaration(bindingName, convertedObjectLiterals, convertedObjectLiterals[0].Location);
        }

        private Expression[] ConvertPackageConfigurationLiterals(IReadOnlyList<IObjectLiteralExpression> configurationLiterals)
        {
            var convertedObjectLiterals = new Expression[configurationLiterals.Count];

            bool hasErrors = false;
            for (int i = 0; i < configurationLiterals.Count; ++i)
            {
                var context = new ConversionContext(EmptyFunctionScope(), QualifierSpaceId.Invalid);
                convertedObjectLiterals[i] = ConvertObjectLiteral(configurationLiterals[i], context);
                if (convertedObjectLiterals[i] == null)
                {
                    // error should be reported already.
                    hasErrors = true;
                }
            }

            return hasErrors ? null : convertedObjectLiterals;
        }

        /// <inheritdoc />
        public Expression ConvertExpression(ICallExpression node, FunctionScope localScope, bool useSemanticNameResolution)
        {
            if (useSemanticNameResolution)
            {
                var argument = node.Arguments[0];

                if (FullSymbol.TryCreate(SymbolTable, new StringSegment(argument.GetText()), out FullSymbol fullName, out int _) ==
                    FullSymbol.ParseResult.Success)
                {
                    return new FullNameBasedSymbolReference(fullName, Location(node));
                }

                // Given expression is not a valid dotted name. Falling back to an old translation logic.
            }

            // This is tightly coupled solution, but we known that the original expression was wrapped
            // into method application just for a sake of conversion.
            // Unwrapping it here, because semantic-based search returns special full-name identifier.
            var context = new ConversionContext(localScope, QualifierSpaceId.Invalid);
            var applyExpression = (ApplyExpression)ConvertCallExpression(node, context);
            return applyExpression.Arguments[0];
        }

        private FunctionScope EmptyFunctionScope() => EmptyGlobalScope().NewFunctionScope(TypeScript.Net.Types.SymbolTable.Empty);

        private NamespaceScope EmptyGlobalScope() => new NamespaceScope(new List<SymbolAtom>(), TypeScript.Net.Types.SymbolTable.Empty, CurrentSourceFile, RuntimeModelContext.PathTable, StringTable);

        private Expression ConvertPropertyAccessExpression(IPropertyAccessExpression source, ConversionContext context)
        {
            var thisExpression = ConvertExpression(source.Expression, context);

            if (thisExpression == null)
            {
                return null;
            }

            if (!source.Expression.IsImportFile())
            {
                // For semantic-based resolution the following logic is applied:
                // If A.B.c crosses a namespace boundary and the resolved expression
                // resides in a namespace with a different qualifier type, then
                // qualifier type coercion should occur.
                //
                // To do that, first, the source expression should be converted to a 'resolved' symbol reference.
                // Then the original 'this expression' should be wrapped into 'coerce' expression.
                // Normally, the following translation occurred:
                // A.B.c -> SelectorExpression(A.B, c)
                // And in this case:
                // A.B.c -> SelectorExpression(Coerce(currentQualifier, qualifierFor(A.B)), c);
                var resolvedExpression = TryResolvePropertyAccess(source, out IDeclaration resolvedPropertyDeclaration) as LocationBasedSymbolReference;

                if (resolvedExpression != null)
                {
                    var resolvedSymbol = ResolveSymbolAtPositionAndReportWarningIfObsolete(source);

                    // source was converted to a resolved location-based expression, this means that
                    // we should be able to get the symbol of the source once again.
                    // Symbol computation happens twice but it is cached, so this should not be a big deal in terms of performance.
                    Contract.Assert(resolvedSymbol != null, "resolvedSymbol != null");

                    // Additional logic is needed only when 'this expression' is a namespace and the 'source' itself doesn't point to a namespace.
                    // This is intentional and this prevents from nested qualifier type coercion for A.B.C.D.
                    // Every dotted expression has no more than one qualifier type coercion and the coercion happens only when the value from a namespace is used.
                    if (m_semanticModel.IsNamespaceType(source.Expression) && !m_semanticModel.IsNamespaceType(resolvedSymbol))
                    {
                        // We know what the resolved symbol is and that the 'this expression' is a namespace.
                        // Need to compare qualifier types for a call site with a 'target qualifier space'.
                        var resolvedQualifierType = ExtractSourceQualifierSpace(resolvedSymbol);

                        // Need to exclude 'withQualifier' because it doesn't fall into the common pattern.
                        if (context.CurrentQualifierSpaceId != resolvedQualifierType && resolvedExpression.Name != m_conversionContext.WithQualifierKeyword)
                        {
                            var targetSourceFile = resolvedPropertyDeclaration.GetSourceFile();
                            Contract.Assert(targetSourceFile != null);

                            var targetSourceFilePath = targetSourceFile.GetAbsolutePath(RuntimeModelContext.PathTable);
                            thisExpression = new CoerceQualifierTypeExpression(
                                thisExpression,
                                resolvedQualifierType,
                                RuntimeModelContext.FrontEndHost.ShouldUseDefaultsOnCoercion(targetSourceFilePath),
                                Location(source),
                                resolvedPropertyDeclaration.Location(targetSourceFile, targetSourceFilePath, RuntimeModelContext.PathTable));
                        }
                    }

                    return new ResolvedSelectorExpression(thisExpression, resolvedExpression, resolvedExpression.Name, Location(source));
                }
            }

            // Two cases here:
            // 1. Selector is lowercased (X.y), so X.y is ModuleIdExpression
            // 2. Selector is uppercased (X.Y), so X.Y is selector expression where X is id and C is selector

            // Checking for the first case first
            if (IsIdentifier(source.Name.Text))
            {
                return new SelectorExpression(thisExpression, SymbolAtom.Create(StringTable, source.Name.Text),
                    Location(source));
            }

            // Need to apply more complicated logic and adjust "this"
            if (thisExpression is ModuleIdExpression mid)
            {
                mid.SetName(CombineFullName(mid.Name, source.Name.Text));
                return mid;
            }

            if (thisExpression is ModuleSelectorExpression modSel)
            {
                modSel.SetSelector(CombineFullName(modSel.Selector, source.Name.Text));
                return modSel;
            }

            return new ModuleSelectorExpression(thisExpression, CreateFullName(source.Name.Text),
                thisExpression.Location);
        }

        private QualifierSpaceId ExtractSourceQualifierSpace(ISymbol resolvedSymbol)
        {
            // The target file can be part of a V1 or V2 module. Each version has a different way to declare the qualifier space.
            var targetSourceFile = resolvedSymbol.DeclarationList[0].GetSourceFile();

            Contract.Assert(targetSourceFile != null);

            var owningModule =
                m_workspace.GetModuleBySpecFileName(targetSourceFile.GetAbsolutePath(RuntimeModelContext.PathTable));

            if (owningModule.Definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences)
            {
                // V2 case
                // This case is simple: all V2 specs are guaranteed to have a qualifier space defined.
                return ExtractSourceQualifierSpaceForV2(resolvedSymbol.DeclarationList[0]);
            }

            return new QualifierSpaceId(owningModule.Definition.V1QualifierSpaceId);
        }

        /// <summary>
        /// Resolves expression that was used for a property access.
        /// </summary>
        private Expression TryResolvePropertyAccess(IPropertyAccessExpression source, out IDeclaration referencedDeclaration)
        {
            // Now we have two steps process:
            // 1. Resolve what symbol is referenced by the 'source' expression
            // 2. Try to resolve declaration of the referenced symbol.
            var resolvedSymbol = ResolveSymbolAtPositionAndReportWarningIfObsolete(source);

            // The resolved symbol can be null only under very particular scenarios
            if (resolvedSymbol == null)
            {
                ValidateNullSymbolAndReportIfNeeded(source);
                referencedDeclaration = null;
                return null;
            }

            referencedDeclaration = m_semanticModel.GetFirstNotFilteredDeclarationOrDefault(resolvedSymbol);

            // If resolved symbol is a local variable or function argument, then we need to fallback to an old logic.
            // This is critical, because a function application is different from other expression evaluation.
            if ((resolvedSymbol.Flags & SymbolFlags.FunctionScopedVariable) == SymbolFlags.None)
            {
                // We just resolved where this particular 'x.y' references to.
                var nameAtom = SymbolAtom.Create(StringTable, resolvedSymbol.Name);
                return CreateSymbolReferenceExpression(nameAtom, resolvedSymbol, Location(source), referencedDeclaration);
            }

            // So this is a function scoped variable. Falling back to default lookup semantic.
            return null;
        }

        private void ValidateNullSymbolAndReportIfNeeded(IPropertyAccessExpression source)
        {
            // In V1 modules 'qualifier' is still of type 'any', so we need to fall back to legacy lookup semantics
            // TODO: remove when all V1 modules are gone
            if (source.Expression.IsCurrentQualifier())
            {
                return;
            }

            // 'toString()' is the only function application we allow on 'any', so we fall back to legacy lookup as well
            if (source.IsToStringCall())
            {
                return;
            }

            // If the right hand side expression is of type 'any' we want to fail gracefully
            var receiverType = m_semanticModel.GetTypeAtLocation(source.Expression);
            if ((receiverType.Flags & TypeFlags.Any) != TypeFlags.None)
            {
                RuntimeModelContext.Logger.ReportPropertyAccessOnValueWithTypeAny(
                    RuntimeModelContext.LoggingContext,
                    Location(source).AsLoggingLocation(),
                    source.Expression.ToDisplayString(),
                    source.Name.ToDisplayString());

                return;
            }

            // If the left hand side expression is of type 'any' we let that go. If there are no
            // accesses on the result of the whole expression, then that's something we can deal with
            // Otherwise, we'll eventually end up failing (gracefully) under the case above
            var selectorType = m_semanticModel.GetTypeAtLocation(source.Name);
            if ((selectorType.Flags & TypeFlags.Any) != TypeFlags.None)
            {
                return;
            }

            Contract.Assert(false, I($"A resolved symbol should always be available for property access expression '{source.ToDisplayString()}'"));
        }

        /// <summary>
        /// Returns true, if <paramref name="text"/> starts with lowercase.
        /// </summary>
        private static bool IsIdentifier(string text)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(text));

            // Some valid characters for identifiers are not alphanumeric (e.g. _) and therefore they are no lower nor uppercase.
            // So checking for not uppercase, since non-aphanumeric are not valid module names
            return !char.IsUpper(text[0]);
        }

        private FullSymbol CombineFullName(FullSymbol existingSymbol, string selector)
        {
            Contract.Requires(existingSymbol.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(selector));
            Contract.Requires(char.IsUpper(selector[0]));

            return existingSymbol.Combine(SymbolTable, selector);
        }

        private Declaration ConvertDeclarationStatement(IStatement source, ModuleLiteral module, NamespaceScope namespaces, QualifierSpaceId currentQualifierSpaceId)
        {
            if (source.IsConfigurationDeclaration())
            {
                RuntimeModelContext.Logger.ReportConfigurationDeclarationIsOnlyInConfigurationFile(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation());
                return null;
            }

            if (source.IsPackageConfigurationDeclaration())
            {
                RuntimeModelContext.Logger.ReportPackageConfigurationDeclarationIsOnlyInConfigurationFile(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation());
                return null;
            }

            // We don't convert injected declarations (e.g. withQualifier or qualifier declarations)
            if (source.IsInjectedForDScript())
            {
                return null;
            }

            switch (source.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration:
                    return ConvertInterfaceDeclaration(source.Cast<IInterfaceDeclaration>(), module, currentQualifierSpaceId);

                case TypeScript.Net.Types.SyntaxKind.ImportDeclaration:
                    return ConvertImportDeclaration(source.Cast<IImportDeclaration>(), currentQualifierSpaceId);

                case TypeScript.Net.Types.SyntaxKind.ExportDeclaration:
                    return ConvertExportDeclaration(source.Cast<IExportDeclaration>());

                case TypeScript.Net.Types.SyntaxKind.ModuleDeclaration:
                    return ConvertNamespaceDeclaration(source.Cast<IModuleDeclaration>(), module, namespaces, currentQualifierSpaceId);

                case TypeScript.Net.Types.SyntaxKind.VariableStatement:
                    {
                        var variableStatement = source.Cast<IVariableStatement>();

                        // Returning first variable declaration. There is a linter rule that prevents from using more than one
                        // variable in variable initialization list.
                        return ConvertTopLevelVariableDeclarations(module, variableStatement.DeclarationList, variableStatement.Flags, namespaces, currentQualifierSpaceId).FirstOrDefault();
                    }

                case TypeScript.Net.Types.SyntaxKind.FunctionDeclaration:
                    return ConvertFunctionDeclaration(source.Cast<IFunctionDeclaration>(), module, namespaces, currentQualifierSpaceId);

                case TypeScript.Net.Types.SyntaxKind.EnumDeclaration:
                    return ConvertEnumDeclaration(source.Cast<IEnumDeclaration>(), module, currentQualifierSpaceId);

                case TypeScript.Net.Types.SyntaxKind.TypeAliasDeclaration:
                    return ConvertTypeAlias(source.Cast<ITypeAliasDeclaration>(), module, currentQualifierSpaceId);

                // Just skipping empty statements. Null will be filtered out on the upper level. No need to reflect this in value AST.
                // Empty statement is legal and following code will create an empty statement:
                // interface Foo {x: 42};
                // Semicolon at the end of the line will lead to EmptyStatement because TypeScript doesnt require
                // semicolons at the end of interface declarations.
                case TypeScript.Net.Types.SyntaxKind.EmptyStatement:
                    return null;
            }

            string message = I($"Unsupported declaration '{source.Kind}' from expression '{source.GetFormattedText()}'.");

            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation(), message);
            return null;
        }

        private TypeAliasDeclaration ConvertTypeAlias(ITypeAliasDeclaration source, ModuleLiteral module, QualifierSpaceId currentQualifierSpaceId)
        {
            SymbolAtom name = SymbolAtom.Create(StringTable, source.Name.Text);
            var typeParameters = ConvertTypeParameters(source.TypeParameters, currentQualifierSpaceId);
            Type type = ConvertType(source.Type, currentQualifierSpaceId);
            Declaration.DeclarationFlags modifier = ConvertModifiers(source);

            if (type != null)
            {
                // TODO: consider extracting template method!
                CreateTypeModule(module, name, Location(source));
                return new TypeAliasDeclaration(name, typeParameters, type, modifier, Location(source));
            }

            return null;
        }

        private InterfaceDeclaration ConvertInterfaceDeclaration(IInterfaceDeclaration source, ModuleLiteral module, QualifierSpaceId currentQualifierSpaceId)
        {
            SymbolAtom name = SymbolAtom.Create(StringTable, source.Name.Text);

            var typeParameters = ConvertTypeParameters(source.TypeParameters, currentQualifierSpaceId);

            NamedTypeReference[] extendedTypes = source.HeritageClauses.Elements.SelectMany(h => ConvertHeritageClause(h, currentQualifierSpaceId)).ToArray();
            Expression[] decorators = ConvertDecorators(source, currentQualifierSpaceId);

            Signature[] members = source.Members.Select(m => ConvertInterfaceMember(m, currentQualifierSpaceId)).Where(m => m != null).ToArray();
            ObjectType body = new ObjectType(members, Location(source));
            Declaration.DeclarationFlags flags = ConvertModifiers(source);

            CreateTypeModule(module, name, Location(source));

            return new InterfaceDeclaration(name, typeParameters, extendedTypes, decorators, body, flags, Location(source));
        }

        private Signature ConvertInterfaceMember(ITypeElement interfaceMember, QualifierSpaceId currentQualifierSpaceId)
        {
            var property = interfaceMember.As<IPropertySignature>();
            if (property != null)
            {
                SymbolAtom propertyName = SymbolAtom.Create(StringTable, property.Name.Text);
                Type propertyType = ConvertType(property.Type, currentQualifierSpaceId);
                bool isOptional = property.QuestionToken.HasValue;
                Expression[] decorators = ConvertDecorators(property, currentQualifierSpaceId);

                return propertyType != null ? new PropertySignature(propertyName, propertyType, isOptional, decorators, Location(interfaceMember)) : null;
            }

            var method = interfaceMember.As<IMethodSignature>();
            if (method != null)
            {
                SymbolAtom methodName = SymbolAtom.Create(StringTable, method.Name.Text);
                bool isOptional = method.Cast<ITypeElement>().QuestionToken.HasValue;
                Expression[] decorators = ConvertDecorators(method, currentQualifierSpaceId);

                var typeParameters = ConvertTypeParameters(method.TypeParameters, currentQualifierSpaceId);
                Parameter[] parameters = ConvertParameters(method.Parameters, currentQualifierSpaceId);
                Type returnType = ConvertType(method.Type, currentQualifierSpaceId);
                Type type = new FunctionType(typeParameters, parameters, returnType, Location(method));

                return new PropertySignature(methodName, type, isOptional, decorators, Location(interfaceMember));
            }

            string message = I($"Interface member kind '{interfaceMember.Kind}' declared at '{interfaceMember.GetFormattedText()}' is not supported.");
            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(interfaceMember).AsLoggingLocation(), message);
            return null;
        }

        private List<NamedTypeReference> ConvertHeritageClause(IHeritageClause heritageClause, QualifierSpaceId currentQualifierSpaceId)
        {
            var result = new List<NamedTypeReference>(heritageClause.Types.Count);

            foreach (var type in heritageClause.Types)
            {
                // DScript only supports dotted identifiers (PropertyAccessExpression) or simple identifier (Identifier) in heritage clauses
                // This should have been validated by our lint rules
                Contract.Assert(type.Expression is IPropertyAccessExpression || type.Expression is IIdentifier);

                var propertyAccess = type.Expression as IPropertyAccessExpression;
                string text;
                if (propertyAccess == null)
                {
                    var ident = type.Expression.Cast<IIdentifier>();
                    text = ident.Text;
                }
                else
                {
                    text = propertyAccess.Name.Text;
                }

                // TODO: this cannot be the way to do it
                SymbolAtom[] name = text.Split('.').Select(a => SymbolAtom.Create(StringTable, a)).ToArray();

                Type[] typeArguments = type.TypeArguments?.Select(t => ConvertType(t, currentQualifierSpaceId)).Where(t => t != null).ToArray() ?? CollectionUtilities.EmptyArray<Type>();

                result.Add(new NamedTypeReference(name, typeArguments, Location(heritageClause)));
            }

            return result;
        }

        private EnumDeclaration ConvertEnumDeclaration(IEnumDeclaration source, ModuleLiteral module, QualifierSpaceId currentQualifierSpaceId)
        {
            SymbolAtom name = SymbolAtom.Create(StringTable, source.Name.Text);

            UniversalLocation location = Location(source);
            var enumModule = CreateTypeModule(module, name, location);

            EnumMemberDeclaration[] members = ConvertEnumMembers(source, enumModule, currentQualifierSpaceId).ToArray();
            Expression[] decorators = ConvertDecorators(source, currentQualifierSpaceId);
            Declaration.DeclarationFlags modifiers = ConvertModifiers(source);
            return new EnumDeclaration(name, members, decorators, modifiers, Location(source));
        }

        private List<EnumMemberDeclaration> ConvertEnumMembers(IEnumDeclaration source, ModuleLiteral enumModule, QualifierSpaceId currentQualifierSpaceId)
        {
            int? currentValueCandidate = 0;
            var result = new List<EnumMemberDeclaration>(source.Members.Count);

            foreach (var member in source.Members)
            {
                var enumMember = ConvertEnumMember(member, ref currentValueCandidate, currentQualifierSpaceId);

                // Invalid enum members are just skipped and error is reported
                if (enumMember != null && currentValueCandidate.HasValue)
                {
                    // Enum declaration is a module with a bag of numeric constants
                    var enumValue = new EnumValue(enumMember.Name, currentValueCandidate.Value);
                    var location = Location(member);
                    m_binder.AddEnumMember(enumModule, enumMember, enumValue, location);

                    result.Add(enumMember);

                    // TODO:Typechecker already has an ability to give a value for enum value. use that logic instead of our own here.
                    currentValueCandidate++;
                }
                else
                {
                    // Re-initialize the count to re-establish the invariant, but evaluation is doomed to fail in this case anyway
                    currentValueCandidate = 0;
                }
            }

            return result;
        }

        private EnumMemberDeclaration ConvertEnumMember(IEnumMember enumMember, ref int? currentValueCandidate, QualifierSpaceId currentQualifierSpaceId)
        {
            Contract.Assert(currentValueCandidate.HasValue);

            SymbolAtom name = SymbolAtom.Create(StringTable, enumMember.Name.Text);

            LineInfo lineInfo = Location(enumMember);

            if (enumMember.Initializer.HasValue)
            {
                // In V2, the type checker has already done this work
                int? memberValue = m_semanticModel?.TypeChecker.GetConstantValue(enumMember);
                if (memberValue.HasValue)
                {
                    currentValueCandidate = memberValue;
                }
                else
                {
                    // We already checked that only const enums are allowed.
                    // So initializer could be only a constant expression!
                    currentValueCandidate = ConvertExpressionToNumericLiteral(enumMember.Initializer.Value);
                }

                lineInfo = Location(enumMember.Initializer.Value);
            }

            if (currentValueCandidate.HasValue)
            {
                // TODO: currently we have artifical EnumValue type that makes implementation less clear and consistent.
                // Instead of creating that artificial value, additional EnumValueLiteral should be introduced.
                NumberLiteral expression = new NumberLiteral(currentValueCandidate.Value, lineInfo);
                Expression[] decorators = ConvertDecorators(enumMember, currentQualifierSpaceId);
                return new EnumMemberDeclaration(name, expression, Declaration.DeclarationFlags.Export, decorators, Location(enumMember));
            }

            // TODO: error handling needs to be refined here. Many different causes for a enum to be invalid
            // but, current behavior is not the final one. So a more detailed error handling should be added
            // to the final behavior
            RuntimeModelContext.Logger.ReportInvalidEnumMember(RuntimeModelContext.LoggingContext, Location(enumMember).AsLoggingLocation(), enumMember.GetFormattedText());

            return null;
        }

        private int? ConvertExpressionToNumericLiteral(IExpression expression)
        {
            // TODO: we should use this evaluator (or, even better) stuff from the checker, for enums as well.
            var constValue = ConstantEvaluator.EvalConstant(expression);

            if (constValue?.IsOverflow == true)
            {
                RuntimeModelContext.Logger.ReportIntegralConstantIsTooLarge(RuntimeModelContext.LoggingContext, Location(expression).AsLoggingLocation(), expression.GetFormattedText());
                return null;
            }

            return constValue?.Value;
        }

        private Expression[] ConvertDecorators(INode source, QualifierSpaceId currentQualifierSpaceId)
        {
            if (source.Decorators == null || source.Decorators.Length == 0)
            {
                return CollectionUtilities.EmptyArray<Expression>();
            }

            return source.Decorators.Select(d => ConvertExpression(d.Expression, EmptyFunctionScope(), currentQualifierSpaceId)).Where(e => e != null).ToArray();
        }

        private Statement[] ConvertStatements(NodeArray<IStatement> statements, ConversionContext context)
        {
            var convertedStatements = statements.Select(s => ConvertStatement(s, context)).Where(s => s != null).ToList();
            return ConvertedStatement.FlattenStatements(convertedStatements).ToArray();
        }

        [CanBeNull]
        private ConvertedStatement ConvertStatement(IStatement statement, ConversionContext context)
        {
            Contract.Requires(statement != null);

            var lineInfo = Location(statement);

            // Note that we don't consider for-in statements since they are already filtered out by the linter
            switch (statement.Kind)
            {
                // Variable statement is: let x = 42; in the function body.
                case TypeScript.Net.Types.SyntaxKind.VariableStatement:
                    return ConvertVariableStatement(statement.Cast<IVariableStatement>(), context);

                case TypeScript.Net.Types.SyntaxKind.FunctionDeclaration:
                    return ConvertLocalFunctionDeclaration(statement.Cast<IFunctionDeclaration>());

                case TypeScript.Net.Types.SyntaxKind.IfStatement:
                    return ConvertIfStatement(statement.Cast<IIfStatement>(), context);

                case TypeScript.Net.Types.SyntaxKind.ReturnStatement:
                    return ConvertReturnStatement(statement.Cast<IReturnStatement>(), context);

                case TypeScript.Net.Types.SyntaxKind.SwitchStatement:
                    return ConvertSwitchStatement(statement.Cast<ISwitchStatement>(), context);
                case TypeScript.Net.Types.SyntaxKind.BreakStatement:
                    return new BreakStatement(lineInfo);
                case TypeScript.Net.Types.SyntaxKind.ContinueStatement:
                    return new ContinueStatement(lineInfo);

                case TypeScript.Net.Types.SyntaxKind.Block:
                    return ConvertBlock(statement.Cast<IBlock>(), context);

                case TypeScript.Net.Types.SyntaxKind.ForOfStatement:
                    return ConvertForOfStatement(statement.Cast<IForOfStatement>(), context);
                case TypeScript.Net.Types.SyntaxKind.ForStatement:
                    return ConvertForStatement(statement.Cast<IForStatement>(), context);
                case TypeScript.Net.Types.SyntaxKind.WhileStatement:
                    return ConvertWhileStatement(statement.Cast<IWhileStatement>(), context);

                case TypeScript.Net.Types.SyntaxKind.ExpressionStatement:
                    return ConvertExpressionStatement(statement.Cast<IExpressionStatement>(), context);

                // Empty statements are just skipped
                case TypeScript.Net.Types.SyntaxKind.EmptyStatement:
                    return null;
            }

            string message = I($"Statement kind '{statement.Kind}' from expression '{statement.GetFormattedText()}' is not supported.");
            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(statement).AsLoggingLocation(), message);
            return null;
        }

        private static ConvertedStatement ConvertLocalFunctionDeclaration(IFunctionDeclaration cast)
        {
            // Local functions are not supported right now and linter should catch them.
            Contract.Assume(false, "Local functions are not supported, but linter should have already checked it!");

            return null;
        }

        private SwitchStatement ConvertSwitchStatement(ISwitchStatement source, ConversionContext context)
        {
            // Switch statement has just one scope. Each case block doesn't inroduce new scope!
            // Locals in switch block are located in a CaseBlock. But this table is optional
            context.Scope.PushBlockScope(source.CaseBlock.Locals ?? TypeScript.Net.Types.SymbolTable.Empty);

            Expression control = ConvertExpression(source.Expression, context);
            CaseClause[] caseClauses = source.CaseBlock.GetCaseClauses().Select(c => ConvertSwitchClause(c, context)).Where(c => c != null).ToArray();

            // This one is ok to be null
            DefaultClause defaultClause = ConvertDefaultClause(source.CaseBlock.GetDefaultClause(), context);

            context.Scope.PopBlockScope();

            return control != null ? new SwitchStatement(control, caseClauses, defaultClause, Location(source)) : null;
        }

        private DefaultClause ConvertDefaultClause(IDefaultClause defaultClause, ConversionContext context)
        {
            if (defaultClause == null)
            {
                return null;
            }

            Statement[] statements = ConvertStatements(defaultClause.Statements, context);
            return new DefaultClause(statements, Location(defaultClause));
        }

        private CaseClause ConvertSwitchClause(ICaseClause caseClause, ConversionContext context)
        {
            Expression caseExpression = ConvertExpression(caseClause.Expression, context);

            // TODO:ST: validate and prohibit from non const expressions here
            Statement[] statements = ConvertStatements(caseClause.Statements, context);

            return caseExpression != null ? new CaseClause(caseExpression, statements, Location(caseClause)) : null;
        }

        private ForOfStatement ConvertForOfStatement(IForOfStatement source, ConversionContext context)
        {
            context.Scope.PushBlockScope(source.Locals);

            VarStatement varStatement = ConvertVarStatement(source.Initializer, context);

            Expression expression = ConvertExpression(source.Expression, context);

            Statement body = ConvertStatement(source.Statement, context)?.Statement;
            context.Scope.PopBlockScope();

            if (varStatement != null && expression != null && body != null)
            {
                return new ForOfStatement(varStatement, expression, body, Location(source));
            }

            return null;
        }

        /// <summary>
        /// Variable initializer should/could have only one variable in the variable list
        /// This code is invalid: for (let x, y of [1, 2]) {}
        /// This is being checked by a lint rule already
        /// </summary>
        private VarStatement ConvertVarStatement([NotNull]VariableDeclarationListOrExpression source, ConversionContext context)
        {
            Contract.Assert(source.AsVariableDeclarationList() != null);
            Contract.Assert(source.AsVariableDeclarationList().Declarations.Count == 1);

            var variableDeclarationList = source.AsVariableDeclarationList();

            return ConvertVariableDeclarationList(variableDeclarationList, context).OfType<VarStatement>().Single();
        }

        private ReturnStatement ConvertReturnStatement(IReturnStatement returnStatement, ConversionContext context)
        {
            Expression expression = null;

            // Return statement can have a null expression (e.g. 'return;')
            if (returnStatement.Expression != null)
            {
                expression = ConvertExpression(returnStatement.Expression, context);
            }

            return new ReturnStatement(expression, Location(returnStatement));
        }

        [CanBeNull]
        private ExpressionStatement ConvertExpressionStatement(IExpressionStatement expressionStatement, ConversionContext context)
        {
            Expression expression = ConvertExpression(expressionStatement.Expression, context);
            return expression != null ? new ExpressionStatement(expression, Location(expressionStatement)) : null;
        }

        [CanBeNull]
        private ForStatement ConvertForStatement(IForStatement forStatement, ConversionContext context)
        {
            context.Scope.PushBlockScope(forStatement.Locals);

            Statement initializer = null;
            if (forStatement.Initializer != null)
            {
                // Currently the previous condition is enforced by the lint rule, but this could be relaxed in the future.
                initializer = ConvertVarStatement(forStatement.Initializer, context);
            }

            Expression condition = null;
            if (forStatement.Condition != null)
            {
                condition = ConvertExpression(forStatement.Condition, context);
            }

            AssignmentOrIncrementDecrementExpression incrementor = null;
            if (forStatement.Incrementor != null)
            {
                incrementor = ConvertForIncrementorExpression(forStatement.Incrementor, context);
            }

            Statement body = ConvertStatement(forStatement.Statement, context)?.Statement;

            context.Scope.PopBlockScope();

            if (body != null)
            {
                return new ForStatement(initializer, condition, incrementor, body, Location(forStatement));
            }

            return null;
        }

        private AssignmentOrIncrementDecrementExpression ConvertForIncrementorExpression(IExpression incrementor, ConversionContext context)
        {
            // An incrementor needs to always be an assignment expression or a postfix incrementor or decrementor. That's being checked by a lint rule.
            Contract.Assume(incrementor.As<IBinaryExpression>() != null || incrementor.As<IPostfixUnaryExpression>() != null);

            // First, we check if it's a proper assignment
            var binaryIncrementor = incrementor.As<IBinaryExpression>();
            if (binaryIncrementor != null)
            {
                var assignmentOperator = s_syntaxKindToAssignmentOperatorMapping[binaryIncrementor.OperatorToken.Kind];
                return ConvertAssignmentExpression(binaryIncrementor, assignmentOperator, context);
            }

            // If not, it must be a postfix increment or decrement
            var unaryIncrementor = incrementor.Cast<IPostfixUnaryExpression>();
            var incrementDecrementOperator = s_syntaxKindToIncrementDecrementOperator[(IncrementDecrementOperator.Postfix, unaryIncrementor.Operator)];
            return ConvertAssignmentExpression(unaryIncrementor, unaryIncrementor.Operand, context.Scope, incrementDecrementOperator);
        }

        private WhileStatement ConvertWhileStatement(IWhileStatement whileStatement, ConversionContext context)
        {
            context.Scope.PushBlockScope(whileStatement.Locals ?? TypeScript.Net.Types.SymbolTable.Empty);

            Expression condition = ConvertExpression(whileStatement.Expression, context);
            Statement body = ConvertStatement(whileStatement.Statement, context)?.Statement;

            context.Scope.PopBlockScope();

            if (body != null)
            {
                return new WhileStatement(condition, body, Location(whileStatement));
            }

            return null;
        }

        private IfStatement ConvertIfStatement(IIfStatement source, ConversionContext context)
        {
            Expression condition = ConvertExpression(source.Expression, context);
            Statement thenStatement = ConvertStatement(source.ThenStatement, context)?.Statement;

            // This one is ok to be null
            Statement elseStatement = source.ElseStatement ? ConvertStatement(source.ElseStatement.Value, context)?.Statement : null;

            if (condition != null && thenStatement != null)
            {
                return new IfStatement(condition, thenStatement, elseStatement, Location(source));
            }

            return null;
        }

        private List<Statement> ConvertVariableDeclarationList(IVariableDeclarationList variableDeclarationList, ConversionContext context)
        {
            var result = new List<Statement>(variableDeclarationList.Declarations.Count);
            foreach (var declaration in variableDeclarationList.Declarations)
            {
                SymbolAtom name = SymbolAtom.Create(StringTable, declaration.Name.GetText());

                // Local table should already have a variable with this name
                if (!context.Scope.TryResolveFromCurrentFunctionScope(name, out VariableDefinition local))
                {
                    Contract.Assert(false, "Failed to find local variable that definitely should be added to local table before statement conversion!");
                }

                // It is ok if type is null here.
                Type type = ConvertType(declaration.Type, context.CurrentQualifierSpaceId);

                Expression initializer = declaration.Initializer != null ? ConvertExpression(declaration.Initializer, context) : null;
                result.Add(new VarStatement(name, local.Index, type, initializer, Location(variableDeclarationList)));
            }

            return result;
        }

        /// <summary>
        /// Creates an expression that evaluates to the result of merging the given initializer with the
        /// captured template.
        /// </summary>
        private Expression CreateTemplateMergedInitializer(IVariableDeclaration declaration, Expression initializer)
        {
            var container = FindEnclosingNamespaceName(declaration, out string enclosingNamespace);

            // If the declaration is at the top level, then there is no parent template and therefore no merging.
            if (enclosingNamespace == Constants.Names.RuntimeRootNamespaceAlias)
            {
                return initializer;
            }

            Contract.Assert(container != null);
            Contract.Assert(container.Parent != null);

            // Otherwise, we create a template declaration whose initializer is the result of merging the parent template with the current template.
            // The location of the new initializer is hardcoded to be the same as the initializer. Observe this is not a problem since calling to 'merge'
            // never fails, and therefore no errors should be ever reported regarind merging. If there is a problem regarding the initializer itself, then
            // the original location will be used, which is the expected behavior

            // We look for a template declaration but starting from the parent location, since the current template declarations is shadowing it
            if (!TryLookUpTemplateAndCreateReference(container.Parent, initializer.Location, out Expression parentTemplate))
            {
                // If no parent template declaration is found, then no merging takes place
                return initializer;
            }

            // parentTemplate.merge
            var mergeSelector = new SelectorExpression(parentTemplate, SymbolAtom.Create(StringTable, Constants.Names.MergeFunction),
                    initializer.Location);

            // parentTemplate.merge(initializer)
            var mergeInitializer = ApplyExpression.Create(mergeSelector, new[] { initializer }, initializer.Location);

            return mergeInitializer;
        }

        /// <summary>
        /// Determines whether a template is in scope of the current node. Creates a template reference to the template in scope or the empty object literal otherwise.
        /// </summary>
        private bool TryLookUpTemplateAndCreateReference([CanBeNull]INode currentNode, LineInfo referenceLocation, out Expression templateReference)
        {
            // This means we are probably out of scopes and we reached a null parent. In that case, there is definitively not a template in scope
            if (currentNode == null)
            {
                // The default template is not really defined anywhere, so reporting an invalid location.
                templateReference = new ObjectLiteral0(referenceLocation, AbsolutePath.Invalid);
                return false;
            }

            var templateSymbol = m_semanticModel.GetTemplateAtLocation(currentNode);
            if (templateSymbol == null)
            {
                templateReference = new ObjectLiteral0(referenceLocation, AbsolutePath.Invalid);
                return false;
            }

            // We found a template to capture. We create a regular reference to it, so
            // it can be later evaluated
            templateReference = CreateSymbolReferenceExpression(
                m_conversionContext.TemplateReference,
                templateSymbol,
                referenceLocation);

            return true;
        }

        private List<Statement> ConvertVariableStatement(IVariableStatement statement, ConversionContext context)
        {
            return ConvertVariableDeclarationList(statement.DeclarationList, context);
        }

        private FunctionDeclaration ConvertFunctionDeclaration(IFunctionDeclaration source, ModuleLiteral module, NamespaceScope namespaceScope, QualifierSpaceId currentQualifierSpaceId)
        {
            var name = SymbolAtom.Create(StringTable, source.Name.Text);

            var functionScope = namespaceScope.NewFunctionScope(source.Locals);
            var signature = ConvertCallSignature(source, currentQualifierSpaceId);

            Declaration.DeclarationFlags modifier = ConvertModifiers(source);

            Statement body = null;

            // Only for ambient functions body can be null.
            Contract.Assert(
                source.Body != null || (modifier & Declaration.DeclarationFlags.Ambient) != 0,
                I($"Function '{source.Name.Text}' has an empty body but it is not an ambient function"));

            var location = Location(source);

            if (source.Body != null)
            {
                // Need to convert statements manually, because ConvertBlock function will introduce another scope for local variables
                var context = new ConversionContext(functionScope, currentQualifierSpaceId);
                Statement[] statements = ConvertStatements(source.Body.Statements, context);
                body = new BlockStatement(statements, Location(source.Body));
            }
            else
            {
                // Currently only ambients may define an ambient function.
                // This logic could not be moved to linter, because currently there is no way to skip ambients in linter.
                // Note, that we don't have to check ambient variable declarations, because 'declare const x' is not allowed,
                // and for 'declare var x' linter rule will warn any way.
                RuntimeModelContext.Logger.ReportNotSupportedCustomAmbientFunctions(RuntimeModelContext.LoggingContext, location.AsLoggingLocation());
                return null;
            }

            var result = new FunctionDeclaration(namespaceScope.FullName, name, signature, body, functionScope.Captures, functionScope.Locals, modifier, location, StringTable);
            AddFunctionDeclarationToModule(result, module, location);

            return result;
        }

        private void AddFunctionDeclarationToModule(FunctionDeclaration function, ModuleLiteral module, in UniversalLocation location)
        {
            Contract.Requires(function != null);
            Contract.Requires(module != null);
            m_binder.AddFunctionDeclaration(module, function, location);
        }

        private BlockStatement ConvertBlock(IBlock block, ConversionContext context)
        {
            // Block could be nested in other statements, like function or switch.
            // In this case locals were already added.
            // But in some cases, block could be a stand alone construct and in this case
            // we need to add locals.
            if (block.Locals != null)
            {
                context.Scope.PushBlockScope(block.Locals);
            }

            Statement[] statements = ConvertStatements(block.Statements, context);

            if (block.Locals != null)
            {
                context.Scope.PopBlockScope();
            }

            return new BlockStatement(statements, Location(block));
        }

        private CallSignature ConvertCallSignature(ISignatureDeclaration source, QualifierSpaceId currentQualifierSpaceId)
        {
            var typeParameters = ConvertTypeParameters(source.TypeParameters, currentQualifierSpaceId);
            Parameter[] parameters = ConvertParameters(source.Parameters, currentQualifierSpaceId);

            // It is ok if returnType is null here
            Type returnType = ConvertType(source.Type, currentQualifierSpaceId);

            return new CallSignature(typeParameters, parameters, returnType, Location(source));
        }

        private Parameter[] ConvertParameters(NodeArray<IParameterDeclaration> parameterDeclarations, QualifierSpaceId currentQualifierSpaceId)
        {
            return parameterDeclarations.Select(p => ConvertParameter(p, currentQualifierSpaceId)).Where(p => p != null).ToArray();
        }

        private Parameter ConvertParameter(IParameterDeclaration parameterDeclaration, QualifierSpaceId currentQualifierSpaceId)
        {
            // Parameter initializer must be null since we don't support default arguments. This is checked by a lint rule.
            Contract.Assert(parameterDeclaration.Initializer == null);

            SymbolAtom parameterName = SymbolAtom.Create(StringTable, parameterDeclaration.Name.GetName());
            var location = Location(parameterDeclaration);

            // It is ok if parameterType is null here
            Type parameterType = ConvertType(parameterDeclaration.Type, currentQualifierSpaceId);
            ParameterKind parameterKind = ConvertParameterKind(parameterDeclaration);

            return new Parameter(parameterName, parameterType, parameterKind, location);
        }

        private static ParameterKind ConvertParameterKind(IParameterDeclaration parameterDeclaration)
        {
            if (parameterDeclaration.QuestionToken.HasValue)
            {
                return ParameterKind.Optional;
            }

            if (parameterDeclaration.DotDotDotToken.HasValue)
            {
                return ParameterKind.Rest;
            }

            return ParameterKind.Required;
        }

        [NotNull]
        private IReadOnlyList<TypeParameter> ConvertTypeParameters(NodeArray<ITypeParameterDeclaration> typeParameters, QualifierSpaceId currentQualifierSpaceId)
        {
            // This method is useful because typeParameters in most cases are empty, so this special case could be covered in one place!
            return typeParameters.Select(t => ConvertTypeParameter(t, currentQualifierSpaceId)).ToList();
        }

        private TypeParameter ConvertTypeParameter(ITypeParameterDeclaration typeParameterDeclaration, QualifierSpaceId currentQualifierSpaceId)
        {
            SymbolAtom parameterName = SymbolAtom.Create(StringTable, typeParameterDeclaration.Name.Text);

            // It is ok if extendedType is null here
            Type extendedType = ConvertType(typeParameterDeclaration.Constraint, currentQualifierSpaceId);
            return new TypeParameter(parameterName, extendedType, Location(typeParameterDeclaration));
        }

        /// <summary>
        /// Converts <paramref name="source"/> to <see cref="VarDeclaration"/>.
        /// </summary>
        /// <remarks>
        /// Method takes <see cref="NodeFlags"/> because export modifier is applied on variable statement level but not on <see cref="IVariableDeclarationList"/>.
        /// </remarks>
        private List<VarDeclaration> ConvertTopLevelVariableDeclarations(
            ModuleLiteral module,
            IVariableDeclarationList source,
            NodeFlags nodeFlags,
            NamespaceScope namespaces,
            QualifierSpaceId currentQualifierSpaceId)
        {
            List<VarDeclaration> declarations = new List<VarDeclaration>(source.Declarations.Count);

            foreach (var declaration in source.Declarations.AsStructEnumerable())
            {
                // Variable declarations (that are not ambient declarations) must always have an initializer. There is a lint rule that checks for this.
                Contract.Assert((NodeUtilities.GetCombinedNodeFlags(declaration) & NodeFlags.Ambient) != 0 || declaration.Initializer != null);

                var nameStr = declaration.Name.GetText();
                var name = SymbolAtom.Create(StringTable, nameStr);

                // It is ok if type is null here
                Type type = m_conversionConfiguration.UnsafeOptions.SkipTypeConversion ? null : ConvertType(declaration.Type, currentQualifierSpaceId);

                Expression initializerExpression = null;
                if (declaration.Initializer != null)
                {
                    // It is ok that the returned initializer is null, since VarDeclaration is also used for for-of and for

                    // Using empty symbol table, because ConvertExpression requires it. But technically speaking it is not needed
                    // because no local binding is happening on the expression level.
                    initializerExpression = ConvertExpression(declaration.Initializer, namespaces.NewFunctionScope(TypeScript.Net.Types.SymbolTable.Empty), currentQualifierSpaceId);

                    // If it is a template declaration, special treatment is needed to reflect template semantics
                    // This is a V2-specific feature.
                    // The template initializer is the result of merging with its parent template (if present)
                    if (declaration.IsTemplateDeclaration())
                    {
                        initializerExpression = CreateTemplateMergedInitializer(declaration, initializerExpression);
                    }
                }

                var flags = ConvertModifiers(nodeFlags);

                var location = Location(declaration);
                var variableDeclaration = new VarDeclaration(name, type, initializerExpression, flags, location);
                FullSymbol fullName = GetVariableFullNameIfPreservingFullNamesEnabled(declaration.Symbol);

                // We try to find if there is a template declaration in scope and generate an expression that evaluates to it
                // If the declaration is already a template declaration, we first go up to the parent namespace to avoid finding the same declaration again
                var currentNode = declaration.IsTemplateDeclaration() ? FindEnclosingNamespaceName(declaration).Parent : declaration;

                TryLookUpTemplateAndCreateReference(currentNode, variableDeclaration.Location, out var currentTemplate);

                m_binder.AddVariableBinding(module, variableDeclaration, fullName, currentQualifierSpaceId, currentTemplate, location);

                declarations.Add(variableDeclaration);
            }

            return declarations;
        }

        private FullSymbol GetVariableFullNameIfPreservingFullNamesEnabled(ISymbol declarationSymbol)
        {
            if (m_conversionConfiguration.PreserveFullNameSymbols)
            {
                var fullNameAsString = m_semanticModel?.GetFullyQualifiedName(declarationSymbol);
                if (!string.IsNullOrEmpty(fullNameAsString))
                {
                    return FullSymbol.Create(SymbolTable, new StringSegment(fullNameAsString));
                }
            }

            return FullSymbol.Invalid;
        }

        private static Type TryConvertPredefinedType(ITypeNode type)
        {
            switch (type.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.AnyKeyword:
                    return PrimitiveType.AnyType;

                case TypeScript.Net.Types.SyntaxKind.NumberKeyword:
                    return PrimitiveType.NumberType;

                case TypeScript.Net.Types.SyntaxKind.BooleanKeyword:
                    return PrimitiveType.BooleanType;

                case TypeScript.Net.Types.SyntaxKind.StringKeyword:
                    return PrimitiveType.StringType;

                case TypeScript.Net.Types.SyntaxKind.VoidKeyword:
                    return PrimitiveType.VoidType;
            }

            return null;
        }

        /// <summary>
        /// An entity name is either an identifier or a qualified name. Returns a list of atoms corresponding to the entity name.
        /// </summary>
        private static List<SymbolAtom> GetAtomsFromQualifiedName(StringTable stringTable, EntityName entityName)
        {
            if (entityName.Kind == TypeScript.Net.Types.SyntaxKind.Identifier)
            {
                return new List<SymbolAtom>() { SymbolAtom.Create(stringTable, entityName.Text) };
            }

            var qualifiedName = entityName.AsQualifiedName();
            var atoms = GetAtomsFromQualifiedName(stringTable, qualifiedName.Left);
            atoms.Add(SymbolAtom.Create(stringTable, qualifiedName.Right.Text));

            return atoms;
        }

        private Type ConvertType(ITypeNode type, QualifierSpaceId currentQualifierSpaceId)
        {
            // Type could be null in many places in the AST.
            // So instead of checking this in every caller, this function will just return null.
            // When ConvertType is explicitly called with null, it is ok to propagate null and don't report errors,
            // since it is representing a valid case of an absent type
            // The only case when an error is reported is in the catch-all code at the bottom of this method.
            if (type == null)
            {
                return null;
            }

            // Checking for predefined types like number, int, string, etc
            var predefinedType = TryConvertPredefinedType(type);
            if (predefinedType != null)
            {
                return predefinedType;
            }

            // Checking for named types like:
            // let n: CustomInterface = undefined;
            //        ~~~~~~~~~~~~~~~
            var typeReferenceNode = type.As<ITypeReferenceNode>();
            if (typeReferenceNode != null)
            {
                EntityName entityName = typeReferenceNode.TypeName;

                SymbolAtom[] typeName = GetAtomsFromQualifiedName(StringTable, entityName).ToArray();

                Type[] typeArguments = typeReferenceNode.TypeArguments?.Select(t => ConvertType(t, currentQualifierSpaceId)).Where(t => t != null).ToArray() ?? CollectionUtilities.EmptyArray<Type>();
                return new NamedTypeReference(typeName, typeArguments, Location(typeReferenceNode));
            }

            // Checking for typed literals like:
            // let n: {x: number, y: number} = {x: 1, y: 3};
            //        ~~~~~~~~~~~~~~~~~~~~~~
            var typeLiteralNode = type.As<ITypeLiteralNode>();
            if (typeLiteralNode != null)
            {
                Signature[] members = typeLiteralNode.Members.Select(m => ConvertInterfaceMember(m, currentQualifierSpaceId)).Where(m => m != null).ToArray();
                return new ObjectType(members, Location(typeLiteralNode));
            }

            // Check for array types like:
            // let n: number[] = [1,2];
            //        ~~~~~~~~
            var arrayTypeNode = type.As<IArrayTypeNode>();
            if (arrayTypeNode != null)
            {
                var elementType = ConvertType(arrayTypeNode.ElementType, currentQualifierSpaceId);
                return elementType != null ? new ArrayType(elementType, Location(arrayTypeNode)) : null;
            }

            // Check for function types like:
            // function foo(): () => number {}
            //                 ~~~~~~~~~~~~
            var functionType = type.As<IFunctionOrConstructorTypeNode>();
            if (functionType != null)
            {
                var typeParameters = ConvertTypeParameters(functionType.TypeParameters, currentQualifierSpaceId);
                Parameter[] parameters = ConvertParameters(functionType.Parameters, currentQualifierSpaceId);
                Type returnType = ConvertType(functionType.Type, currentQualifierSpaceId);

                return returnType != null ? new FunctionType(typeParameters, parameters, returnType, Location(functionType)) : null;
            }

            // Check for parenthesized types like:
            // function foo(): (() => number)[] {return [];}
            //                 ~~~~~~~~~~~~~~~~
            var parenthesizedType = type.As<IParenthesizedTypeNode>();
            if (parenthesizedType != null)
            {
                return ConvertType(parenthesizedType.Type, currentQualifierSpaceId);
            }

            // Checking for union types like:
            // type X = string | number;
            //          ~~~~~~~~~~~~~~~
            var unionType = type.As<IUnionTypeNode>();
            if (unionType != null)
            {
                Type[] types = unionType.Types.Select(t => ConvertType(t, currentQualifierSpaceId)).Where(t => t != null).ToArray();
                return new UnionType(types, Location(unionType));
            }

            // Checking for tuple types like:
            // type X = [1, 2];
            //          ~~~~~~~~~~~~~~~
            var tupleType = type.As<ITupleTypeNode>();
            if (tupleType != null)
            {
                Type[] types = tupleType.ElementTypes.Select(t => ConvertType(t, currentQualifierSpaceId)).Where(t => t != null).ToArray();
                return new TupleType(types, Location(tupleType));
            }

            // Checking for type query like:
            // type X = typeof anIdentifier;
            //          ~~~~~~~~~~~~~~~
            var typeQueryType = type.As<ITypeQueryNode>();
            if (typeQueryType != null)
            {
                var entityName = typeQueryType.ExprName;

                SymbolAtom[] typeName = GetAtomsFromQualifiedName(StringTable, entityName).ToArray();

                var namedTypeReference = new NamedTypeReference(typeName, CollectionUtilities.EmptyArray<Type>(), Location(typeQueryType));

                return new TypeQuery(namedTypeReference, Location(typeQueryType));
            }

            switch (type.Kind)
            {
                // Checking for type literals like:
                // interface X { kind: "X" }
                case TypeScript.Net.Types.SyntaxKind.StringLiteralType:
                    // Currently, there is no special logic that differentiates literal types from string
                    return PrimitiveType.StringType;

                // Checking for type predicates like:
                // function foo(x: any): x is string {...}
                case TypeScript.Net.Types.SyntaxKind.TypePredicate:
                    // From interpreter's perspective, type predicate is just a boolean.
                    // Main part of type predicate is a hint to the typechecker that is missing completely!
                    return PrimitiveType.BooleanType;
            }

            string message = I($"Type '{type.Kind}' appeared in expression '{type.GetFormattedText()}' is not supported.");
            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(type).AsLoggingLocation(), message);
            return null;
        }

        private static Declaration.DeclarationFlags ConvertModifiers(NodeFlags flags)
        {
            var result = Declaration.DeclarationFlags.None;

            if ((flags & NodeFlags.Export) != 0)
            {
                result |= Declaration.DeclarationFlags.Export;
            }

            if ((flags & NodeFlags.Ambient) != 0)
            {
                result |= Declaration.DeclarationFlags.Ambient;
            }

            return result;
        }

        private static Declaration.DeclarationFlags ConvertModifiers(INode node)
        {
            return ConvertModifiers(node.Modifiers?.Flags ?? NodeFlags.None);
        }

        private ModuleDeclaration ConvertNamespaceDeclaration(IModuleDeclaration source, ModuleLiteral moduleLiteral, NamespaceScope namespaces, QualifierSpaceId currentQualifierSpaceId)
        {
            return ConvertNamespaceDeclarationWithV2(source, moduleLiteral, namespaces);
        }

        private ModuleDeclaration ConvertNamespaceDeclarationWithV2(IModuleDeclaration source, ModuleLiteral moduleLiteral, NamespaceScope namespaces)
        {
            var names = new List<SymbolAtom> { SymbolAtom.Create(StringTable, source.Name.Text) };

            var flags = ConvertModifiers(source);

            UniversalLocation location = Location(source);

            // Semantic resolution is guaranteed to be on here. But still we could be converting a V1 module, so we need to extract the qualifier space
            // in a generic way
            var qualifierSpaceId = ExtractSourceQualifierSpace(source.Symbol);
            var module = CreateNamespaceModule(moduleLiteral, names, location, qualifierSpaceId);

            var block = source.Body;
            var nestedModule = block.As<IModuleDeclaration>();

            IReadOnlyList<Declaration> declarations;

            if (nestedModule != null)
            {
                declarations = new Declaration[] { ConvertNamespaceDeclarationWithV2(nestedModule, module, namespaces) };
            }
            else
            {
                var nestedNamespace = new NamespaceScope(names, block.Parent.Locals, CurrentSourceFile, RuntimeModelContext.PathTable, StringTable, namespaces);
                declarations = ConvertDeclarationStatements(
                    block.Statements,
                    module,
                    nestedNamespace,
                    qualifierSpaceId);
            }

            return new ModuleDeclaration(names[0], declarations, flags, Location(source));
        }

        private ImportDeclaration ConvertImportDeclaration(IImportDeclaration source, QualifierSpaceId currentQualifierSpaceId)
        {
            var importClause = ConvertImportClause(source);

            if (source.IsLikeImport)
            {
                // DScript doesn't support `import * from` syntax.
                // TODO: This can be moved to a linter.
                RuntimeModelContext.Logger.ReportImportStarIsNotSupportedWithSemanticResolution(
                    RuntimeModelContext.LoggingContext,
                    Location(source).AsLoggingLocation());
                return null;
            }

            var moduleSpecifier = source.ModuleSpecifier.As<IStringLiteral>();

            // Linter.
            Contract.Assume(moduleSpecifier != null, "Module specifier must be a literal expression.");

            Expression pathSpecifier = ConvertPathSpecifier(moduleSpecifier);

            if (pathSpecifier == null)
            {
                // Module resolution failed or import declaration is not referenced.
                return null;
            }

            // Get decorators if any.
            var decoratorExpressions = ConvertDecorators(source, currentQualifierSpaceId);

            Declaration.DeclarationFlags modifier = ConvertModifiers(source);


            var result = new ImportDeclaration(
                importClause,
                pathSpecifier,
                null,
                decoratorExpressions,
                modifier,
                Location(source));

            // Semantic resolution requires special steps.
            // We have two cases here:
            // 1. import * as x from path;
            // 2. import {x, y as foo} from path;
            // For semantic resolution to work, current file should have entry for every symbol
            // that can be referenced by other symbols in this or in another file for the current module.
            //
            // First case is represented by ImportAliasExpression very similar to what importFrom will produce.
            // This is fine, because technically speaking, `import * as x from path; const y = x.foo;`
            // is similar to `importFrom(path).foo`.
            //
            // Second case is slightly more complicated, because it requires a projection:
            // import {x as foo} from path;
            // is equivalent to
            // `const foo = importFrom(path).x;`
            // And to mimic that, resolved expression is created
            var namespaceImport = source.ImportClause.NamedBindings.As<INamespaceImport>();

            if (namespaceImport != null)
            {
                // `import * as Alias from path;` case.
                UniversalLocation nameLocation = Location(namespaceImport);
                var importAliasExpression = CreateImportAliasExpression(pathSpecifier, nameLocation);
                if (importAliasExpression != null)
                {
                    m_binder.AddImportAliasBinding(CurrentFileModule, importAliasExpression, nameLocation);
                }
            }
            else
            {
                // `import {x as foo} from path;` case
                var namedImports = source.ImportClause.NamedBindings.Cast<INamedImports>();

                foreach (var namedImport in namedImports.Elements)
                {
                    UniversalLocation nameLocation = Location(namedImport);

                    // Creating an alias for an exported path.
                    var importAliasExpression = CreateImportAliasExpression(pathSpecifier, nameLocation);
                    if (importAliasExpression != null)
                    {
                        // Creating a projection. First, resolving the symbol
                        var importSymbol = ResolveSymbolAtPositionAndReportWarningIfObsolete(namedImport.Name);
                        importSymbol = m_semanticModel.GetAliasedSymbol(importSymbol);
                        var resolvedPosition = CreateSymbolReferenceExpression(
                            SymbolAtom.Create(StringTable, namedImport.Name.Text),
                            importSymbol,
                            nameLocation);

                        // And creating a selector expression
                        var name = SymbolAtom.Create(RuntimeModelContext.StringTable, namedImport.Name.Text);
                        var expression = new ResolvedSelectorExpression(
                            importAliasExpression,
                            resolvedPosition,
                            name,
                            importAliasExpression.Location);
                        m_binder.AddImportAliasBinding(CurrentFileModule, expression, nameLocation);
                    }
                }
            }

            return result;
        }

        private ExportDeclaration ConvertExportDeclaration(IExportDeclaration source)
        {
            // Either export clause is null, which means export *, or module specifier is null,
            // which means there is no from. But not both (since 'export *;' is not a valid expression)
            Contract.Assert(source.ExportClause != null || source.ModuleSpecifier != null);

            // This one deals with 'export *' and 'export {names}' cases
            ImportOrExportClause exportClause = ConvertExportClause(source);

            ExportDeclaration exportDeclaration;
            if (source.ModuleSpecifier != null)
            {
                var moduleSpecifier = source.ModuleSpecifier.As<IStringLiteral>();

                // This is checked by a lint rule
                Contract.Assume(moduleSpecifier != null, "Module specifier must be a literal expression.");
                var pathSpecifier = ConvertPathSpecifier(moduleSpecifier);

                if (pathSpecifier == null)
                {
                    // Module resolution failed
                    return null;
                }

                exportDeclaration = new ExportDeclaration(exportClause, pathSpecifier, Location(source));
            }
            else
            {
                exportDeclaration = new ExportDeclaration(exportClause, Location(source));
            }

            return exportDeclaration;
        }

        /// <summary>
        /// Converts a module specifier to an expression.
        /// </summary>
        /// <returns>
        /// Returns <code>null</code> if the semantic resolution is enabled but target spec is not part of the workspace.
        /// This means that there is no references for the alias and we can freely ignore this node.
        /// </returns>
        [CanBeNull]
        private Expression ConvertPathSpecifier(IStringLiteral stringLiteral)
        {
            Contract.Requires(stringLiteral != null);
            Contract.Requires(stringLiteral.LiteralKind != LiteralExpressionKind.None);

            return ConvertPathSpecifier(stringLiteral.Text, Location(stringLiteral));
        }

        [CanBeNull]
        private Expression ConvertPathSpecifier(string text, in UniversalLocation location)
        {
            Contract.Requires(text != null);

            var resolvedModuleFilePath = m_semanticModel?.TryGetResolvedModulePath(CurrentSourceFile, text);

            if (resolvedModuleFilePath == null)
            {
                // This can be a perfectly legit situation, but in some cases it can indicate a bug in the filtering logic.
                RuntimeModelContext.Logger.ReportImportAliasIsNotReferencedAndWillBeRemoved(
                    RuntimeModelContext.LoggingContext,
                    location.AsLoggingLocation(),
                    text);
                return null;
            }

            AbsolutePath resultingPath = AbsolutePath.Create(RuntimeModelContext.PathTable, resolvedModuleFilePath);
            return new ResolvedStringLiteral(resultingPath, text, location);
        }

        private ImportOrExportClause ConvertExportClause(IExportDeclaration source)
        {
            // A null exportClause means the 'export *' case. Otherwise, it's a 'export {names}' case
            var namedBinding = source.ExportClause == null
                ? new NamespaceImport(FullSymbol.Invalid, Location(source))
                : ConvertNamedImportsOrExports(new NamedImportsOrNamedExports(source.ExportClause));

            // For semantic resolution 'export {name}' creats aliases that should be registered in the current module.
            if (source.ExportClause != null)
            {
                RegisterExports(source);
            }

            return new ImportOrExportClause(namedBinding, Location(source));
        }

        private void RegisterExports(IExportDeclaration source)
        {
            INodeArray<IImportOrExportSpecifier> elements;
            var namedImportsOrNamedExports = source.ExportClause;
            if (namedImportsOrNamedExports.Kind == TypeScript.Net.Types.SyntaxKind.NamedImports)
            {
                elements = namedImportsOrNamedExports.Cast<INamedImports>().Elements;
            }
            else
            {
                elements = namedImportsOrNamedExports.Cast<INamedExports>().Elements;
            }

            foreach (var importOrExportSpecifier in elements.AsStructEnumerable())
            {
                var propertySymbol = ResolveSymbolAtPositionAndReportWarningIfObsolete(importOrExportSpecifier.Name);

                // Consider following case: 'export const x = 42; export {x}'
                // Trying to resolve symbol for 'x' checker will return a symbol that points
                // to the same node. This is well-known and desired.
                // To get a symbol for variable 'x' special API should be used: GetAliasedSymbol.
                // We *don't* want to resolve the symbol recursively: the reference may end up pointing
                // to a module, but we want to go through the appropriate import if that's the case, so
                // the runtime model points to the right construct
                propertySymbol = m_semanticModel.GetAliasedSymbol(propertySymbol, resolveAliasRecursively: false);
                if (propertySymbol != null)
                {
                    var nameAtom = SymbolAtom.Create(StringTable, importOrExportSpecifier.Name.Text);
                    var location = Location(importOrExportSpecifier);
                    var expression = CreateSymbolReferenceExpression(nameAtom, propertySymbol, location);
                    m_binder.AddExportBinding(CurrentFileModule, nameAtom, expression, location);
                }
            }
        }

        private ImportOrExportClause ConvertImportClause(IImportDeclaration source)
        {
            // NamedBindings is null for default imports like: import x from './spec.dsc'
            // This syntax is an alias for `import {default as x} from './spec.dsc';
            // DScript doesn't support default imports and this case already should be checked by the linter.
            Contract.Assert(source.ImportClause.NamedBindings != null);

            INamespaceImport namespaceImport = source.ImportClause.NamedBindings.As<INamespaceImport>();

            NamedBinding namedBinding;
            if (namespaceImport != null)
            {
                // import * as blah from 'name';
                var alias = CreateNameOrFullName(namespaceImport.Name);

                // FullSymbol means the name is a namespace. Otherwise, it is a variable name
                if (alias is FullSymbol)
                {
                    namedBinding = new NamespaceImport((FullSymbol)alias, Location(source.ImportClause));
                }
                else
                {
                    // TODO: revise! this case means 'import * as x from ...'. With lowercase x! This is preserving old behavior
                    // and will likely be refactored when we lift our casing restrictions. But with the current casing rules, this shouldn't be allowed!
                    namedBinding = new NamespaceAsVarImport((SymbolAtom)alias, Location(source.ImportClause));
                }
            }
            else
            {
                // import { IdentifierList } from 'name'
                INamedImports namedImports = source.ImportClause.NamedBindings.As<INamedImports>();
                namedBinding = ConvertNamedImportsOrExports(new NamedImportsOrNamedExports(namedImports));
            }

            return new ImportOrExportClause(namedBinding, Location(source));
        }

        private NamedBinding ConvertNamedImportsOrExports(NamedImportsOrNamedExports namedImportsOrNamedExports)
        {
            INodeArray<IImportOrExportSpecifier> elements;

            if (namedImportsOrNamedExports.Kind == TypeScript.Net.Types.SyntaxKind.NamedImports)
            {
                elements = namedImportsOrNamedExports.Cast<INamedImports>().Elements;
            }
            else
            {
                elements = namedImportsOrNamedExports.Cast<INamedExports>().Elements;
            }

            var specifiers = new List<ImportOrExportSpecifier>(elements.Count);

            foreach (var importOrExportSpecifier in elements.AsStructEnumerable())
            {
                var name = CreateNameOrFullName(importOrExportSpecifier.Name);
                var nameIsFullSymbol = name is FullSymbol;
                var asName = nameIsFullSymbol ? FullSymbol.Invalid : (BuildXL.Utilities.ISymbol)SymbolAtom.Invalid;

                ImportOrExportSpecifier specifier;
                if (nameIsFullSymbol)
                {
                    specifier = new ImportOrExportModuleSpecifier((FullSymbol)name, (FullSymbol)asName, Location(importOrExportSpecifier));
                }
                else
                {
                    specifier = new ImportOrExportVarSpecifier((SymbolAtom)name, (SymbolAtom)asName, Location(importOrExportSpecifier));
                }

                specifiers.Add(specifier);
            }

            // specifiers can be empty, but the precondition for NamedImportsOrExports is weaken now.
            return new NamedImportsOrExports(specifiers.ToArray(), Location(namedImportsOrNamedExports));
        }

        /// <summary>
        /// Creates a FullSymbol in case the identifier represents a module, or a Symbol if it is not
        /// </summary>
        private BuildXL.Utilities.ISymbol CreateNameOrFullName(IIdentifier identifier)
        {
            BuildXL.Utilities.ISymbol name;
            if (identifier.StartsWithUpperCase())
            {
                name = CreateFullName(identifier.Text);
            }
            else
            {
                name = SymbolAtom.Create(StringTable, identifier.Text);
            }

            return name;
        }

        private FullSymbol CreateFullName(string token)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(token));
            Contract.Requires(char.IsUpper(token[0]));

            return FullSymbol.Create(SymbolTable, token);
        }

        private ObjectLiteral ConvertObjectLiteral(IObjectLiteralExpression source, ConversionContext context)
        {
            if (!CheckObjectLiteralForDuplicateProperties(source))
            {
                return null;
            }

            var properties =
                source.Properties
                    .Select(e => ConvertObjectLiteralElement(e, context))
                    .ToList();

            var location = Location(source);
            var result = ObjectLiteral.Create(properties, location, CurrentFileModule.Path);

            return result;
        }

        private bool CheckObjectLiteralForDuplicateProperties(IObjectLiteralExpression source)
        {
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var element in source.Properties.AsStructEnumerable())
            {
                var propertyName = element.Name?.Text;
                Contract.Assume(!string.IsNullOrEmpty(propertyName));

                if (propertyNames.Contains(propertyName))
                {
                    RuntimeModelContext.Logger.ReportDuplicateBinding(RuntimeModelContext.LoggingContext, element.LocationForLogging(CurrentSourceFile), propertyName);
                    return false;
                }

                propertyNames.Add(propertyName);
            }

            return true;
        }

        private Binding ConvertObjectLiteralElement(IObjectLiteralElement source, ConversionContext context)
        {
            // The following contract prevents the construction of object literal from failure down stream,
            // i.e., when the evaluated object literal is created from bindings.
            // TODO: Do we ever have an object literal element with an empty or null property name?
            Contract.Assume(!string.IsNullOrEmpty(source.Name?.Text));

            StringId propertyName = StringId.Create(StringTable, source.Name.Text);

            Expression expression = null;

            // This is being checked by a lint rule
            Contract.Assert(source.Kind != TypeScript.Net.Types.SyntaxKind.MethodDeclaration);

            switch (source.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.PropertyAssignment:
                    expression = ConvertExpression(source.Cast<IPropertyAssignment>().Initializer, context);
                    break;
                case TypeScript.Net.Types.SyntaxKind.ShorthandPropertyAssignment:
                    {
                        // Shorthand property declaration are tricky.
                        // In object literal expression '{foo}' symbol 'foo' plays two roles at the same time:
                        // - Property name
                        // - Symbol 'foo'
                        // To find symbol that 'foo' points to, special API should be used - GetShorthandAssignmentValueSymbol
                        var shortHand = source.Cast<IShorthandPropertyAssignment>();

                        var symbol = m_semanticModel.GetShorthandAssignmentValueSymbol(shortHand);
                        var declaration = m_semanticModel.GetFirstNotFilteredDeclarationOrDefault(symbol);
                        if (declaration != null)
                        {
                            // Every property, referenced in object literal needs to be stored in a new referenced symbol table.
                            // This will gives and ability to resolve name in the following case:
                            // function foo(x: {bar: string}) { return x.foo;}
                            // foo({bar});
                            // In this case, we'll resolve where {bar} is points to and store this entry in a symbol table.
                            // Later, when 'x.foo' would be resolved, we'll look up in the symbol table and will find the entry
                            // that we just added.
                            // The only trick here is not to add redundant entries for local variable declaration or any
                            // other declarations that property 'bar' can reference to (because they already added during conversion).
                            // For instance, property 'foo' may point to a local function declaration and we'll add the entry
                            // in a symbol table for function declaration during conversion process.
                            //
                            // To understand this in more details, try to comment following line and debug one of the failing unit tests.
                        }

                        expression = ConvertIdentifier(shortHand.Name.Text, symbol, Location(shortHand), context);
                        break;
                    }
            }

            // It is ok that expression is null here, binder returns undefined instance
            object body = m_binder.ToBindingObject(expression, thunking: false);
            return new Binding(propertyName, body, Location(source));
        }

        /// <summary>
        /// Converts expression from TypeScript AST to evaluation AST.
        /// </summary>
        internal Expression ConvertExpression([NotNull] IExpression expression, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            var context = new ConversionContext(escapes, currentQualifierSpaceId);
            return ConvertExpression(expression, context);
        }

        /// <summary>
        /// Converts expression from TypeScript AST to evaluation AST.
        /// </summary>
        private Expression ConvertExpression([NotNull]IExpression expression, ConversionContext context)
        {
            var literal = expression.As<ILiteralExpression>();
            if (literal != null)
            {
                // isNegative could be true only for converting unary expression that has
                // special case for literal expressions any way.
                return ConvertLiteral(literal, isNegative: false);
            }

            switch (expression.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.TrueKeyword:
                    return BoolLiteral.CreateTrue(Location(expression));

                case TypeScript.Net.Types.SyntaxKind.FalseKeyword:
                    return BoolLiteral.CreateFalse(Location(expression));

                case TypeScript.Net.Types.SyntaxKind.ExpressionStatement:
                    return ConvertExpression(expression.Cast<IExpressionStatement>().Expression, context);

                case TypeScript.Net.Types.SyntaxKind.Identifier:
                    return ConvertIdentifier(expression.Cast<IIdentifier>(), context);

                case TypeScript.Net.Types.SyntaxKind.BinaryExpression:
                    return ConvertBinaryExpression(expression.Cast<IBinaryExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.CallExpression:
                    return ConvertCallExpression(expression.Cast<ICallExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.ArrayLiteralExpression:
                    return ConvertArrayExpression(expression.Cast<IArrayLiteralExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression:
                    return ConvertPropertyAccessExpression(expression.Cast<IPropertyAccessExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.ElementAccessExpression:
                    return ConvertElementAccessExpression(expression.Cast<IElementAccessExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.SpreadElementExpression:
                    return ConvertSpreadExpression(expression.Cast<ISpreadElementExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.PrefixUnaryExpression:
                    return ConvertPrefixUnaryExpression(expression.Cast<IPrefixUnaryExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.PostfixUnaryExpression:
                    return ConvertPostfixUnaryExpression(expression.Cast<IPostfixUnaryExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.ParenthesizedExpression:
                    return ConvertParenthesizedExpression(expression.Cast<IParenthesizedExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.ConditionalExpression:
                    return ConvertConditionalExpression(expression.Cast<IConditionalExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.SwitchExpression:
                    return ConvertSwitchExpression(expression.Cast<ISwitchExpression>(), context);
                case TypeScript.Net.Types.SyntaxKind.SwitchExpressionClause:
                    return ConvertSwitchExpressionClause(expression.Cast<ISwitchExpressionClause>(), context);

                case TypeScript.Net.Types.SyntaxKind.TypeAssertionExpression:
                    return ConvertTypeAssertionExpression(expression.Cast<ITypeAssertion>(), context);
                case TypeScript.Net.Types.SyntaxKind.AsExpression:
                    return ConvertAsExpression(expression.Cast<IAsExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.ObjectLiteralExpression:
                    return ConvertObjectLiteral(expression.Cast<IObjectLiteralExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.ArrowFunction:
                    return ConvertArrowFunction(expression.Cast<IArrowFunction>(), context);

                case TypeScript.Net.Types.SyntaxKind.TaggedTemplateExpression:
                    return ConvertInterpolation(expression.Cast<ITaggedTemplateExpression>(), context);
                case TypeScript.Net.Types.SyntaxKind.TemplateExpression:
                    return ConvertStringInterpolation(expression.Cast<ITemplateExpression>(), context);

                case TypeScript.Net.Types.SyntaxKind.TypeOfExpression:
                    return ConvertTypeOfExpression(expression.Cast<ITypeOfExpression>(), context);

                // Omitted expression will occur in this case: [1,,2]
                case TypeScript.Net.Types.SyntaxKind.OmittedExpression:
                    return UndefinedLiteral.Instance;
            }

            string message = I($"Expression with type '{expression.Kind}' appeared at expression '{expression.GetFormattedText()}' is not supported.");
            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(expression).AsLoggingLocation(), message);
            return null;
        }

        private Expression ConvertInterpolation(ITaggedTemplateExpression taggedTemplateExpression, ConversionContext context)
        {
            return m_interpolationConverter.ConvertInterpolation(taggedTemplateExpression, context.Scope, context.CurrentQualifierSpaceId);
        }

        private Expression ConvertStringInterpolation(ITemplateExpression source, ConversionContext context)
        {
            return m_interpolationConverter.ConvertStringInterpolation(source, context.Scope, context.CurrentQualifierSpaceId);
        }

        private Expression ConvertTypeOfExpression(ITypeOfExpression source, ConversionContext context)
        {
            UnaryOperator @operator = UnaryOperator.TypeOf;
            Expression expression = ConvertExpression(source.Expression, context);

            return expression != null ? new UnaryExpression(@operator, expression, Location(source)) : null;
        }

        private FunctionLikeExpression ConvertArrowFunction(IArrowFunction source, ConversionContext context)
        {
            // For expression-body functions, locals could be null.
            var nestedEscapes = context.Scope.CreateNestedScope(source.Locals ?? TypeScript.Net.Types.SymbolTable.Empty);

            var signature = ConvertCallSignature(source, context.CurrentQualifierSpaceId);

            Statement body = null;
            if (source.Body.Block() != null)
            {
                var nestedContext = context.WithScope(nestedEscapes);
                body = ConvertBlock(source.Body.Block(), nestedContext);
            }
            else
            {
                var expressionBody = source.Body.Expression();

                Contract.Assert(expressionBody != null, "Expression should not be null, because Block() was null");

                var convertedExpression = ConvertExpression(expressionBody, nestedEscapes, context.CurrentQualifierSpaceId);
                if (convertedExpression != null)
                {
                    body = new ReturnStatement(convertedExpression, Location(expressionBody));
                }
            }

            return body != null
                ? FunctionLikeExpression.CreateLambdaExpression(
                    signature,
                    body,
                    nestedEscapes.Captures,
                    nestedEscapes.Locals,
                    location: Location(source))
                : null;
        }

        private IndexExpression ConvertElementAccessExpression(IElementAccessExpression source, ConversionContext context)
        {
            Expression thisExpression = ConvertExpression(source.Expression, context);

            // From the AST point of view, source.ArgumentExpression can be null,
            // from the TypeScript grammar point of view, this property is not-nullable.
            // In the current implementation, the validation is happening by the TypeChecker
            // that opens a possibility of getting NRE in the AstConverter because TypeChecker is optional in the current version of DScript.
            // To avoid NullReferenceException, we need to make an additional check here as well.
            // The check should be removed if the managed checker would be required for all builds.
            if (source.ArgumentExpression == null)
            {
                RuntimeModelContext.Logger.ReportExpressionExpected(RuntimeModelContext.LoggingContext, source.LocationForLogging(CurrentSourceFile));
                return null;
            }

            Expression index = ConvertExpression(source.ArgumentExpression, context);

            return (thisExpression != null && index != null)
                ? new IndexExpression(thisExpression, index, Location(source))
                : null;
        }

        private Expression ConvertAsExpression(IAsExpression source, ConversionContext context)
        {
            Expression expression = ConvertExpression(source.Expression, context);

            if (m_conversionConfiguration.UnsafeOptions.SkipTypeConversion)
            {
                return expression;
            }
            
            Type type = ConvertType(source.Type, context.CurrentQualifierSpaceId);
            CastExpression.TypeAssertionKind castKind = CastExpression.TypeAssertionKind.AsCast;

            return (type != null && expression != null) ? new CastExpression(expression, type, castKind, Location(source)) : null;
        }

        private CastExpression ConvertTypeAssertionExpression(ITypeAssertion source, ConversionContext context)
        {
            Expression expression = ConvertExpression(source.Expression, context);
            Type type = ConvertType(source.Type, context.CurrentQualifierSpaceId);
            CastExpression.TypeAssertionKind castKind = CastExpression.TypeAssertionKind.TypeCast;

            return (type != null && expression != null) ? new CastExpression(expression, type, castKind, Location(source)) : null;
        }

        private Expression ConvertConditionalExpression(IConditionalExpression source, ConversionContext context)
        {
            Expression conditionalExpression = ConvertExpression(source.Condition, context);
            Expression thenExpression = ConvertExpression(source.WhenTrue, context);
            Expression elseExpression = ConvertExpression(source.WhenFalse, context);

            return (conditionalExpression != null && thenExpression != null && elseExpression != null)
                ? new ConditionalExpression(conditionalExpression, thenExpression, elseExpression, Location(source))
                : null;
        }

        private Expression ConvertSwitchExpression(ISwitchExpression source, ConversionContext context)
        {
            Expression expression = ConvertExpression(source.Expression, context);
            SwitchExpressionClause[] clauses = source.Clauses
                .Select(a => ConvertSwitchExpressionClause(a, context))
                .Where(a => a != null)
                .ToArray();

            return (expression != null)
                ? new SwitchExpression(expression, clauses, Location(source))
                : null;
        }

        private SwitchExpressionClause ConvertSwitchExpressionClause(ISwitchExpressionClause source, ConversionContext context)
        {
            Expression expression = ConvertExpression(source.Expression, context);
            if (expression == null)
            {
                return null;
            }

            if (source.IsDefaultFallthrough)
            {
                return new SwitchExpressionClause(expression, Location(source));
            }
            else
            {
                Expression match = ConvertExpression(source.Match, context);
                if (match == null)
                {
                    return null;
                }

                return new SwitchExpressionClause(match, expression, Location(source));
            }
        }

        private Expression ConvertParenthesizedExpression(IParenthesizedExpression source, ConversionContext context)
        {
            return ConvertExpression(source.Expression, context);
        }

        private Expression ConvertPrefixUnaryExpression(IPrefixUnaryExpression source, ConversionContext context)
        {
            if (s_syntaxKindToIncrementDecrementOperator.TryGetValue((IncrementDecrementOperator.Prefix, source.Operator), out IncrementDecrementOperator incrementDecrementOperator))
            {
                return ConvertAssignmentExpression(source, source.Operand, context.Scope, incrementDecrementOperator);
            }

            UnaryOperator? @operator = ConvertPrefixUnaryOperator(source.Operator, source);

            // Need to add a special case for negative literals to cover following case:
            // If text is -2147483648 there is no way to convert 2147483648 to int (because of overflow).
            // So literal conversion should know about the negative sign.
            // But in the case result should be just a literal, because
            // UnaryExpression would negate the value once again.
            var literal = source.Operand.As<ILiteralExpression>();
            if (@operator == UnaryOperator.Negative && literal != null)
            {
                return ConvertLiteral(literal, @operator == UnaryOperator.Negative);
            }

            Expression expression = ConvertExpression(source.Operand, context);

            return (expression != null && @operator.HasValue) ? new UnaryExpression(@operator.Value, expression, Location(source)) : null;
        }

        private UnaryOperator? ConvertPrefixUnaryOperator(TypeScript.Net.Types.SyntaxKind operatorKind, IPrefixUnaryExpression source)
        {
            if (!s_syntaxKindToPrefixUnaryOperatorMapping.TryGetValue(operatorKind, out UnaryOperator result))
            {
                string message = I($"Unary prefix operator kind {operatorKind} is not supported yet. Fix me!");
                RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation(), message);

                return null;
            }

            return result;
        }

        private Expression ConvertPostfixUnaryExpression(IPostfixUnaryExpression source, ConversionContext context)
        {
            if (s_syntaxKindToIncrementDecrementOperator.TryGetValue((IncrementDecrementOperator.Postfix, source.Operator), out IncrementDecrementOperator incrementDecrementOperator))
            {
                return ConvertAssignmentExpression(source, source.Operand, context.Scope, incrementDecrementOperator);
            }

            UnaryOperator? @operator = ConvertPostfixUnaryOperator(source.Operator, source);

            Expression expression = ConvertExpression(source.Operand, context);

            return (expression != null && @operator.HasValue) ? new UnaryExpression(@operator.Value, expression, Location(source)) : null;
        }

        private UnaryOperator? ConvertPostfixUnaryOperator(TypeScript.Net.Types.SyntaxKind operatorKind, IPostfixUnaryExpression source)
        {
            string message = I($"Unary postfix operator kind {operatorKind} is not supported yet. Fix me!");
            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation(), message);

            return null;
        }

        private UnaryExpression ConvertSpreadExpression(ISpreadElementExpression source, ConversionContext context)
        {
            Expression expression = ConvertExpression(source.Expression, context);

            return expression != null ? new UnaryExpression(UnaryOperator.Spread, expression, Location(source)) : null;
        }

        /// <summary>
        /// Converts <paramref name="source"/> to <see cref="ArrayLiteral"/> or to set of concat method invocations.
        /// </summary>
        private Expression ConvertArrayExpression(IArrayLiteralExpression source, ConversionContext context)
        {
            var expressions = source.Elements.Select(e => ConvertExpression(e, context)).Where(e => e != null).ToArray();

            var (spreadExpressionCount, constCount) = ComputeSpreadExpressions(expressions);

            if (constCount == expressions.Length && constCount != 0)
            {
                return ArrayLiteral.CreateEvaluated(expressions, Location(source), CurrentFileModule.Path);
            }

            // If there is no spreads, just return array literal
            if (spreadExpressionCount == 0)
            {
                return ArrayLiteral.Create(expressions, Location(source), CurrentFileModule.Path);
            }

            return new ArrayLiteralWithSpreads(expressions, spreadExpressionCount, Location(source), CurrentFileModule.Path);
        }

        private static (int spreadCount, int constCount) ComputeSpreadExpressions(IReadOnlyList<Expression> expressions)
        {
            var spreadCount = 0;
            var constCount = 0;
            for (int i = 0; i < expressions.Count; i++)
            {
                var expression = expressions[i];

                if ((expression as UnaryExpression)?.OperatorKind == UnaryOperator.Spread)
                {
                    spreadCount++;
                }
                else if (expression is IConstantExpression)
                {
                    constCount++;
                }
            }

            return (spreadCount, constCount);
        }

        private Expression ConvertCallExpression(ICallExpression source, ConversionContext context)
        {
            Expression functor = ConvertExpression(source.Expression, context);

            Expression[] arguments =
                source.Arguments.Select(a => ConvertExpression(a, context)).Where(a => a != null).ToArray();
            Type[] typeArguments = source.TypeArguments?.Select(t => ConvertType(t, context.CurrentQualifierSpaceId)).Where(t => t != null).ToArray() ??
                                         CollectionUtilities.EmptyArray<Type>();

            if (functor == null)
            {
                // Error was logged already.
                return null;
            }

            if (source.IsImportCall(out _, out var importKind, out IExpression argumentAsExpression, out _))
            {
                Expression pathSpecifier = null;
                if (importKind == DScriptImportFunctionKind.ImportFrom)
                {
                    var firstArgument = argumentAsExpression.As<IStringLiteral>();
                    if (firstArgument == null)
                    {
                        Contract.Assume(
                            false,
                            I($"Inline import method '{Constants.Names.InlineImportFunction}' takes a string literal as its first argument. {source.GetFormattedText()}"));
                    }

                    pathSpecifier = ConvertPathSpecifier(firstArgument.Text, Location(firstArgument));
                }
                else if (importKind == DScriptImportFunctionKind.ImportFile)
                {
                    // First argument to 'importFile' should be a file literal
                    (var interpolationKind, ILiteralExpression literal, _, _) = argumentAsExpression.Cast<ITaggedTemplateExpression>();

                    Contract.Assume(interpolationKind == InterpolationKind.FileInterpolation);
                    if (literal == null)
                    {
                        Contract.Assume(
                            false,
                            I($"Inline import method '{Constants.Names.InlineImportFileFunction}' takes a file literal as its first argument. {source.GetFormattedText()}"));
                    }

                    string text = literal.Text;
                    if (ImportPathHelpers.IsPackageName(literal.Text))
                    {
                        // Prefix with ./ to force 'relative-path' conversion, instead of module resolution.
                        text = "./" + text;
                    }

                    pathSpecifier = ImportPathHelpers.ResolvePath(RuntimeModelContext, CurrentSpecPath, text, Location(literal));
                }

                if (pathSpecifier == null)
                {
                    // Module resolution failed!
                    return null;
                }

                arguments[0] = pathSpecifier;

                // Semantic evaluation mode uses ImportAliasExpression for `import * as X` statement.
                // To be consistent, we're creating the same type of the expression instead of relying on ambients.
                // This helps to make code consistent and will allow to remove ambient importFrom implementation in the future.
                var importAliasExpr = CreateImportAliasExpression(pathSpecifier, Location(source));
                return importKind == DScriptImportFunctionKind.ImportFile
                    ? new ModuleToObjectLiteral(importAliasExpr)
                    : (Expression)importAliasExpr;
            }

            var selector = functor as SelectorExpressionBase;
            if (selector?.Selector == m_conversionContext.WithQualifierKeyword)
            {
                Contract.Assert(arguments.Length != 0, "withQualifier should have at least one argument.");
                var qualifierExpression = arguments[0];

                var resolvedSymbol = ResolveSymbolAtPositionAndReportWarningIfObsolete(source.Expression);
                Contract.Assert(resolvedSymbol != null);
                var targetQualifierSpaceId = ExtractSourceQualifierSpace(resolvedSymbol);

                return new WithQualifierExpression(
                    selector.ThisExpression,
                    qualifierExpression,
                    sourceQualifierSpaceId: context.CurrentQualifierSpaceId,
                    targetQualifierSpaceId: targetQualifierSpaceId,
                    location: Location(source));
            }

            return ApplyExpression.Create(functor, typeArguments, arguments, Location(source));
        }

        private bool IsObsolete(ISymbol symbol, out string message)
        {
            foreach (var declaration in symbol.DeclarationList)
            {
                if (IsObsolete(declaration, out message))
                {
                    return true;
                }
            }

            message = null;
            return false;
        }

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Instance state is used by the local function.")]
        private bool IsObsolete(INode source, out string message)
        {
            foreach (var d in source.Decorators.AsStructEnumerable())
            {
                if (IsDecoratorObsolete(d, out message))
                {
                    return true;
                }
            }

            message = null;
            return false;
        }

        private bool IsDecoratorObsolete(IDecorator decorator, out string msg)
        {
            bool result = UnwrapIdentifier(decorator.Expression)?.Text == Constants.Names.ObsoleteAttributeName;
            if (result)
            {
                var literal = decorator.Expression?.As<ICallExpression>()?.Arguments?.FirstOrDefault();
                msg = literal.As<ILiteralExpression>()?.Text;
            }
            else
            {
                msg = null;
            }

            return result;
        }

        private static IIdentifier UnwrapIdentifier(IExpression expression)
        {
            var identifier = expression.As<IIdentifier>();
            if (identifier != null)
            {
                return identifier;
            }

            var callExpression = expression.As<ICallExpression>();
            if (callExpression != null)
            {
                return UnwrapIdentifier(callExpression.Expression);
            }

            return null;
        }

        /// <summary>
        /// Converts an import expression.
        /// </summary>
        /// <returns>
        /// Returns <code>null</code> if the semantic resolution is enabled but target spec is not part of the workspace.
        /// This means that there is no references for the alias and we can freely ignore this node.
        /// </returns>
        [CanBeNull]
        private ImportAliasExpression CreateImportAliasExpression(Expression pathSpecifier, in UniversalLocation location)
        {
            var absolutePathSpecifier = pathSpecifier as PathLiteral;
            if (absolutePathSpecifier == null)
            {
                Contract.Assert(false, I($"pathSpecifier should be of PathLiteral type but was '{pathSpecifier.GetType()}'."));
            }

            if (!m_workspace.ContainsSpec(absolutePathSpecifier.Value))
            {
                throw Contract.AssertFailure(I($"Resolved path specifier '{absolutePathSpecifier.Value.ToString(RuntimeModelContext.PathTable)}' is not part of a workspace."));
            }

            return new ImportAliasExpression(absolutePathSpecifier.Value, location);
        }

        /// <summary>
        /// Converts binary expression to evaluation ast counterpart.
        /// </summary>
        private Expression ConvertBinaryExpression(IBinaryExpression source, ConversionContext context)
        {
            // Binary expression could be converted to assignment expression or to a binary expression
            if (s_syntaxKindToAssignmentOperatorMapping.ContainsKey(source.OperatorToken.Kind))
            {
                return ConvertAssignmentExpression(source, s_syntaxKindToAssignmentOperatorMapping[source.OperatorToken.Kind], context);
            }

            // For other cases, the result should be a binary expression
            var left = ConvertExpression(source.Left, context);
            var right = ConvertExpression(source.Right, context);
            var @operator = ConvertOperator(source.OperatorToken);

            return (left != null && right != null && @operator.HasValue) ? new BinaryExpression(left, @operator.Value, right, Location(source)) : null;
        }

        [CanBeNull]
        private AssignmentExpression ConvertAssignmentExpression(IBinaryExpression source, AssignmentOperator assignmentOperator, ConversionContext context)
        {
            // left hand side of the assignment should be an identifier
            var identifier = source.Left.As<IIdentifier>();
            if (identifier == null)
            {
                RuntimeModelContext.Logger.ReportLeftHandSideOfAssignmentMustBeLocalVariable(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation());
                return null;
            }

            VariableDefinition? local = ResolveLocalVariableForMutation(context.Scope, identifier.Text, source, assignment: true);
            if (local == null)
            {
                // Error already reported.
                return null;
            }

            Expression rightExpression = ConvertExpression(source.Right, context);

            return rightExpression != null ? new AssignmentExpression(local.Value.Name, local.Value.Index, assignmentOperator, rightExpression, Location(source)) : null;
        }

        [CanBeNull]
        private IncrementDecrementExpression ConvertAssignmentExpression(IUnaryExpression source, IExpression operand, FunctionScope escapes, IncrementDecrementOperator incrementDecrementOperator)
        {
            // operand should be an identifier
            var identifier = operand.As<IIdentifier>();
            if (identifier == null)
            {
                RuntimeModelContext.Logger.OperandOfIncrementOrDecrementOperatorMustBeLocalVariable(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation());
                return null;
            }

            VariableDefinition? local = ResolveLocalVariableForMutation(escapes, identifier.Text, source, assignment: false);
            if (local == null)
            {
                // Error already reported.
                return null;
            }

            return new IncrementDecrementExpression(local.Value.Name, local.Value.Index, incrementDecrementOperator, Location(source));
        }

        private VariableDefinition? ResolveLocalVariableForMutation(FunctionScope escapes, string identifier, INode source, bool assignment)
        {
            // Local resolution for mutation and for reading is different.
            // DScript allows only mutation for a current function scope.
            // It means, that all captured variables can not be changed.
            if (!SymbolAtom.TryCreate(StringTable, identifier, out SymbolAtom symbolIdentifier))
            {
                // It is possible that identifier is anot a valid symbol atom.
                // For instance, spec could have an expression like 1 += 1.
                if (assignment)
                {
                    RuntimeModelContext.Logger.ReportLeftHandSideOfAssignmentMustBeLocalVariable(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation());
                }
                else
                {
                    RuntimeModelContext.Logger.OperandOfIncrementOrDecrementOperatorMustBeLocalVariable(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation());
                }

                return null;
            }

            VariableDefinition? local = escapes.ResolveFromCurrentFunctionScope(symbolIdentifier);
            if (local?.IsConstant == false)
            {
                // This is the only successful path: found non-const variable in current function.
                return local;
            }

            // Potentially, there is no function in a local scope, but it does exists in a parent scope!
            local = local ?? escapes.ResolveFromFunctionScopeRecursivelyIncludingGlobalScope(symbolIdentifier);

            // Now, we've looked at the current, parent and global scope.
            // In each case we can emit a different error.
            if (local == null)
            {
                RuntimeModelContext.Logger.ReportNameCannotBeFound(
                    RuntimeModelContext.LoggingContext,
                    Location(source).AsLoggingLocation(), identifier);
            }
            else
            {
                if (local.Value.IsConstant)
                {
                    // We found let-variable in a parent scope! But this is not allowed as well!
                    if (assignment)
                    {
                        RuntimeModelContext.Logger.ReportLeftHandSideOfAssignmentExpressionCannotBeAConstant(
                            RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation(), identifier);
                    }
                    else
                    {
                        // this is increment, not an assignment
                        RuntimeModelContext.Logger.ReportTheOperandOfAnIncrementOrDecrementOperatorCannotBeAConstant(
                            RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation(), identifier);
                    }
                }
                else
                {
                    // We found let-variable in a parent scope! But this is not allowed as well!
                    RuntimeModelContext.Logger.ReportOuterVariableCapturingForMutationIsNotSupported(RuntimeModelContext.LoggingContext, Location(source).AsLoggingLocation(), identifier);
                }
            }

            return null;
        }

        private static Dictionary<TypeScript.Net.Types.SyntaxKind, UnaryOperator> CreateUnaryOperatorMapping()
        {
            return new Dictionary<TypeScript.Net.Types.SyntaxKind, UnaryOperator>
            {
                [TypeScript.Net.Types.SyntaxKind.ExclamationToken] = UnaryOperator.Not,
                [TypeScript.Net.Types.SyntaxKind.MinusToken] = UnaryOperator.Negative,
                [TypeScript.Net.Types.SyntaxKind.TildeToken] = UnaryOperator.BitwiseNot,
                [TypeScript.Net.Types.SyntaxKind.TypeOfKeyword] = UnaryOperator.TypeOf,
                [TypeScript.Net.Types.SyntaxKind.PlusToken] = UnaryOperator.UnaryPlus,

                // prefix and postfix increment and decrement are handled in a special way
                // [TypeScript.Net.Types.SyntaxKind.PlusPlusToken] = UnaryOperator.??,
                // [TypeScript.Net.Types.SyntaxKind.MinusMinusToken] = UnaryOperator.??,
            };
        }

        private static Dictionary<(IncrementDecrementOperator, TypeScript.Net.Types.SyntaxKind), IncrementDecrementOperator> CreateIncrementDecrementOperatorMapping()
        {
            return new Dictionary<(IncrementDecrementOperator, TypeScript.Net.Types.SyntaxKind), IncrementDecrementOperator>
            {
                [(IncrementDecrementOperator.Prefix, TypeScript.Net.Types.SyntaxKind.PlusPlusToken)] = IncrementDecrementOperator.PrefixIncrement,
                [(IncrementDecrementOperator.Prefix, TypeScript.Net.Types.SyntaxKind.MinusMinusToken)] = IncrementDecrementOperator.PrefixDecrement,
                [(IncrementDecrementOperator.Postfix, TypeScript.Net.Types.SyntaxKind.PlusPlusToken)] = IncrementDecrementOperator.PostfixIncrement,
                [(IncrementDecrementOperator.Postfix, TypeScript.Net.Types.SyntaxKind.MinusMinusToken)] = IncrementDecrementOperator.PostfixDecrement,
            };
        }

        private static Dictionary<TypeScript.Net.Types.SyntaxKind, BinaryOperator> CreateBinaryOperatorMapping()
        {
            var binaryOperators = new Dictionary<TypeScript.Net.Types.SyntaxKind, BinaryOperator>();

            // +, -, *, **
            binaryOperators[TypeScript.Net.Types.SyntaxKind.PlusToken] = BinaryOperator.Addition;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.MinusToken] = BinaryOperator.Subtraction;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.AsteriskToken] = BinaryOperator.Multiplication;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.AsteriskAsteriskToken] = BinaryOperator.Exponentiation;

            binaryOperators[TypeScript.Net.Types.SyntaxKind.PercentToken] = BinaryOperator.Remainder;

            // Division is not supported in DScript. There is a lint rule that enforces this.
            // [TypeScript.Net.Types.SyntaxKind.SlashToken] = BinaryOperator.???,

            // Bit-wise operators
            binaryOperators[TypeScript.Net.Types.SyntaxKind.CaretToken] = BinaryOperator.BitWiseXor;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.AmpersandToken] = BinaryOperator.BitWiseAnd;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.BarToken] = BinaryOperator.BitWiseOr;

            // Comparison
            binaryOperators[TypeScript.Net.Types.SyntaxKind.GreaterThanToken] = BinaryOperator.GreaterThan;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.GreaterThanEqualsToken] = BinaryOperator.GreaterThanOrEqual;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.LessThanToken] = BinaryOperator.LessThan;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.LessThanEqualsToken] = BinaryOperator.LessThanOrEqual;

            // Equality, nonequality
            binaryOperators[TypeScript.Net.Types.SyntaxKind.EqualsEqualsEqualsToken] = BinaryOperator.Equal;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.ExclamationEqualsEqualsToken] = BinaryOperator.NotEqual;

            // Boolean operators
            binaryOperators[TypeScript.Net.Types.SyntaxKind.AmpersandAmpersandToken] = BinaryOperator.And;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.BarBarToken] = BinaryOperator.Or;

            // Bit-wise operators
            binaryOperators[TypeScript.Net.Types.SyntaxKind.LessThanLessThanToken] = BinaryOperator.LeftShift;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.GreaterThanGreaterThanToken] = BinaryOperator.SignPropagatingRightShift;
            binaryOperators[TypeScript.Net.Types.SyntaxKind.GreaterThanGreaterThanGreaterThanToken] = BinaryOperator.ZeroFillingRightShift;

            return binaryOperators;
        }

        private static Dictionary<TypeScript.Net.Types.SyntaxKind, AssignmentOperator> CreateAssignmentOperatorMapping()
        {
            var assignmentOperators = new Dictionary<TypeScript.Net.Types.SyntaxKind, AssignmentOperator>
                                      {
                                          [TypeScript.Net.Types.SyntaxKind.EqualsToken] = AssignmentOperator.Assignment,
                                          [TypeScript.Net.Types.SyntaxKind.PlusEqualsToken] = AssignmentOperator.AdditionAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.MinusEqualsToken] = AssignmentOperator.SubtractionAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.AsteriskEqualsToken] = AssignmentOperator.MultiplicationAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.AsteriskAsteriskEqualsToken] = AssignmentOperator.ExponentiationAssignment,

                                          // Compound division is not supported!
                                          [TypeScript.Net.Types.SyntaxKind.PercentEqualsToken] = AssignmentOperator.RemainderAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.LessThanLessThanEqualsToken] = AssignmentOperator.LeftShiftAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.GreaterThanGreaterThanEqualsToken] = AssignmentOperator.RightShiftAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken] =
                                              AssignmentOperator.UnsignedRightShiftAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.AmpersandEqualsToken] = AssignmentOperator.BitwiseAndAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.BarEqualsToken] = AssignmentOperator.BitwiseOrAssignment,
                                          [TypeScript.Net.Types.SyntaxKind.CaretEqualsToken] = AssignmentOperator.BitwiseXorAssignment,
                                      };

            return assignmentOperators;
        }

        private BinaryOperator? ConvertOperator(INode operatorToken)
        {
            if (!s_syntaxKindToBinaryOperatorMapping.TryGetValue(operatorToken.Kind, out BinaryOperator result))
            {
                string message =
                    I($"Binary operator kind {operatorToken.Kind} is not supported. Full expression: '{operatorToken.GetFormattedText()}'.");
                RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, Location(operatorToken).AsLoggingLocation(), message);

                return null;
            }

            return result;
        }

        [CanBeNull]
        private Expression ConvertIdentifier(IIdentifier source, ConversionContext context)
        {
            if (source is SymbolAtomBasedIdentifier identifier)
            {
                return ConvertIdentifier2(identifier.Name, ResolveSymbolAtPositionAndReportWarningIfObsolete(source), Location(source), context);
            }

            return ConvertIdentifier(source.Text, ResolveSymbolAtPositionAndReportWarningIfObsolete(source), Location(source), context);
        }

        [CanBeNull]
        private ISymbol ResolveSymbolAtPositionAndReportWarningIfObsolete(INode node)
        {
            if (node.Kind == TypeScript.Net.Types.SyntaxKind.CallExpression)
            {
                node = node.Cast<ICallExpression>().Expression.ResolveUnionType();
            }

            // This functionality is applicable only when symbol-based name resolution is enabled.
            var result = m_semanticModel?.GetSymbolAtLocation(node);

            if (result != null)
            {
                if (IsObsolete(result, out var message))
                {
                    string member = result.DeclarationList[0].Name.ToString();
                    if (!string.IsNullOrEmpty(message))
                    {
                        message = " " + message;
                        if (!message.EndsWith("."))
                        {
                            message += ".";
                        }
                    }

                    RuntimeModelContext.Logger.ReportMemberIsObsolete(RuntimeModelContext.LoggingContext, node.LocationForLogging(CurrentSourceFile), member, message);
                }
            }

            return result;
        }

        [CanBeNull]
        private Expression ConvertIdentifier(string text, [CanBeNull]ISymbol identifierSymbol, in UniversalLocation location, ConversionContext context)
        {
            return ConvertIdentifier2(SymbolAtom.Create(StringTable, text), identifierSymbol, location, context);
        }

        [CanBeNull]
        private Expression ConvertIdentifier2(SymbolAtom nameAtom, [CanBeNull]ISymbol identifierSymbol, in UniversalLocation location, ConversionContext context)
        {
            // TODO: do we really need to do that?!?!?
            // Because of this check the result of this function can't be SymbolReferenceExpression!
            if (nameAtom == m_conversionContext.UndefinedLiteral)
            {
                return UndefinedLiteral.Instance;
            }

            if (nameAtom == m_conversionContext.QualifierDeclarationKeyword)
            {
                // Qualifier instance was referenced, using a different expression type for it.
                return new QualifierReferenceExpression(location);
            }

            var name = FullSymbol.Create(SymbolTable, nameAtom);

            // Even in a case when symbol is available, we need to perform additional lookups in local symbol tables,
            // because some reference expressions are still required some special information, like index in local variable storage.
            if (context.Scope.TryResolveFromFunctionScopeRecursively(nameAtom, out VariableDefinition variable))
            {
                // TODO: Move the configuration to a lint rule
                if (ShouldCheckDeclarationBeforeUse())
                {
                    var lineInfo = location.AsFilePosition();

                    // Local identifier could be defined later than current expression.
                    // Error should be emitted because locals in typescript/buildxlscipt should be defined before use
                    if (variable.LocationDefinition.AsFilePosition().Position > lineInfo.Position)
                    {
                        RuntimeModelContext.Logger.ReportBlockScopedVariableUsedBeforeDeclaration(
                            RuntimeModelContext.LoggingContext,
                            location.AsLoggingLocation(),
                            nameAtom.ToString(StringTable));
                        return null;
                    }
                }

                Contract.Assert(variable.IsLocal);

                // Variable is local and LocalReferenceExpression should be used to hold an index to the local array (frame during evaluation).
                // Even if symbol is available, need to return local reference that stores index in local variable table!
                // This is crucial for memory efficient evaluation model.
                return new LocalReferenceExpression(nameAtom, variable.Index, location);
            }

            if (ShouldCheckDeclarationBeforeUse())
            {
                var globals = context.Scope.GetGlobalScope();

                if (globals != null)
                {
                    if (globals.TryResolveRecursively(nameAtom, out variable))
                    {
                        var lineInfo = location.AsFilePosition();

                        // Enforce def-before-use.
                        if (variable.LocationDefinition.AsFilePosition().Position > lineInfo.Position)
                        {
                            RuntimeModelContext.Logger.ReportBlockScopedVariableUsedBeforeDeclaration(
                                RuntimeModelContext.LoggingContext,
                                location.AsLoggingLocation(),
                                nameAtom.ToString(StringTable));
                            return null;
                        }

                        Contract.Assert(!variable.IsLocal);
                    }
                }
            }

            // Fall back: Id may be untracked by namespaceScope, e.g., function name.
            return CreateNamespaceReferenceExpressionIfNeeded(nameAtom, identifierSymbol, location, context);
        }

        /// <summary>
        /// Function that creates a reference to a given symbol or converts an expression to a fully-qualified one if namespace boundary was crossed.
        /// </summary>
        /// <remarks>
        /// There are two cases when qualifier type should be coerced:
        /// - explicit dotted name like A.B.c
        /// - named reference that ended up in a value in a different namespace, like fooBar when fooBar declared in the enclosing namespace.
        /// First case covered in the <see cref="ConvertPropertyAccessExpression"/> and the latter case is covered here.
        /// The logic is very similar in both cases, but the structure is different so there is no code reuse between them.
        /// </remarks>
        private Expression CreateNamespaceReferenceExpressionIfNeeded(
            SymbolAtom name,
            ISymbol resolvedSymbol,
            in UniversalLocation location,
            ConversionContext context)
        {
            var currentQualifierSpaceId = context.CurrentQualifierSpaceId;

            IDeclaration symbolDeclaration = m_semanticModel?.GetFirstNotFilteredDeclarationOrDefault(resolvedSymbol);

            // Creating regular expression that points to a given symbol
            Expression referenceExpression = CreateSymbolReferenceExpression(name, resolvedSymbol, location, symbolDeclaration);
            var locationBasedReferencedExpression = referenceExpression as LocationBasedSymbolReference;

            // This function can be used in both: semantic-based mode and with legacy mode, so resolvedSymbol can be null
            if (referenceExpression != null && symbolDeclaration != null && locationBasedReferencedExpression != null)
            {
                // Target expression is location-based (i.e. semantic resolution was used)
                // and potentially qualifier space region was crossed
                // (i.e. target symbol can be located in the different qualifier space that the current one).

                // If the target is a namespace, no additional checks are needed.
                // Qualifier type coercion happens only when a value is used.
                if (!m_semanticModel.IsNamespaceType(resolvedSymbol))
                {
                    // Then need to find qualifier space for the given symbol
                    var targetQualifierSpaceId = ExtractSourceQualifierSpace(resolvedSymbol);

                    // Next we need to check that the target qualifier space id is different from the current one
                    if (targetQualifierSpaceId.IsValid && currentQualifierSpaceId.IsValid && currentQualifierSpaceId != targetQualifierSpaceId)
                    {
                        // Now, we need to find a namespace that contains the target symbol
                        // and wrap expression into property access expression.
                        // This means that an expression like 'someField' will be translated to
                        // coerceQualifierType('SomeFieldsNamespace', qualifierFor(someField)).someField
                        var declaredNamespace = FindEnclosingNamespaceName(symbolDeclaration, out string namespaceName);

                        // The namespace that we've got can be null, in this case the symbol is declared at the file level
                        // Get the file path from the referenced expression to avoid redundant computation.
                        // The path is the same for computed reference expression and for the namespace.
                        var referencedSymbolPath = locationBasedReferencedExpression.FilePosition.Path;
                        FilePosition targetPosition = new FilePosition(declaredNamespace, referencedSymbolPath);

                        if (!m_workspace.ContainsSpec(referencedSymbolPath))
                        {
                            Contract.Assert(false, I($"{referencedSymbolPath.ToString(RuntimeModelContext.PathTable)} is not part of the worksapce."));
                        }

                        var targetDeclaration = m_semanticModel.GetFirstNotFilteredDeclarationOrDefault(resolvedSymbol);
                        var targetSourceFile = targetDeclaration.GetSourceFile();
                        var targetSourceFilePath = targetSourceFile.GetAbsolutePath(RuntimeModelContext.PathTable);

                        // Creating selector expression in a form: Selector(CoerceQualifierType(namespace for a given symbol), symbol's Location))
                        return new ResolvedSelectorExpression(
                            new CoerceQualifierTypeExpression(
                                new LocationBasedSymbolReference(targetPosition, SymbolAtom.Create(StringTable, namespaceName), referenceExpression.Location, SymbolTable),
                                targetQualifierSpaceId,
                                RuntimeModelContext.FrontEndHost.ShouldUseDefaultsOnCoercion(targetSourceFilePath),
                                targetDeclaration.LineInfo(targetSourceFile),
                                location),
                            locationBasedReferencedExpression,
                            locationBasedReferencedExpression.Name,
                            locationBasedReferencedExpression.Location);
                    }
                }
            }

            return referenceExpression;
        }

        /// <summary>
        /// Returns an enclosing namespace name or a special <see cref="BuildXL.FrontEnd.Script.Constants.Names.RuntimeRootNamespaceAlias"/>.
        /// </summary>
        [CanBeNull]
        private static INode FindEnclosingNamespaceName(INode node, out string name)
        {
            INode currentNode = node.ResolveUnionType();
            while (currentNode.Kind != TypeScript.Net.Types.SyntaxKind.SourceFile)
            {
                if (currentNode.Kind == TypeScript.Net.Types.SyntaxKind.ModuleDeclaration)
                {
                    var result = (IModuleDeclaration)currentNode;
                    name = result.Name.Text;
                    return result;
                }

                currentNode = currentNode.Parent;
            }

            // Using a root namespace alias
            name = Constants.Names.RuntimeRootNamespaceAlias;
            return currentNode;
        }

        /// <summary>
        /// Returns an enclosing namespace name or a special <see cref="BuildXL.FrontEnd.Script.Constants.Names.RuntimeRootNamespaceAlias"/>.
        /// </summary>
        private static INode FindEnclosingNamespaceName(INode node)
        {
            return FindEnclosingNamespaceName(node, out string dummy);
        }

        private Expression CreateSymbolReferenceExpression(
            SymbolAtom name,
            [CanBeNull] ISymbol resolvedSymbol,
            LineInfo location)
        {
            // In this case it is save to get the first declaration of the symbol,
            // because in all cases it should be just one.
            var symbolDeclaration = m_semanticModel.GetFirstNotFilteredDeclarationOrDefault(resolvedSymbol);
            return CreateSymbolReferenceExpression(name, resolvedSymbol, location, symbolDeclaration);
        }

        private Expression CreateSymbolReferenceExpression(
            SymbolAtom name,
            [CanBeNull] ISymbol resolvedSymbol,
            LineInfo location,
            IDeclaration symbolDeclaration)
        {
            if (resolvedSymbol == null)
            {
                return new NameBasedSymbolReference(name, location);
            }

            if (symbolDeclaration == null)
            {
                // symbol declaration can be null if the target spec was filtered out.
                // For instance, if the target symbol is a namespace and there was just a namespace reference
                // but with no usages, it is absolutely possible to filter all the files with that namespace out.
                // In this case the result is a fake namespace.
                return TypeOrNamespaceModuleLiteral.EmptyInstance;
            }

            // Symbol could belong to another file.
            var specPath = symbolDeclaration.GetSourceFile().GetAbsolutePath(RuntimeModelContext.PathTable);

            // If the specPath is contained in the Prelude, then this needs a special treatment since the prelude
            // is not registered as a module literal, and therefore a location based symbol won't work.
            // We fall back to the standard name based reference, so for example ambient calls can be properly resolved.
            if (m_prelude?.Specs.ContainsKey(specPath) == true)
            {
                // Switching back to V1 evaluation semantic.
                // For different kinds of nodes different expressions are needed.
                // For namespaces and enums, ModuleIdExpression is needed
                // and for other things, like functions or constants - NameBasedSymbolReferences should be used.
                // This is required, because different stages are required for different types of symbols in V1 evaluation mode
                if ((resolvedSymbol.Flags & SymbolFlags.Namespace) == SymbolFlags.Namespace
                    || (resolvedSymbol.Flags & SymbolFlags.ValueModule) == SymbolFlags.ValueModule
                    || (resolvedSymbol.Flags & SymbolFlags.ConstEnum) == SymbolFlags.ConstEnum)
                {
                    var fullName = FullSymbol.Create(SymbolTable, name);
                    return new ModuleIdExpression(fullName, location);
                }

                return new NameBasedSymbolReference(name, location);
            }

            if (!m_workspace.ContainsSpec(specPath))
            {
                Contract.Assert(false, I($"{specPath.ToString(RuntimeModelContext.PathTable)} is not part of the worksapce."));
            }

            FilePosition filePosition = new FilePosition(symbolDeclaration, specPath);
            return new LocationBasedSymbolReference(filePosition, name, location, SymbolTable);
        }

        private bool ShouldCheckDeclarationBeforeUse()
        {
            return !m_conversionConfiguration.UnsafeOptions.DisableDeclarationBeforeUseCheck;
        }

        private UniversalLocation Location(INode node)
        {
            return node.Location(CurrentSourceFile, CurrentSpecPath, RuntimeModelContext.PathTable);
        }

        [CanBeNull]
        private Expression ConvertLiteral(ILiteralExpression literal, bool isNegative)
        {
            var location = Location(literal);

            if (literal is StringIdTemplateLiteralFragment stringIdLiteral)
            {
                return new StringIdLiteral(stringIdLiteral.TextAsStringId, location);
            }

            string text = isNegative ? ($"-{literal.Text}") : literal.Text;
            
            switch (literal.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.NumericLiteral:
                    return new NumberLiteral(literal.AsNumber(isNegative), location);

                case TypeScript.Net.Types.SyntaxKind.StringLiteral:
                    return ConvertStringLiteral(text, location);

                case TypeScript.Net.Types.SyntaxKind.NoSubstitutionTemplateLiteral:
                    return ConvertStringLiteral(text, location);
            }

            string message = I($"Literal kind '{literal.Kind}' appeared in expression '{literal.GetFormattedText()}' is not supported.");
            RuntimeModelContext.Logger.ReportTypeScriptFeatureIsNotSupported(RuntimeModelContext.LoggingContext, location.AsLoggingLocation(), message);
            return null;
        }

        private static Expression ConvertStringLiteral(string text, in UniversalLocation location)
        {
            return LiteralConverter.ConvertStringLiteral(text, location);
        }

        /// <summary>
        /// Context required for expressions conversion.
        /// </summary>
        private readonly struct ConversionContext
        {
            [NotNull]
            public FunctionScope Scope { get; }

            public QualifierSpaceId CurrentQualifierSpaceId { get; }

            public ConversionContext(FunctionScope scope, QualifierSpaceId currentQualifierSpaceId)
                : this()
            {
                Scope = scope;
                CurrentQualifierSpaceId = currentQualifierSpaceId;
            }

            public ConversionContext WithScope(FunctionScope nestedEscapes)
            {
                return new ConversionContext(nestedEscapes, CurrentQualifierSpaceId);
            }
        }
    }
}
