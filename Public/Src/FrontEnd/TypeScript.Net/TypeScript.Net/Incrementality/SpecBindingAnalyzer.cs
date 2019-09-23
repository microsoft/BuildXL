// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Incrementality
{
    /// <summary>
    /// Declaration flags used for binding fingerprint computation.
    /// </summary>
    [Flags]
    public enum DeclarationFlags : byte
    {
        /// <summary>
        /// No Modifier
        /// </summary>
        None = 0x0,

        /// <summary>
        /// Export Modifier
        /// </summary>
        Export = 1 << 0,

        /// <summary>
        /// Ambient Modifier
        /// </summary>
        Ambient = 1 << 1,

        /// <summary>
        /// Member is publicly exposed from the module.
        /// </summary>
        Public = 1 << 2,

        /// <summary>
        /// Declaration is marked with Obsolete annotation.
        /// </summary>
        Obsolete = 1 << 3,

        /// <summary>
        /// Member is optional (applicable for interface members or parameters).
        /// </summary>
        Optional = 1 << 4,
    }

    internal static class DeclarationFlagsExtensions
    {
        [NotNull]
        public static string ToDisplayModifier(this DeclarationFlags flags)
        {
            string result = string.Empty;
            if ((flags & DeclarationFlags.Public) == DeclarationFlags.Public)
            {
                result += "public";
            }
            else if ((flags & DeclarationFlags.Export) == DeclarationFlags.Export)
            {
                result += "internal";
            }

            if ((flags & DeclarationFlags.Obsolete) == DeclarationFlags.Obsolete)
            {
                if (string.IsNullOrEmpty(result))
                {
                    result += "obsolete";
                }
                else
                {
                    result += ".obsolete";
                }
            }

            return result;
        }

        [NotNull]
        public static string GetModifierAsSuffix(this DeclarationFlags flags)
        {
            var modifier = flags.ToDisplayModifier();

            if (!string.IsNullOrEmpty(modifier))
            {
                return string.Concat(".", modifier);
            }

            return modifier;
        }
    }

    /// <summary>
    /// Analyzes a given file to compute a binding fingerprint for it.
    /// </summary>
    /// <remarks>
    /// This class is very similar to AstConverter and does manual ast traversal.
    /// </remarks>
    internal sealed class SpecBindingAnalyzer
    {
        // File under analysis
        private readonly ISourceFile m_sourceFile;

        [NotNull]
        private readonly BuildXLWriter m_referencedSymbolsWriter;

        [NotNull]
        private readonly BuildXLWriter m_declaredSymbolsWriter;

        // The stack of lexical scopes required for the full name computation of the symbol.
        private readonly NameTracker m_currentLocationStack = new NameTracker(NameTracker.DefaultCapacity);

        /// <summary>
        /// Set of symbols referenced by the file.
        /// </summary>
        private readonly HashSet<InteractionSymbol> m_referencedSymbols;

        /// <summary>
        /// Set of symbols declared in the file.
        /// </summary>
        private readonly HashSet<InteractionSymbol> m_declaredSymbols;

        private readonly bool m_keepSymbols;

        /// <nodoc />
        public SpecBindingAnalyzer([NotNull]ISourceFile sourceFile, [NotNull]BuildXLWriter referencedSymbolsWriter, [NotNull]BuildXLWriter declaredSymbolsWriter, bool keepSymbols)
        {
            m_sourceFile = sourceFile;
            m_referencedSymbolsWriter = referencedSymbolsWriter;
            m_declaredSymbolsWriter = declaredSymbolsWriter;

            if (keepSymbols)
            {
                m_referencedSymbols = new HashSet<InteractionSymbol>();
                m_declaredSymbols = new HashSet<InteractionSymbol>();
            }

            m_keepSymbols = keepSymbols;
        }

        /// <summary>
        /// Computes the fingerprint of a given file.
        /// </summary>
        public void ComputeFingerprint()
        {
            AnalyzeDeclarationStatements(m_sourceFile.Statements);
        }

        [CanBeNull]
        public IReadOnlySet<InteractionSymbol> DeclaredSymbols => m_declaredSymbols?.ToReadOnlySet();

        [CanBeNull]
        public IReadOnlySet<InteractionSymbol> ReferencedSymbols => m_referencedSymbols?.ToReadOnlySet();

        private void AnalyzeDeclarationStatements(NodeArray<IStatement> statements)
        {
            int idx = 0;
            foreach (var statement in statements.AsStructEnumerable())
            {
                AnalyzeDeclarationStatement(statement, idx);
                idx++;
            }
        }

        private void AnalyzeDeclarationStatement(IStatement source, int idx)
        {
            // We don't need to analyze injected declarations (e.g. withQualifier or qualifier declarations)
            if (source.IsInjectedForDScript())
            {
                return;
            }

            switch (source.Kind)
            {
                case SyntaxKind.InterfaceDeclaration:
                    AnalyzeInterfaceDeclaration(source.Cast<IInterfaceDeclaration>());
                    break;
                case SyntaxKind.ImportDeclaration:
                    AnalyzeImportDeclaration(source.Cast<IImportDeclaration>());
                    break;
                case SyntaxKind.ExportDeclaration:
                    AnalyzeExportDeclaration(source.Cast<IExportDeclaration>());
                    break;
                case SyntaxKind.ModuleDeclaration:
                    AnalyzeNamespaceDeclaration(source.Cast<IModuleDeclaration>());
                    break;
                case SyntaxKind.VariableStatement:
                    AnalyzeTopLevelVariableDeclarations(
                        source.Cast<IVariableStatement>().DeclarationList,
                        GetModifiers(source.Cast<IVariableStatement>()),
                        idx);
                    break;

                case SyntaxKind.FunctionDeclaration:
                    AnalyzeFunctionDeclaration(source.Cast<IFunctionDeclaration>());
                    break;
                case SyntaxKind.EnumDeclaration:
                    AnalyzeEnumDeclaration(source.Cast<IEnumDeclaration>());
                    break;
                case SyntaxKind.TypeAliasDeclaration:
                    AnalyzeTypeAlias(source.Cast<ITypeAliasDeclaration>());
                    break;
            }
        }

        private void AnalyzeTypeAlias(ITypeAliasDeclaration source)
        {
            var flags = GetModifiers(source);
            AddOrCreateDeclarationSymbol(SymbolKind.TypeDeclaration, source.Name.Text, flags);

            AnalyzeDecorators(source);
            AnalyzeTypeParameters(source.TypeParameters);
            AnalyzeTypeReference(source.Type);
        }

        private void AnalyzeEnumDeclaration(IEnumDeclaration source)
        {
            var flags = GetModifiers(source);
            AddOrCreateDeclarationSymbol(SymbolKind.EnumDeclaration, source.Name.Text, flags);

            AnalyzeDecorators(source);

            using (m_currentLocationStack.AutoPush(source.Name.Text))
            {
                foreach (var member in source.Members.AsStructEnumerable())
                {
                    var memberFlags = GetModifiers(member);
                    AddOrCreateDeclarationSymbol(SymbolKind.EnumValueDeclaration, member.Name.Text, memberFlags);

                    AnalyzeDecorators(member);

                    // Technically, enum initializer could have a reference,
                    // but this is not supported in DScript.
                }
            }
        }

        private void AnalyzeFunctionDeclaration(IFunctionDeclaration source)
        {
            var modifiers = GetModifiers(source);
            AddOrCreateDeclarationSymbol(SymbolKind.FunctionDeclaration, source.Name.Text, modifiers);

            AnalyzeDecorators(source);

            using (m_currentLocationStack.AutoPush(source.Name.Text))
            {
                AnalyzeCallSignature(source);

                if (source.Body != null)
                {
                    AnalyzeStatements(source.Body.Statements);
                }
            }
        }

        private void AnalyzeCallSignature(ISignatureDeclaration source)
        {
            AnalyzeTypeParameters(source.TypeParameters);
            AnalyzeParameters(source.Parameters);
            AnalyzeTypeReference(source.Type);
        }

        private void AnalyzeStatements([CanBeNull]NodeArray<IStatement> statements)
        {
            int i = 0;
            foreach (var statement in statements.AsStructEnumerable())
            {
                AnalyzeStatement(statement, i);
                i++;
            }
        }

        private void AnalyzeStatement([CanBeNull]IStatement statement, int idx)
        {
            if (statement == null)
            {
                return;
            }

            switch (statement.Kind)
            {
                case SyntaxKind.VariableStatement:
                    AnalyzeVariableStatement(statement.Cast<IVariableStatement>().DeclarationList, idx);
                    break;
                case SyntaxKind.FunctionDeclaration:
                    AnalyzeLocalFunctionDeclaration(statement.Cast<IFunctionDeclaration>(), idx);
                    break;
                case SyntaxKind.IfStatement:
                    AnalyzeIfStatement(statement.Cast<IIfStatement>(), idx);
                    break;
                case SyntaxKind.ReturnStatement:
                    AnalyzeReturnStatement(statement.Cast<IReturnStatement>(), idx);
                    break;
                case SyntaxKind.SwitchStatement:
                    AnalyzeSwitchStatement(statement.Cast<ISwitchStatement>(), idx);
                    break;
                case SyntaxKind.Block:
                    AnalyzeBlock(statement.Cast<IBlock>());
                    break;
                case SyntaxKind.ForOfStatement:
                    AnalyzeForOfStatement(statement.Cast<IForOfStatement>(), idx);
                    break;
                case SyntaxKind.ForInStatement:
                    AnalyzeForInStatement(statement.Cast<IForInStatement>(), idx);
                    break;
                case SyntaxKind.ForStatement:
                    AnalyzeForStatement(statement.Cast<IForStatement>(), idx);
                    break;
                case SyntaxKind.WhileStatement:
                    AnalyzeWhileStatement(statement.Cast<IWhileStatement>(), idx);
                    break;

                case SyntaxKind.ExpressionStatement:
                    AnalyzeExpressionStatement(statement.Cast<IExpressionStatement>(), idx);
                    break;
            }
        }

        private void AnalyzeVariableStatement([CanBeNull]IVariableDeclarationList source, int idx)
        {
            if (source != null)
            {
                foreach (var declaration in source.Declarations.AsStructEnumerable())
                {
                    AddOrCreateReferencedSymbol(SymbolKind.VariableDeclaration, declaration.Name.GetText());
                    AnalyzeExpression(declaration.Initializer, idx);
                }
            }
        }

        private static void AnalyzeLocalFunctionDeclaration(IFunctionDeclaration source, int idx)
        {
            // Local functions are not supported
        }

        private void AnalyzeIfStatement(IIfStatement source, int idx)
        {
            AnalyzeExpression(source.Expression, idx);
            AnalyzeStatement(source.ThenStatement, idx);
            AnalyzeStatement(source.ElseStatement.ValueOrDefault, idx);
        }

        private void AnalyzeReturnStatement(IReturnStatement source, int idx)
        {
            AnalyzeExpression(source.Expression, idx);
        }

        private void AnalyzeSwitchStatement(ISwitchStatement source, int idx)
        {
            // Switch statement is a block. Need to introduce the faked scope.
            using (m_currentLocationStack.AutoPush("__switch__" + idx.ToString()))
            {
                AnalyzeExpression(source.Expression, idx);

                foreach (var caseClause in source.CaseBlock.GetCaseClauses())
                {
                    AnalyzeExpression(caseClause.Expression, idx);

                    AnalyzeStatements(caseClause.Statements);
                }

                AnalyzeStatements(source.CaseBlock.GetDefaultClause()?.Statements);
            }
        }

        private void AnalyzeBlock(IBlock source)
        {
            AnalyzeStatements(source.Statements);
        }

        private void AnalyzeForOfStatement(IForOfStatement source, int idx)
        {
            using (m_currentLocationStack.AutoPush("__for_of__" + idx.ToString()))
            {
                AnalyzeVariableStatement(source.Initializer.AsVariableDeclarationList(), idx);
                AnalyzeExpression(source.Expression, idx);
                AnalyzeStatement(source.Statement, idx);
            }
        }

        private void AnalyzeForInStatement(IForInStatement source, int idx)
        {
            using (m_currentLocationStack.AutoPush("__for_in__" + idx.ToString()))
            {
                AnalyzeVariableStatement(source.Initializer.AsVariableDeclarationList(), idx);
                AnalyzeExpression(source.Expression, idx);
                AnalyzeStatement(source.Statement, idx);
            }
        }

        private void AnalyzeForStatement(IForStatement source, int idx)
        {
            using (m_currentLocationStack.AutoPush("__for__" + idx.ToString()))
            {
                AnalyzeVariableStatement(source.Initializer?.AsVariableDeclarationList(), idx);
                AnalyzeExpression(source.Condition, idx);
                AnalyzeExpression(source.Incrementor, idx);
                AnalyzeStatement(source.Statement, idx);
            }
        }

        private void AnalyzeWhileStatement(IWhileStatement source, int idx)
        {
            using (m_currentLocationStack.AutoPush("__while__" + idx.ToString()))
            {
                AnalyzeExpression(source.Expression, idx);
                AnalyzeStatement(source.Statement, idx);
            }
        }

        private void AnalyzeExpressionStatement(IExpressionStatement source, int idx)
        {
            AnalyzeExpression(source.Expression, idx);
        }

        /// <summary>
        /// Analyze the expression to compute references being used by the current spec file.
        /// </summary>
        /// <remarks>
        /// If the <paramref name="saveIdentifierOnStack"/> is true, the function will push the name of the expression
        /// to the current name stack.
        /// Consider the following cases:
        /// <code>x(foo).y.z</code>
        /// In this case the expected symbol should be 'x.y.z'. To do that, the <see cref="ICallExpression"/>
        /// and <see cref="IPropertyAccessExpression"/> should be analyzed in t he different way:
        /// This function pushes values computed by the left hand side of the expression and
        /// only after that calls the analysis for the right hand side.
        /// This means that this function should push elements for composite expressions and pop them back.
        /// That's why for those two cases this function is called recursively for the left hand side
        /// with <paramref name="saveIdentifierOnStack"/> argument equals to true.
        /// </remarks>
        private void AnalyzeExpression([CanBeNull]IExpression expression, int idx, bool saveIdentifierOnStack = false)
        {
            if (expression == null)
            {
                return;
            }

            switch (expression.Kind)
            {
                case SyntaxKind.ExpressionStatement:
                {
                    AnalyzeExpression(expression.Cast<IExpressionStatement>().Expression, idx);
                    break;
                }

                case SyntaxKind.Identifier:
                {
                    // Identifier may be pushed to the stack in case of a composite expressions like 'a.b'.
                    AnalyzeIdentifier(expression.Cast<IIdentifier>(), saveIdentifierOnStack: saveIdentifierOnStack);
                    break;
                }

                case SyntaxKind.BinaryExpression:
                {
                    var node = expression.Cast<IBinaryExpression>();
                    AnalyzeExpression(node.Left, idx);
                    AnalyzeExpression(node.Right, idx);
                    break;
                }

                case SyntaxKind.CallExpression:
                {
                    var node = expression.Cast<ICallExpression>();

                    if (node.IsImportFrom())
                    {
                        AnalyzeModuleFromImportFrom(node, saveIdentifierOnStack);
                        break;
                    }

                    // This is a regular function call.
                    foreach (var p in node.Arguments)
                    {
                        AnalyzeExpression(p, idx);
                    }

                    AnalyzeTypeArguments(node.TypeArguments);

                    // If the caller specified that the identifier needs to be saved on stack, then doing that,
                    // but if not, the call expression is a standalone one, and this function needs to restore
                    // the stack state to the original one.
                    using (m_currentLocationStack.PreserveLength(restoreOriginalSize: !saveIdentifierOnStack))
                    {
                        AnalyzeExpression(node.Expression, idx, saveIdentifierOnStack: saveIdentifierOnStack);
                    }

                    break;
                }

                case SyntaxKind.ArrayLiteralExpression:
                {
                    var node = expression.Cast<IArrayLiteralExpression>();
                    foreach (var e in node.Elements.AsStructEnumerable())
                    {
                        AnalyzeExpression(e, idx);
                    }

                    break;
                }

                case SyntaxKind.PropertyAccessExpression:
                {
                    // If the caller specified that the identifier needs to be saved on stack, then doing that,
                    // but if not, the expression is a standalone one, and this function needs to restore
                    // the stack state to the original one.
                    using (m_currentLocationStack.PreserveLength(restoreOriginalSize: !saveIdentifierOnStack))
                    {
                        var node = expression.Cast<IPropertyAccessExpression>();
                        AnalyzeExpression(node.Expression, idx, saveIdentifierOnStack: true);

                        AnalyzeIdentifier(node.Name, saveIdentifierOnStack: saveIdentifierOnStack);
                    }

                    break;
                }

                case SyntaxKind.ElementAccessExpression:
                {
                    var node = expression.Cast<IElementAccessExpression>();
                    AnalyzeExpression(node.Expression, idx);
                    AnalyzeExpression(node.ArgumentExpression, idx);
                    break;
                }

                case SyntaxKind.SpreadElementExpression:
                {
                    AnalyzeExpression(expression.Cast<ISpreadElementExpression>().Expression, idx);
                    break;
                }

                case SyntaxKind.PrefixUnaryExpression:
                {
                    AnalyzeExpression(expression.Cast<IPrefixUnaryExpression>().Operand, idx);
                    break;
                }

                case SyntaxKind.PostfixUnaryExpression:
                {
                    AnalyzeExpression(expression.Cast<IPostfixUnaryExpression>().Operand, idx);
                    break;
                }

                case SyntaxKind.ParenthesizedExpression:
                {
                    AnalyzeExpression(expression.Cast<IParenthesizedExpression>().Expression, idx);
                    break;
                }

                case SyntaxKind.ConditionalExpression:
                {
                    var node = expression.Cast<IConditionalExpression>();
                    AnalyzeExpression(node.Condition, idx);
                    AnalyzeExpression(node.WhenTrue, idx);
                    AnalyzeExpression(node.WhenFalse, idx);
                    break;
                }

                case SyntaxKind.SwitchExpression:
                {
                    var node = expression.Cast<ISwitchExpression>();
                    AnalyzeExpression(node.Expression, idx);
                    foreach (var clause in node.Clauses) {
                        AnalyzeExpression(clause, idx);
                    }
                    break;
                }

                case SyntaxKind.SwitchExpressionClause:
                {
                    var node = expression.Cast<ISwitchExpressionClause>();
                    if (!node.IsDefaultFallthrough)
                    {
                        AnalyzeExpression(node.Match, idx);
                    }

                    AnalyzeExpression(node.Expression, idx);
                    break;
                }

                case SyntaxKind.TypeAssertionExpression:
                {
                    var node = expression.Cast<ITypeAssertion>();
                    AnalyzeExpression(node.Expression, idx);
                    AnalyzeTypeReference(node.Type);
                    break;
                }

                case SyntaxKind.AsExpression:
                {
                    var node = expression.Cast<IAsExpression>();
                    AnalyzeExpression(node.Expression, idx);
                    AnalyzeTypeReference(node.Type);
                    break;
                }

                case SyntaxKind.ObjectLiteralExpression:
                {
                    var node = expression.Cast<IObjectLiteralExpression>();
                    foreach (var prop in node.Properties.AsStructEnumerable())
                    {
                        AnalyzeObjectLiteralElement(prop, idx);
                    }

                    break;
                }

                case SyntaxKind.ArrowFunction:
                {
                    AnalyzeArrowFunction(expression.Cast<IArrowFunction>(), idx);
                    break;
                }

                case SyntaxKind.TaggedTemplateExpression:
                {
                    // We don't need to analyze the name of the tagged function like 'p', 'd', 'f' etc
                    // because the only place where this is located is prelude. And we'll keep prelude in every run.
                    var node = expression.Cast<ITaggedTemplateExpression>();
                    var template = node.TemplateExpression.As<ITemplateExpression>();
                    if (template != null)
                    {
                        AnalyzeTemplateExpression(template, idx);
                    }

                    break;
                }

                case SyntaxKind.TemplateExpression:
                {
                    AnalyzeTemplateExpression(expression.Cast<ITemplateExpression>(), idx);
                    break;
                }

                case SyntaxKind.TypeOfExpression:
                {
                    AnalyzeExpression(expression.Cast<ITypeOfExpression>().Expression, idx);
                    break;
                }
            }
        }

        private void AnalyzeTemplateExpression(ITemplateExpression source, int idx)
        {
            foreach (var t in source.Cast<ITemplateExpression>().TemplateSpans.AsStructEnumerable())
            {
                AnalyzeExpression(t.Expression, idx);
            }
        }

        private void AnalyzeObjectLiteralElement(IObjectLiteralElement source, int idx)
        {
            // Skipping property name itself.
            if (source.Kind == SyntaxKind.PropertyAssignment)
            {
                // Don't need to push identifier into the stack, because it is not the left hand side of the expression
                AnalyzeExpression(source.Cast<IPropertyAssignment>().Initializer, idx, saveIdentifierOnStack: false);
            }
            else if (source.Kind == SyntaxKind.ShorthandPropertyAssignment)
            {
                var shortHand = source.Cast<IShorthandPropertyAssignment>();
                AddOrCreateReferencedSymbol(SymbolKind.Reference, shortHand.Name.Text);
            }
        }

        private void AnalyzeIdentifier(IIdentifier source, bool saveIdentifierOnStack = false)
        {
            if (source.Text == "undefined")
            {
                return;
            }

            if (saveIdentifierOnStack)
            {
                m_currentLocationStack.Push(source.Text);
            }
            else
            {
                AddOrCreateReferencedSymbol(SymbolKind.Reference, source.Text);
            }
        }

        private void AnalyzeModuleFromImportFrom(ICallExpression node, bool saveIdentifierOnStack = false)
        {
            var moduleName = GetModuleNameFromImportFrom(node);
            if (!string.IsNullOrEmpty(moduleName))
            {
                string fullNameString = string.Concat(Names.InlineImportFunction, ".", moduleName);

                if (saveIdentifierOnStack)
                {
                    m_currentLocationStack.Push(fullNameString);
                }
                else
                {
                    AddOrCreateInteractionSymbol(SymbolKind.ImportedModule, fullNameString);
                }
            }
        }

        private void AnalyzeArrowFunction(IArrowFunction source, int idx)
        {
            AnalyzeCallSignature(source);

            using (m_currentLocationStack.AutoPush("__fn__" + idx.ToString()))
            {
                var block = source.Body.Block();
                if (block != null)
                {
                    AnalyzeStatements(block.Statements);
                }
                else
                {
                    AnalyzeExpression(source.Body.Expression(), idx);
                }
            }
        }

        private void AnalyzeTopLevelVariableDeclarations(IVariableDeclarationList source, DeclarationFlags modifiers, int idx)
        {
            foreach (var declaration in source.Declarations.AsStructEnumerable())
            {
                if (modifiers == DeclarationFlags.None)
                {
                    AddOrCreateReferencedSymbol(SymbolKind.VariableDeclaration, declaration.Name.GetText(), modifiers);
                }
                else
                {
                    AddOrCreateDeclarationSymbol(SymbolKind.VariableDeclaration, declaration.Name.GetText(), modifiers);
                }

                if (declaration.Initializer != null)
                {
                    AnalyzeExpression(declaration.Initializer, idx, saveIdentifierOnStack: false);
                }

                AnalyzeTypeReference(declaration.Type);
            }
        }

        private void AnalyzeNamespaceDeclaration(IModuleDeclaration source)
        {
            // Namespace name could be chained together in a form of 'namespace A.B'
            // in this case we need to get full name, not just source.Name.Text
            string fullName = string.Join(".", source.GetFullName());

            AddOrCreateDeclarationSymbol(SymbolKind.NamespaceDeclaration, fullName);

            using (m_currentLocationStack.AutoPush(fullName))
            {
                AnalyzeDeclarationStatements(source.GetModuleBlock().Statements);
            }
        }

        private void AnalyzeExportDeclaration(IExportDeclaration source)
        {
            // Checking for 'export * from "moduleName";'
            AnalyzeModuleSpecifier(source.ModuleSpecifier);

            if (source.ExportClause != null)
            {
                // Checking for 'export {foo};'
                foreach (var specifier in source.ExportClause.Elements.AsStructEnumerable())
                {
                    AnalyzeImportSpecifier(specifier);
                }
            }
        }

        private void AnalyzeImportDeclaration(IImportDeclaration source)
        {
            if (source.ModuleSpecifier != null)
            {
                AnalyzeModuleSpecifier(source.ModuleSpecifier);
            }

            if (source.ImportClause != null)
            {
                AnalyzeImportClause(source.ImportClause);
            }
        }

        private void AnalyzeModuleSpecifier(IExpression moduleSpecifier)
        {
            // Cannot safely cast to ILiteralExpression, because TS typecheck has not occurred yet.
            var literalExpression = moduleSpecifier?.TryCast<ILiteralExpression>();
            if (literalExpression != null)
            {
                AddOrCreateInteractionSymbol(SymbolKind.ImportedModule, literalExpression.Text);
            }
        }

        private static string GetModuleNameFromImportFrom(ICallExpression node)
        {
            return node.Arguments.First().TryCast<ILiteralExpression>()?.Text;
        }

        private void AnalyzeImportClause(IImportClause importClause)
        {
            if (importClause.NamedBindings != null)
            {
                INamespaceImport namespaceImport = importClause.NamedBindings.As<INamespaceImport>();
                if (namespaceImport != null)
                {
                    // import * as blah from 'name';
                    AddOrCreateReferencedSymbol(SymbolKind.ImportAlias, namespaceImport.Name.Text);
                }
                else
                {
                    // import { IdentifierList } from 'name'
                    INamedImports namedImports = importClause.NamedBindings.As<INamedImports>();
                    if (namedImports != null)
                    {
                        foreach (var specifier in namedImports.Elements.AsStructEnumerable())
                        {
                            AnalyzeImportSpecifier(specifier);
                        }
                    }
                }
            }
        }

        private void AnalyzeImportSpecifier(IImportOrExportSpecifier import)
        {
            // Checking 'import {x as y}'..
            if (import.Name != null)
            {
                // Checking 'x'
                AddOrCreateInteractionSymbol(SymbolKind.ImportAlias, import.Name.Text);
            }

            if (import.PropertyName != null)
            {
                // Checking 'y'
                AddOrCreateInteractionSymbol(SymbolKind.ImportAlias, import.PropertyName.Text);
            }
        }

        private void AnalyzeInterfaceDeclaration(IInterfaceDeclaration source)
        {
            var flags = GetModifiers(source);
            AddOrCreateDeclarationSymbol(SymbolKind.InterfaceDeclaration, source.Name.Text, flags);

            AnalyzeDecorators(source);

            using (m_currentLocationStack.AutoPush(source.Name.Text))
            {
                // Base types
                AnalyzeHeritageClause(source.HeritageClauses.Elements);

                // Members
                AnalyzeInterfaceMembers(source.Members, registerPropertyNames: true);

                // Type parameters
                AnalyzeTypeParameters(source.TypeParameters);
            }
        }

        private void AnalyzeInterfaceMembers(NodeArray<ITypeElement> elements, bool registerPropertyNames)
        {
            foreach (var interfaceMember in elements.AsStructEnumerable())
            {
                var property = interfaceMember.As<IPropertySignature>();
                if (property != null)
                {
                    if (registerPropertyNames)
                    {
                        // Registering property name
                        bool isOptional = property.QuestionToken.HasValue;
                        DeclarationFlags modifiers = GetModifiers(property, isOptional);
                        AddOrCreateDeclarationSymbol(SymbolKind.InterfaceMemberDeclaration, property.Name.Text, modifiers);
                    }

                    // Registering property type
                    AnalyzeTypeReference(property.Type);

                    AnalyzeDecorators(interfaceMember);
                }

                var method = interfaceMember.As<IMethodSignature>();
                if (method != null)
                {
                    if (registerPropertyNames)
                    {
                        // Registering method name
                        var typeElement = method.TryCast<ITypeElement>();
                        if (typeElement != null)
                        {
                            bool isOptional = typeElement.QuestionToken.HasValue;
                            DeclarationFlags modifiers = GetModifiers(property, isOptional);
                            AddOrCreateDeclarationSymbol(SymbolKind.InterfaceMemberDeclaration, method.Name.Text, modifiers);
                        }
                    }

                    AnalyzeTypeParameters(method.TypeParameters);

                    AnalyzeDecorators(method);
                }
            }
        }

        private void AnalyzeTypeParameters(NodeArray<ITypeParameterDeclaration> typeParameters)
        {
            foreach (var t in typeParameters.AsStructEnumerable())
            {
                // Parameters are not part of the declaration fingerprint.
                AddOrCreateReferencedSymbol(SymbolKind.TypeParameter, t.Name.Text);
                AnalyzeTypeReference(t.Constraint);
            }
        }

        private void AnalyzeHeritageClause(IReadOnlyList<IHeritageClause> elements)
        {
            foreach (var heritageClause in elements.AsStructEnumerable())
            {
                foreach (var type in heritageClause.Types)
                {
                    // Registering base type itself
                    string typeName = GetBaseTypeName(type);
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        AddOrCreateReferencedSymbol(SymbolKind.TypeReference, typeName);
                    }

                    // Registering type arguments
                    AnalyzeTypeArguments(type.TypeArguments);
                }
            }
        }

        private void AnalyzeTypeReference([CanBeNull]ITypeNode type)
        {
            if (type == null || type.IsPredefinedType())
            {
                return;
            }

            // Checking for named types like:
            // let n: CustomInterface = undefined;
            //        ~~~~~~~~~~~~~~~
            var typeReferenceNode = type.As<ITypeReferenceNode>();
            if (typeReferenceNode != null)
            {
                EntityName entityName = typeReferenceNode.TypeName;
                AnalyzeTypeReference(entityName);

                AnalyzeTypeArguments(typeReferenceNode.TypeArguments);
            }

            // Checking for typed literals like:
            // let n: {x: number, y: number} = {x: 1, y: 3};
            //        ~~~~~~~~~~~~~~~~~~~~~~
            var typeLiteralNode = type.As<ITypeLiteralNode>();
            if (typeLiteralNode != null)
            {
                // Don't need to register names, because it is not a real interface declaration.
                AnalyzeInterfaceMembers(typeLiteralNode.Members, registerPropertyNames: false);
            }

            // Check for array types like:
            // let n: number[] = [1,2];
            //        ~~~~~~~~
            var arrayTypeNode = type.As<IArrayTypeNode>();
            if (arrayTypeNode != null)
            {
                AnalyzeTypeReference(arrayTypeNode.ElementType);
            }

            // Check for function types like:
            // function foo(): () => number {}
            //                 ~~~~~~~~~~~~
            var functionType = type.As<IFunctionOrConstructorTypeNode>();
            if (functionType != null)
            {
                AnalyzeParameters(functionType.Parameters);
                AnalyzeTypeReference(functionType.Type);
                AnalyzeTypeParameters(functionType.TypeParameters);
            }

            // Check for parenthesized types like:
            // function foo(): (() => number)[] {return [];}
            //                 ~~~~~~~~~~~~~~~~
            var parenthesizedType = type.As<IParenthesizedTypeNode>();
            if (parenthesizedType != null)
            {
                AnalyzeTypeReference(parenthesizedType.Type);
            }

            // Checking for union types like:
            // type X = string | number;
            //          ~~~~~~~~~~~~~~~
            var unionType = type.As<IUnionTypeNode>();
            if (unionType != null)
            {
                foreach (var t in unionType.Types)
                {
                    AnalyzeTypeReference(t);
                }
            }

            // Checking for tuple types like:
            // type X = [1, 2];
            //          ~~~~~~~~~~~~~~~
            var tupleType = type.As<ITupleTypeNode>();
            if (tupleType != null)
            {
                foreach (var t in tupleType.ElementTypes)
                {
                    AnalyzeTypeReference(t);
                }
            }

            // Checking for type query like:
            // type X = typeof anIdentifier;
            //          ~~~~~~~~~~~~~~~
            var typeQueryType = type.As<ITypeQueryNode>();
            if (typeQueryType != null)
            {
                var entityName = typeQueryType.ExprName;
                AnalyzeTypeReference(entityName);
            }
        }

        private void AnalyzeTypeArguments([CanBeNull]NodeArray<ITypeNode> typeArguments)
        {
            foreach (var t in typeArguments.AsStructEnumerable())
            {
                AnalyzeTypeReference(t);
            }
        }

        private void AnalyzeParameters(NodeArray<IParameterDeclaration> parameters)
        {
            foreach (var parameterDeclaration in parameters.AsStructEnumerable())
            {
                AddOrCreateReferencedSymbol(SymbolKind.ParameterDeclaration, parameterDeclaration.Name.GetName());
                AnalyzeTypeReference(parameterDeclaration.Type);
            }
        }

        private void AnalyzeTypeReference(EntityName entityName)
        {
            var typeName = entityName.GetAtomsFromQualifiedName();
            var typeNameAsString = string.Join(".", typeName);
            AddOrCreateReferencedSymbol(SymbolKind.TypeReference, typeNameAsString);
        }

        private static string GetBaseTypeName(IExpressionWithTypeArguments type)
        {
            var propertyAccess = type.Expression.As<IPropertyAccessExpression>();
            if (propertyAccess == null)
            {
                return type.Expression.TryCast<IIdentifier>()?.Text;
            }

            return propertyAccess.ToDisplayString();
        }

        private void AppendSymbol(BuildXLWriter writer, SymbolKind kind, string name, DeclarationFlags? flags = null)
        {
            if (flags != null)
            {
                writer.WriteCompact((int)flags);
            }

            writer.Write((byte)kind);

            AppendFullName(writer, name);
        }

        private void AppendFullName(BuildXLWriter writer, string name)
        {
            using (m_currentLocationStack.PreserveLength())
            {
                foreach (var n in m_currentLocationStack.Names)
                {
                    writer.Write(n);
                }

                writer.Write(name);
            }
        }

        private void AddOrCreateDeclarationSymbol(SymbolKind kind, string name, DeclarationFlags? flags = null)
        {
            AppendSymbol(m_declaredSymbolsWriter, kind, name, flags);

            if (m_keepSymbols)
            {
                m_declaredSymbols.Add(CreateSymbol(kind, name, flags));
            }
        }

        private void AddOrCreateReferencedSymbol(SymbolKind kind, string name, DeclarationFlags? flags = null)
        {
            AppendSymbol(m_referencedSymbolsWriter, kind, name, flags);

            if (m_keepSymbols)
            {
                m_referencedSymbols.Add(CreateSymbol(kind, name, flags));
            }
        }

        private void AddOrCreateInteractionSymbol(SymbolKind kind, string name)
        {
            m_referencedSymbolsWriter.Write((byte)kind);
            m_referencedSymbolsWriter.Write(name);

            if (m_keepSymbols)
            {
                m_referencedSymbols.Add(new InteractionSymbol(kind, name));
            }
        }

        private InteractionSymbol CreateSymbol(SymbolKind kind, string name, DeclarationFlags? flags = null)
        {
            string modifiers = flags?.ToDisplayModifier();
            string fullNameAsString = GetFullNameFor(name, modifiers);

            return new InteractionSymbol(kind, fullNameAsString);
        }

        private string GetFullNameFor(string name, string modifiers = null)
        {
            // TODO: This function is not very performant and generates a lot of memory traffic
            // Consider more efficient implementation.
            using (m_currentLocationStack.PreserveLength())
            {
                m_currentLocationStack.Push(name);
                if (!string.IsNullOrEmpty(modifiers))
                {
                    m_currentLocationStack.Push(modifiers);
                }

                return m_currentLocationStack.CurrentName;
            }
        }

        private void AnalyzeDecorators(INode source)
        {
            if (source?.Decorators?.Count > 0)
            {
                foreach (var d in source.Decorators.AsStructEnumerable())
                {
                    AnalyzeExpression(d.Expression, 0);
                }
            }
        }

        private static DeclarationFlags GetModifiers([CanBeNull]INode source, bool isOptional = false)
        {
            if (source == null)
            {
                return DeclarationFlags.None;
            }

            var result = GetModifiers(source.Modifiers?.Flags ?? NodeFlags.None) | GetObsoleteModifierAsFlags(source);

            if (isOptional)
            {
                result |= DeclarationFlags.Optional;
            }

            return result;
        }

        private static DeclarationFlags GetObsoleteModifierAsFlags(INode source)
        {
            if (NodeArrayExtensions.Any(source.Decorators, d => IsObsolete(d)))
            {
                return DeclarationFlags.Obsolete;
            }

            return DeclarationFlags.None;
        }

        private static DeclarationFlags GetModifiers(NodeFlags flags)
        {
            var result = DeclarationFlags.None;

            if ((flags & NodeFlags.Export) != 0)
            {
                result |= DeclarationFlags.Export;
            }

            if ((flags & NodeFlags.Ambient) != 0)
            {
                result |= DeclarationFlags.Ambient;
            }

            if ((flags & NodeFlags.ScriptPublic) != 0)
            {
                result |= DeclarationFlags.Public;
            }

            return result;
        }

        private static bool IsObsolete(IDecorator decorator)
        {
            return UnwrapIdentifier(decorator.Expression)?.Text == Names.ObsoleteAttributeName;
        }

        [CanBeNull]
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
    }
}
