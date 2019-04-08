// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Workspaces;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules;
using BuildXL.FrontEnd.Sdk;
using TypeScript.Net.Extensions;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Incrementality
{
    using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

    /// <summary>
    /// A visitor that can generate a public-only representation of a source file
    /// </summary>
    /// <remarks>
    /// This aims at being equivalent to the --declaration option for tsc.exe, with some DScript-specific tweaks
    /// </remarks>
    public sealed class PublicSurfacePrinter : ReformatterVisitor
    {
        private readonly ISemanticModel m_semanticModel;
        private readonly HashSet<INode> m_keptNodes;

        /// <summary>
        /// Set to 'true' for testing only, so that the test can test the correctness of
        /// <see cref="IsTypeAccessible(ITypeNode)"/> for explicitly declared types.
        /// </summary>
        private readonly bool m_skipTypeInferenceForTesting;

        /// <nodoc/>
        public PublicSurfacePrinter(ScriptWriter writer, ISemanticModel semanticModel)
            : this(writer, semanticModel, skipTypeInferenceForTesting: false)
        { }

        /// <nodoc/>
        internal PublicSurfacePrinter(ScriptWriter writer, ISemanticModel semanticModel, bool skipTypeInferenceForTesting)
            : base(writer, onlyPrintHeader: false)
        {
            Contract.Requires(semanticModel != null);

            m_semanticModel = semanticModel;
            m_keptNodes = new HashSet<INode>();
            m_skipTypeInferenceForTesting = skipTypeInferenceForTesting;
        }

        /// <summary>
        /// Attempts to print the public surface of a source file.
        /// </summary>
        public bool TryPrintPublicSurface(ISourceFile sourceFile, out FileContent publicContent)
        {
            ((IVisitableNode)sourceFile).Accept(this);
            if (CancellationRequested)
            {
                publicContent = FileContent.Invalid;
                return false;
            }

            publicContent = FileContent.ReadFromString(Writer.ToString());
            return true;
        }

        /// <summary>
        /// Variable declaration bodies are nullyfied and its type annotated
        /// </summary>
        public override void VisitVariableDeclaration(VariableDeclaration node)
        {
            // This is likely the most common element to print. So instead of cloning the node
            // we manually write it to avoid extra memory allocations
            AppendFlags(node.Flags);
            AppendNode(node.Name);

            // There is always a type, inferred or explicit
            Writer.AppendToken(":").Whitespace();
            if (node.Type != null && IsTypeAccessible(node.Type))
            {
                // Type is explicitly declared and we've determined that it is accesible
                //   --> print the type declaration
                Writer.AppendToken(node.Type.ToDisplayString());
            }
            else
            {
                // Type was either not declared or we couldn't determine that it is accessible
                //   --> try to infer it (which may trigger visibility problems that otherwise would be ignored)
                TryPrintInferredType(node);
            }

            // We don't need to print a body, the corresponding variable statement was just made an ambient, so nothing to write
        }

        private bool IsTypeAccessible(ITypeNode type)
        {
            // type not declared --> treat it as accessible
            if (type == null)
            {
                return true;
            }

            // predefined types are always accessible
            if (type.IsPredefinedType())
            {
                return true;
            }

            // descend into ParenthesizedType
            if (type.Kind == SyntaxKind.ParenthesizedType)
            {
                return IsTypeAccessible(type.Cast<IParenthesizedTypeNode>().Type);
            }

            // an array type is accessible iff its element type is accessible
            if (type.Kind == SyntaxKind.ArrayType)
            {
                return IsTypeAccessible(type.Cast<IArrayTypeNode>().ElementType);
            }

            // a union type is accessible iff all its types are accessible
            if (type.Kind == SyntaxKind.UnionType)
            {
                return type.Cast<IUnionOrIntersectionTypeNode>().Types.All(t => IsTypeAccessible(t));
            }

            // a function type is accessible iff its return type and its type parameters are accessible
            if (type.Kind == SyntaxKind.FunctionType)
            {
                var fnType = type.Cast<IFunctionOrConstructorTypeNode>();
                var fnParameters = fnType.Parameters ?? NodeArray.Empty<IParameterDeclaration>();
                return
                    IsTypeAccessible(fnType.Type) &&
                    fnParameters.All(tp => IsTypeAccessible(tp.Type));
            }

            // a type literal is accessible iff all its members are accessible
            if (type.Kind == SyntaxKind.TypeLiteral)
            {
                return type.Cast<ITypeLiteralNode>().Members.All(m => IsMemberAccessible(m));

                bool IsMemberAccessible(ITypeElement e)
                {
                    var memberTypes = e.Kind == SyntaxKind.PropertySignature
                        ? new[] { e.Cast<IPropertySignature>().Type }
                        : null; // handle other kinds if needed
                    return memberTypes != null && memberTypes.All(t => IsTypeAccessible(t));
                }
            }

            // if any type argument is inaccessible, this type is not accessible either
            var typeArguments = type.GetTypeArguments();
            if (!typeArguments.All(t => IsTypeAccessible(t)))
            {
                return false;
            }

            // if this type couldn't be resolved, treat it as inaccessible
            var resolvedSymbol = type.ResolvedSymbol;
            if (resolvedSymbol == null)
            {
                return false;
            }

            // otherwise, a type is accessible if any of its declarations is either explicitly exported or defined in the prelude
            return resolvedSymbol
                .GetDeclarations()
                .Any(d =>
                    d.IsExported() ||
                    m_semanticModel.TypeChecker.IsPreludeDeclaration(d));
        }

        /// <summary>
        /// If the variable statement is exported, then it becomes an ambient. Since we want to
        /// annotate each declaration with the original position, each declaration is printed
        /// as a separate statement
        /// </summary>
        public override void VisitVariableStatement(VariableStatement node)
        {
            var declarations = node.DeclarationList.Declarations;
            for (var i = 0; i < declarations.Count; i++)
            {
                AppendSingleVariableStatementAndForceAmbient(node, declarations[i]);

                // All new statements get an ending semicolon but the last one. That one is added by the caller of this method.
                if (i != declarations.Count - 1)
                {
                    AppendSeparatorToken(Writer.NoNewLine());
                }
            }
        }

        private void AppendDecoratorsWithPositionFor(IVariableStatement statement, IVariableDeclaration declaration)
        {
            AppendPositionDecorator(declaration);
            base.AppendDecorators(statement);
        }

        /// <summary>
        /// Every declaration statement we print is decorated with the original position of the declaration name as its first decorator.
        /// Enum members are included as well.
        /// </summary>
        /// <remarks>
        /// Note that VariableStatements are not DeclarationStatements, but those are explicitly handled in <see cref="VisitVariableStatement"/>
        /// </remarks>
        public override void AppendDecorators(INode node)
        {
            var declarationStatement = node.As<IDeclarationStatement>();
            if (
                // Need to explude export declarations here because
                // they add position decorators manually.
                (declarationStatement != null && declarationStatement.Kind != SyntaxKind.ExportDeclaration)
                || node.Kind == SyntaxKind.EnumMember)
            {
                AppendPositionDecorator(node);
            }

            base.AppendDecorators(node);
        }

        private void AppendPositionDecorator(INode node)
        {
            IDecorator positionDecorator = new Decorator(new LiteralExpression(node.Pos));
            AppendNode(positionDecorator);
            Writer.NewLine();
        }

        /// <summary>
        /// Function declarations are turned into ambients
        /// </summary>
        public override void VisitFunctionDeclaration(FunctionDeclaration node)
        {
            AppendDecorators(node);
            AppendFlagsAndForceAmbient(node.Flags);
            Writer.AppendToken("function").Whitespace();
            AppendOptionalNode(node.Name);

            // TODO: do we need to do visibility check on type parameters? Task 1066456
            AppendTypeParameters(node.TypeParameters);
            AppendInferredArgumentsOrParameters(node.Parameters);

            Writer.NoNewLine();

            Writer.Whitespace();
            Writer.AppendToken(":").Whitespace();

            // There is always a type
            // But we always try to infer it, since that may trigger visibility problems
            // that otherwise would be ignored
            TryPrintInferredType(node);
        }

        private void AppendInferredArgumentsOrParameters(NodeArray<IParameterDeclaration> nodeParameters)
        {
            AppendList(
                nodeParameters,
                separatorToken: ScriptWriter.SeparateArgumentsToken,
                startBlockToken: ScriptWriter.StartArgumentsToken,
                endBlockToken: ScriptWriter.EndArgumentsToken,
                placeSeparatorOnLastElement: false,
                minimumCountBeforeNewLines: 5,
                printTrailingComments: true,
                visitItem: n => VisitInferredParameterDeclaration(n.Cast<ParameterDeclaration>()));
        }

        private void VisitInferredParameterDeclaration(ParameterDeclaration node)
        {
            AppendFlags(node.Flags);
            AppendOptionalNode(node.DotDotDotToken.ValueOrDefault);
            AppendNode(node.Name);
            AppendOptionalNode(node.QuestionToken.ValueOrDefault);
            Writer.Whitespace().AppendToken(":").Whitespace();

            // We always print an inferred type
            TryPrintInferredType(node);

            // We don't print the initializer. AppendOptionalNode(node.Initializer, "=");
        }

        /// <summary>
        /// Some statements are filtered based on visibility
        /// </summary>
        public override void VisitSourceFile(TypeScript.Net.Types.SourceFile node)
        {
            if (node.Statements.IsNullOrEmpty())
            {
                return;
            }

            var filteredStatements = FilterStatements(node.Statements);
            AppendSourceFileStatements(node, filteredStatements);
        }

        /// <summary>
        /// Some statements are filtered based on visibility
        /// </summary>
        public override void VisitModuleBlock(ModuleBlock node)
        {
            if (node.Statements == null)
            {
                return;
            }

            var filteredStatements = FilterStatements(node.Statements);
            AppendBlock(filteredStatements);
        }

        /// <summary>
        /// Export declarations can turn non-exported declarations into exported ones. So we account for those cases here.
        /// Additionally, each named specifier is given its own declaration, so positions can be kept
        /// </summary>
        /// <remarks>
        /// Observe that exports can only be top-level statements and they can only reference top level declarations. So
        /// it is always safe to print the referenced declarations in this same context.
        /// </remarks>
        public override void VisitExportDeclaration(ExportDeclaration node)
        {
            // In the case of 'export {blah}', local declarations are turned into exported ones, so we need to print them
            if (node.ExportClause != null && node.ModuleSpecifier == null)
            {
                WriteExportedAliasedDeclarations(node);
            }

            if (node.ExportClause == null)
            {
                AppendPositionDecorator(node);
                // @@public can be applied to export declarations.
                AppendDecorators(node);
                Writer.AppendToken("export").Whitespace();
                Writer.AppendToken("*");
                AppendModuleSpecifier(node); // TODO: revisit
            }
            else
            {
                VisitNamedExports(node.ExportClause, node);
            }
        }

        private void VisitNamedExports(INamedExports nodeExportClause, ExportDeclaration exportDeclaration)
        {
            // export {A, B};
            // is translated into:
            // export {A};
            // export {B};
            for (var i = 0; i < nodeExportClause.Elements.Count; i++)
            {
                var exportSpecifier = nodeExportClause.Elements[i];

                AppendPositionDecorator(exportSpecifier);
                // @@public can be applied to export declarations.
                AppendDecorators(exportDeclaration);
                Writer.AppendToken("export").Whitespace();
                AppendNode(new NamedExports { Decorators = nodeExportClause.Decorators, Elements = new NodeArray<IExportSpecifier>(exportSpecifier), Flags = nodeExportClause.Flags });
                AppendModuleSpecifier(exportDeclaration);

                // The last export specifier gets a separator from upstream
                if (i != nodeExportClause.Elements.Count - 1)
                {
                    AppendSeparatorIfNeeded(exportDeclaration);
                }
            }
        }

        private void AppendModuleSpecifier(ExportDeclaration node)
        {
            if (node.ModuleSpecifier != null)
            {
                Writer.Whitespace().AppendToken("from").Whitespace();
                AppendNode(node.ModuleSpecifier);
            }
        }

        /// <summary>
        /// We decompose namespaces like A.B.C into individual namespace declarations, so each one gets its own original position
        /// </summary>
        public override void VisitModuleDeclaration(ModuleDeclaration node)
        {
            AppendDecorators(node);
            AppendModuleFlags(node);
            Writer.AppendToken(node.Name.Text);
            Writer.Whitespace();

            var body = node.Body;
            var nestedModule = body.AsModuleDeclaration();
            if (nestedModule != null)
            {
                using (Writer.Block())
                {
                    AppendNode(nestedModule);
                }
            }
            else
            {
                AppendNode(body.AsModuleBlock());
            }
        }

        private void TryPrintInferredType(INode node)
        {
            if (m_skipTypeInferenceForTesting)
            {
                CancelVisitation();
                return;
            }

            string typeString;
            bool success;
            var signatureDeclaration = node.As<ISignatureDeclaration>();

            // If the node is a signature, what we want to print is the return type
            if (signatureDeclaration != null)
            {
                success = m_semanticModel.TryPrintReturnTypeOfSignature(signatureDeclaration, out typeString, node.Parent, TypeFormatFlags.NoTruncation);
            }
            else
            {
                var type = m_semanticModel.GetTypeAtLocation(node);
                success = m_semanticModel.TryPrintType(type, out typeString, node.Parent, TypeFormatFlags.NoTruncation);
            }

            // If the type is not printable due to visibility problems, we cancel visitation
            if (!success)
            {
                CancelVisitation();
            }

            Writer.AppendToken(typeString);
        }

        private void WriteExportedAliasedDeclarations(ExportDeclaration node)
        {
            foreach (var exportSpecifier in node.ExportClause.Elements)
            {
                // Collect all aliases that the specifier points to
                var name = exportSpecifier.PropertyName ?? exportSpecifier.Name;
                var linkedAliases = m_semanticModel.TypeChecker.CollectLinkedAliases(name);

                foreach (var linkedAlias in linkedAliases)
                {
                    WriteLinkedAlias(linkedAlias);
                }
            }
        }

        /// <summary>
        /// We only care about declaration statements and variable declarations, which are the only
        /// nodes that introduce declarations.
        /// In all cases, we check that the declaration was not already exported (with export {a as b} this become possible)
        /// We also keep track of already printed statements to account for the case of aliasing (two export statements that point to
        /// the same value). E.g. export {x as a}; export {x as b};
        /// </summary>
        private void WriteLinkedAlias(INode linkedAlias)
        {
            var declarationStatement = linkedAlias.As<IDeclarationStatement>();
            if (declarationStatement != null && (declarationStatement.Flags & NodeFlags.Export) == NodeFlags.None &&
                !m_keptNodes.Contains(declarationStatement))
            {
                AppendNode(declarationStatement);
                m_keptNodes.Add(declarationStatement);
            }

            var varDeclaration = linkedAlias.As<IVariableDeclaration>();
            if (varDeclaration != null)
            {
                // For the case of a variable declaration, we print a statement per variable, so we keep the code simpler.
                // TODO: a more compact version can be generated, but the case of multiple variables per statement is not that likely to occur
                var statement = varDeclaration.Parent.Parent;
                if ((statement.Flags & NodeFlags.Export) == NodeFlags.None && !m_keptNodes.Contains(varDeclaration))
                {
                    var variableStatement = statement.Cast<IVariableStatement>();
                    AppendSingleVariableStatementAndForceAmbient(variableStatement, varDeclaration);
                    AppendSeparatorIfNeeded(variableStatement);
                    m_keptNodes.Add(varDeclaration);
                }
            }
        }

        private void AppendFlagsAndForceAmbient(NodeFlags flags)
        {
            var finalFlags = flags;

            if ((flags & NodeFlags.Ambient) == NodeFlags.None)
            {
                finalFlags = flags | NodeFlags.Ambient;
            }

            AppendFlags(finalFlags);
        }

        /// <summary>
        /// Uses the containing statement to print statement-level decorators and flags, followed by an individual variable declaration
        /// </summary>
        /// <remarks>
        /// The statement is forced to be an ambient one
        /// </remarks>
        private void AppendSingleVariableStatementAndForceAmbient(IVariableStatement containingStatement, IVariableDeclaration varDeclaration)
        {
            AppendDecoratorsWithPositionFor(containingStatement, varDeclaration);
            AppendFlagsAndForceAmbient(containingStatement.Flags);
            VisitVariableDeclarationList(new VariableDeclarationList(varDeclaration));
        }

        private NodeArray<IStatement> FilterStatements(NodeArray<IStatement> statements)
        {
            var filteredStatements = new List<IStatement>(statements.Elements.Where(ShouldKeepStatement).ToList());
            return new NodeArray<IStatement>(filteredStatements);
        }

        private bool ShouldKeepStatement(IStatement statement)
        {
            if (statement.IsInjectedForDScript())
            {
                return false;
            }

            var result = (statement.Kind == SyntaxKind.ModuleDeclaration) || // Modules are automatically exported in DScript
                   (statement.Kind == SyntaxKind.ImportDeclaration) || // Import and
                   (statement.Kind == SyntaxKind.ExportDeclaration) || // export declarations are always kept to make references work
                   (statement.Kind == SyntaxKind.InterfaceDeclaration) || // Interfaces,
                   (statement.Kind == SyntaxKind.TypeAliasDeclaration) || // Types,
                   (statement.Kind == SyntaxKind.EnumDeclaration) || // and Enums are kept regardless visibility so we don't need to analyze type-related declarations. TODO: this can be optimized
                   (statement.Flags & NodeFlags.Export) != NodeFlags.None;   // anything else that is explicitly exported

            if (result)
            {
                m_keptNodes.Add(statement);
            }

            return result;
        }
    }
}
