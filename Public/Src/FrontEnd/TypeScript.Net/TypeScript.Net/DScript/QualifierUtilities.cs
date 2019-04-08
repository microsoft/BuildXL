// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;
using static TypeScript.Net.Extensions.NodeArrayExtensions;

namespace TypeScript.Net.DScript
{
    /// <nodoc/>
    public static class QualifierUtilities
    {
        /// <summary>
        /// Injects to the node statements a function declaration for 'withQualifier'
        /// </summary>
        /// <remarks>
        /// The created function has the following shape:
        ///
        /// <code>
        /// @@public
        /// export declare function withQualifier(newQualifier: typeof qualifier) : typeof namespaceName;
        /// </code>
        /// 
        /// All nodes are created with the module block starting position and with a length of 1. They are also all flagged with
        /// NodeFlags.ScriptInjectedNode. This is in order to avoid the checker to think that the node is missing.
        ///
        /// The identifier count and node count that the parser mantains is not updated! But nobody is consuming that information so far.
        /// 
        /// Note that a source file should be passed explicitely, because <see cref="NodeStructureExtensions.GetSourceFile"/> will return null
        /// when the file get's parsed but not bound (and this is exactly the case for this function: it is called during parsing phase when the <see cref="INode.Parent"/> is still null).
        /// </remarks>
        public static void AddWithQualifierFunction(this IModuleBlock node, IIdentifier namespaceName, ISourceFile sourceFile)
        {
            Contract.Requires(node != null);
            Contract.Requires(namespaceName != null);
            Contract.Requires(sourceFile != null);

            var withQualifier = CreateWithQualifierFunction(namespaceName, node.Pos, sourceFile);

            node.Statements.Add(withQualifier);
        }

        /// <nodoc />
        [System.Obsolete("Use AddWithQualifierFunction that takes a source file.")]
        public static void AddWithQualifierFunction(this IModuleBlock node, IIdentifier namespaceName)
        {
            throw new NotSupportedException("Please use AddWithQualifierFunction that takes a source file.");
        }

        /// <summary>
        /// Injects a 'withQualifier' function as a top level statement in a source file
        /// </summary>
        /// <remarks>
        /// What's being generated is:
        /// 
        /// <code>
        /// @@public
        /// export declare function withQualifier(newQualifier: typeof qualifier) : typeof $;
        /// </code>
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public static void AddTopLevelWithQualifierFunction(this ISourceFile sourceFile, string owningModuleName)
        {
            var withQualifier = CreateWithQualifierFunction(CreateIdentifier(Names.RootNamespace, 0, sourceFile), 0, sourceFile);

            sourceFile.Statements.Add(withQualifier);

            sourceFile.DeclaresInjectedTopLevelWithQualifier = true;
        }

        private static FunctionDeclaration CreateWithQualifierFunction(IIdentifier namespaceName, int pos, ISourceFile sourceFile)
        {
            // Observe the body is null, this is a 'declare' function
            var withQualifier = CreateInjectedNode<FunctionDeclaration>(SyntaxKind.FunctionDeclaration, pos, sourceFile);

            withQualifier.Name = CreateIdentifier(Names.WithQualifierFunction, pos, sourceFile);
            withQualifier.Flags |= NodeFlags.Ambient | NodeFlags.Export | NodeFlags.ScriptPublic;
            withQualifier.Modifiers = ModifiersArray.Create(withQualifier.Flags);
            withQualifier.Modifiers.Add(CreateInjectedNode<Modifier>(SyntaxKind.ExportKeyword, pos, sourceFile));
            withQualifier.Modifiers.Add(CreateInjectedNode<Modifier>(SyntaxKind.DeclareKeyword, pos, sourceFile));
            withQualifier.Parameters = CreateWithQualifierParameters(pos, sourceFile);
            withQualifier.Type = CreateTypeOfExpression(namespaceName.Text, pos, sourceFile);
            withQualifier.Decorators = new NodeArray<IDecorator>(CreatePublicDecorator(pos, sourceFile));

            return withQualifier;
        }

        /// <nodoc />
        public static IDecorator CreatePublicDecorator(int pos, ISourceFile sourceFile)
        {
            var publicIdentifier = CreateIdentifier(Names.PublicDecorator, pos, sourceFile);
            publicIdentifier.OriginalKeywordKind = SyntaxKind.PublicKeyword;

            var decorator = CreateInjectedNode<Decorator>(SyntaxKind.Decorator, pos, sourceFile);
            decorator.Expression = publicIdentifier;

            return decorator;
        }

        /// <summary>
        /// Returns whether a statement is a qualifier declaration
        /// </summary>
        /// <remarks>
        /// This function cannot rely on EnforceQualifierDeclarationRule, since node injection happens before the linter runs
        /// </remarks>
        public static bool IsQualifierDeclaration(this INode node)
        {
            var declarationStatement = node.As<IVariableStatement>();

            return Any(
                    declarationStatement?.DeclarationList?.Declarations,
                    declaration => declaration.Name.GetText() == Names.CurrentQualifier);
        }

        /// <summary>
        /// Returns whether a statement is a qualifier declaration
        /// </summary>
        /// <remarks>
        /// This function cannot rely on EnforceQualifierDeclarationRule, since node injection happens before the linter runs
        /// </remarks>
        public static bool IsQualifierDeclaration(this INode node, SymbolAtom qualifierNameAsAtom, string qualifierNameAsString)
        {
            var declarationStatement = node.As<IVariableStatement>();

            foreach (var declaration in (declarationStatement?.DeclarationList?.Declarations).AsStructEnumerable())
            {
                var name = declaration.Name;
                var identifier = name.As<SymbolAtomBasedIdentifier>();
                if (identifier != null)
                {
                    // Fast path: check an optimized version.
                    if (identifier.Name == qualifierNameAsAtom)
                    {
                        return true;
                    }
                }
                else
                {
                    // Slow path: need to get the text of the identifier
                    if (declaration.Name.GetText() == qualifierNameAsString)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Injects a default qualifier declaration as a top level statement
        /// </summary>
        /// <remarks>
        /// The created function has the following shape:
        ///
        /// export declare const qualifier : {};
        ///
        /// The identifier count and node count that the parser mantains is not updated! But nobody is consuming that information so far.
        /// The declaration is created at position 0. This should not be visible to the user though.
        /// </remarks>
        public static void AddDefaultQualifierDeclaration(this ISourceFile sourceFile)
        {
            var variableStatement = CreateInjectedNode<VariableStatement>(SyntaxKind.VariableStatement, 0, sourceFile);

            // Flags and modifiers for the statement are set to 'export declare'
            variableStatement.Flags |= NodeFlags.Ambient | NodeFlags.Export;
            variableStatement.Modifiers = ModifiersArray.Create(variableStatement.Flags);
            variableStatement.Modifiers.Add(CreateInjectedNode<Modifier>(SyntaxKind.ExportKeyword, 0, sourceFile));
            variableStatement.Modifiers.Add(CreateInjectedNode<Modifier>(SyntaxKind.DeclareKeyword, 0, sourceFile));

            variableStatement.DeclarationList = CreateQualifierDeclaration(0, sourceFile);

            sourceFile.Statements.Insert(0, variableStatement);

            sourceFile.DeclaresRootQualifier = true;
        }

        /// <summary>
        /// Creates a declaration list with shape 'const qualifier: {}'
        /// </summary>
        private static VariableDeclarationList CreateQualifierDeclaration(int pos, ISourceFile sourceFile)
        {
            // Flags for the declaration list are set to 'const'
            var declarationList = CreateInjectedNode<VariableDeclarationList>(SyntaxKind.VariableDeclarationList, pos, sourceFile);
            declarationList.Flags |= NodeFlags.Const | NodeFlags.Export;

            var variableDeclaration = CreateInjectedNode<VariableDeclaration>(SyntaxKind.VariableDeclaration, pos, sourceFile);
            variableDeclaration.Name = new IdentifierOrBindingPattern(CreateIdentifier(Names.CurrentQualifier, pos, sourceFile));
            variableDeclaration.Type = CreateEmptyTypeLiteral(pos, sourceFile);

            declarationList.Declarations = new NodeArray<IVariableDeclaration>(variableDeclaration)
                                           {
                                               Pos = variableDeclaration.Pos,
                                               End = variableDeclaration.End,
                                           };

            return declarationList;
        }

        /// <summary>
        /// Creates a type literal with shape {}
        /// </summary>
        private static TypeLiteralNode CreateEmptyTypeLiteral(int pos, ISourceFile sourceFile)
        {
            var emptyTypeLiteral = CreateInjectedNode<TypeLiteralNode>(SyntaxKind.TypeLiteral, pos, sourceFile);
            emptyTypeLiteral.Members = NodeArray.Empty<ITypeElement>();
            return emptyTypeLiteral;
        }

        /// <nodoc />
        public static Identifier CreateIdentifier(string identifierName, int pos, ISourceFile sourceFile)
        {
            // This won't increase the identifier count in the parser. But nobody is really consuming that.
            var identifier = CreateInjectedNode<Identifier>(SyntaxKind.Identifier, pos, sourceFile);
            identifier.Text = identifierName;
            return identifier;
        }

        /// <summary>
        /// Creates "withQualifier: typeof qualifier"
        /// </summary>
        private static NodeArray<IParameterDeclaration> CreateWithQualifierParameters(int pos, ISourceFile sourceFile)
        {
            var qualifier = CreateInjectedNode<ParameterDeclaration>(SyntaxKind.Parameter, pos, sourceFile);
            qualifier.Name = new IdentifierOrBindingPattern(CreateIdentifier(Names.WithQualifierParameter, pos, sourceFile));
            qualifier.Type = CreateTypeOfExpression(Names.CurrentQualifier, pos, sourceFile);

            var result = new NodeArray<IParameterDeclaration>(qualifier);
            result.Pos = qualifier.Pos;
            result.End = qualifier.End;

            return result;
        }

        /// <summary>
        /// Creates "typeof #identifierName#"
        /// </summary>
        private static TypeQueryNode CreateTypeOfExpression(string identifierName, int pos, ISourceFile sourceFile)
        {
            var type = CreateInjectedNode<TypeQueryNode>(SyntaxKind.TypeQuery, pos, sourceFile);
            type.ExprName = new EntityName(CreateIdentifier(identifierName, pos, sourceFile));

            return type;
        }

        /// <summary>
        /// Creates an injected node of length 1 that is flagged as belonging to the special 'withQualifier' function.
        /// </summary>
        private static TNode CreateInjectedNode<TNode>(SyntaxKind kind, int pos, ISourceFile sourceFile) where TNode : INode, new()
        {
            Contract.Requires(sourceFile != null);

            var result = FastActivator<TNode>.Create();

            // The node is always created with length 1, so GetNodeOfText does not return 'missing'.
            result.Initialize(kind, pos, pos + 1);

            result.MarkAsDScriptInjected();
            result.SourceFile = sourceFile;

            return result;
        }
    }
}
