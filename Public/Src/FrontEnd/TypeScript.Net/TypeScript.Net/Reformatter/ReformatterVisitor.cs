// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;

namespace TypeScript.Net.Reformatter
{
    /// <nodoc />
    public class ReformatterVisitor : INodeVisitor
    {
        private const string Separator = ";";

        /// <nodoc />
        public ReformatterVisitor(ScriptWriter writer, bool onlyPrintHeader, bool attemptToPreserveNewlinesForListMembers = true)
        {
            Writer = writer;
            OnlyPrintHeader = onlyPrintHeader;
            AttemptToPreserveNewlinesForListMembers = attemptToPreserveNewlinesForListMembers;
        }

        /// <nodoc />
        public ScriptWriter Writer { get; }

        /// <nodoc />
        public bool OnlyPrintHeader { get; }

        /// <nodoc />
        public bool AttemptToPreserveNewlinesForListMembers { get; }

        /// <nodoc />
        public bool CancellationRequested { get; protected set; } = false;

        /// <nodoc />210
        protected void CancelVisitation()
        {
            CancellationRequested = true;
        }

        /// <nodoc />
        public virtual void VisitAccessorDeclaration(AccessorDeclaration node)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public virtual void VisitArrayLiteralExpression(ArrayLiteralExpression node)
        {
            VisitArrayLiteralExpression(node, 3); // More than three elements in an array will put each element on its own line.
        }

        /// <nodoc />
        public virtual void VisitArrayLiteralExpression(ArrayLiteralExpression node, int minimumCountBeforeNewLines)
        {
            AppendList(
                node.Elements,
                separatorToken: ScriptWriter.SeparateArrayToken,
                startBlockToken: ScriptWriter.StartArrayToken,
                endBlockToken: ScriptWriter.EndArrayToken,
                placeSeparatorOnLastElement: true,
                minimumCountBeforeNewLines: minimumCountBeforeNewLines,
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void VisitArrayTypeNode(ArrayTypeNode node)
        {
            AppendNode(node.ElementType);
            Writer.AppendToken("[]");
        }

        /// <nodoc />
        public virtual void VisitAsExpression(AsExpression node)
        {
            AppendNode(node.Expression);
            Writer
                .Whitespace()
                .AppendToken("as")
                .Whitespace();
            AppendNode(node.Type);
        }

        /// <nodoc />
        public virtual void VisitBinaryExpression(BinaryExpression node)
        {
            // TODO: not sure about name!!
            AppendOptionalNode(node.Name);
            AppendNode(node.Left);
            Writer.Whitespace();
            AppendNode(node.OperatorToken);
            Writer.Whitespace();
            AppendNode(node.Right);
        }

        /// <nodoc />
        public virtual void VisitBindingElement(BindingElement node)
        {
            Writer.AppendToken("{[");
            AppendOptionalNode(node.Name);
            Writer.AppendToken("]:");
            AppendOptionalNode(node.PropertyName);
            Writer.AppendToken("}").Whitespace();
            AppendInitializerIfNeeded(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitBindingPattern(BindingPattern node)
        {
            Writer.AppendToken("[");

            foreach (var e in node.Elements)
            {
                AppendNode(e);
            }

            Writer.AppendToken("]");
        }

        /// <nodoc />
        public virtual void VisitBlock(Block node)
        {
            AppendStatements(node.Statements, node);
        }

        /// <nodoc />
        public virtual void VisitBreakOrContinueStatement(BreakOrContinueStatement node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.BreakStatement:
                    Writer.AppendToken("break");
                    break;
                case SyntaxKind.ContinueStatement:
                    Writer.AppendToken("continue");
                    break;
                default:
                    AppendNode(node.Label);
                    break;
            }
        }

        /// <nodoc />
        public virtual void VisitCallExpression(CallExpression node)
        {
            AppendNode(node.Expression);
            AppendTypeArguments(node.TypeArguments);

            var singleArg = node.Arguments != null && node.Arguments.Count == 1 ? node.Arguments[0] : null;

            switch (singleArg?.Kind)
            {
                case SyntaxKind.ArrayLiteralExpression:
                    Writer
                        .AppendToken(ScriptWriter.StartArgumentsToken);
                    AppendNode(singleArg);
                    Writer.NoNewLine()
                        .AppendToken(ScriptWriter.EndArgumentsToken);
                    break;
                case SyntaxKind.ObjectLiteralExpression:
                    Writer
                        .AppendToken(ScriptWriter.StartArgumentsToken);
                    AppendNode(singleArg);
                    Writer.NoNewLine()
                        .AppendToken(ScriptWriter.EndArgumentsToken);
                    break;
                default:
                    AppendArgumentsOrParameters(node.Arguments);
                    Writer.NoNewLine();
                    break;
            }
        }

        /// <nodoc />
        public virtual void VisitCallSignatureDeclarationOrConstructSignatureDeclaration(CallSignatureDeclarationOrConstructSignatureDeclaration node)
        {
        }

        /// <nodoc />
        public virtual void VisitCaseBlock(CaseBlock node)
        {
            using (Writer.Block())
            {
                foreach (var clause in node.Clauses)
                {
                    AppendNode(clause);
                    Writer.AppendLine(string.Empty);
                }
            }
        }

        /// <nodoc />
        public virtual void VisitCaseClause(CaseClause node)
        {
            Writer
                .AppendToken("case")
                .Whitespace();
            AppendNode(node.Expression);
            Writer.AppendToken(":")
                .AppendLine(string.Empty);

            if (node.Statements != null)
            {
                using (Writer.Indent())
                {
                    foreach (var statement in node.Statements)
                    {
                        AppendNode(statement);
                        if (statement.NeedSeparatorAfter())
                        {
                            Writer.AppendLine(";");
                        }
                    }
                }
            }
        }

        /// <nodoc />
        public virtual void VisitCatchClause(CatchClause node)
        {
            throw new NotImplementedException("'catch' clause is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitClassDeclaration(ClassDeclaration node)
        {
            PrintClassLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitClassExpression(ClassExpression node)
        {
            PrintClassLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitClassElement(ClassElement node)
        {
            throw new NotImplementedException("Class elements are not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitComputedPropertyName(ComputedPropertyName node)
        {
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitConditionalExpression(ConditionalExpression node)
        {
            AppendNode(node.Condition);

            Writer.Whitespace();
            AppendNode(node.QuestionToken);
            Writer.Whitespace();
            AppendNode(node.WhenTrue);
            Writer.Whitespace();
            AppendNode(node.ColonToken);
            Writer.Whitespace();
            AppendNode(node.WhenFalse);
        }

        /// <nodoc />
        public virtual void VisitSwitchExpression(SwitchExpression node)
        {
            AppendNode(node.Expression);

            Writer.Whitespace().AppendToken("switch").Whitespace().AppendToken("{").NewLine();
            using (Writer.Indent())
            {
                foreach (var clause in node.Clauses)
                {
                    AppendNode(clause);
                    Writer.AppendLine(",");
                }
            }

            Writer.AppendToken("}");
        }

        /// <nodoc />
        public virtual void VisitSwitchExpressionClause(SwitchExpressionClause node)
        {
            if (node.IsDefaultFallthrough)
            {
                Writer.AppendToken("default");
            }
            else
            {
                AppendNode(node.Match);
            }

            Writer.AppendToken(":").Whitespace();
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitDecorator(Decorator node)
        {
            // The parser is not distinguishing between @ and @@
            // Special-casing the visitor for the DScript decorators
            // TODO: consider adding @@ as a first-class citizen.
            Writer.AppendToken("@@");
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitDefaultClause(DefaultClause node)
        {
            Writer.AppendLine("default:");
            using (Writer.Indent())
            {
                foreach (var statement in node.Statements)
                {
                    AppendNode(statement);
                    if (statement.NeedSeparatorAfter())
                    {
                        Writer.AppendLine(";");
                    }
                }
            }
        }

        /// <nodoc />
        public virtual void VisitDeleteExpression(DeleteExpression node)
        {
            throw new NotImplementedException("Delete expression is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitDoStatement(DoStatement node)
        {
            Writer.AppendToken("do").Whitespace();
            AppendNode(node.Statement);
            Writer.NoNewLine().Whitespace().AppendToken("while").Whitespace().AppendToken("(");
            AppendNode(node.Expression);
            Writer.AppendToken(")");
        }

        /// <nodoc />
        public virtual void VisitElementAccessExpression(ElementAccessExpression node)
        {
            AppendNode(node.Expression);

            Writer.AppendToken("[");
            AppendNode(node.ArgumentExpression);
            Writer.AppendToken("]");
        }

        /// <nodoc />
        public virtual void VisitEmptyStatement(EmptyStatement node)
        {
            // Nothing to write for empty statements
        }

        /// <nodoc />
        public virtual void VisitBlankLineStatement(BlankLineStatement node)
        {
            Writer.NewLine();
            Writer.AppendToken("\r\n");
        }

        /// <nodoc />
        public virtual void VisitEnumDeclaration(EnumDeclaration node)
        {
            AppendDecorators(node);
            AppendFlags(node.Flags);
            Writer.AppendToken("enum").Whitespace();
            AppendNode(node.Name);
            Writer.Whitespace();
            AppendList(
                node.Members,
                separatorToken: ",",
                startBlockToken: ScriptWriter.StartBlockToken,
                endBlockToken: ScriptWriter.EndBlockToken,
                placeSeparatorOnLastElement: true,
                minimumCountBeforeNewLines: 0,
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void VisitEnumMember(EnumMember node)
        {
            AppendDecorators(node);
            AppendNode(node.Name);
            AppendInitializerIfNeeded(node.Initializer.ValueOrDefault);
        }

        /// <nodoc />
        public virtual void VisitExportAssignment(ExportAssignment node)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public virtual void VisitExportDeclaration(ExportDeclaration node)
        {
            Writer.AppendToken("export").Whitespace();

            if (node.ExportClause == null)
            {
                Writer.AppendToken("*");
            }
            else
            {
                AppendNode(node.ExportClause);
            }

            if (node.ModuleSpecifier != null)
            {
                Writer.Whitespace().AppendToken("from").Whitespace();
                AppendNode(node.ModuleSpecifier);
            }
        }

        /// <nodoc />
        public virtual void VisitExportSpecifier(ExportSpecifier node)
        {
            if (node.PropertyName != null)
            {
                AppendNode(node.PropertyName);
                Writer.Whitespace()
                    .AppendToken("as")
                    .Whitespace();
            }

            AppendNode(node.Name);
        }

        /// <nodoc />
        public virtual void VisitExpression(Expression node)
        {
            if (node.Kind != SyntaxKind.OmittedExpression)
            {
                throw new InvalidOperationException("This is a bug.");
            }

            // Just expecting omitted expressions here?
        }

        /// <nodoc />
        public virtual void VisitExpressionStatement(ExpressionStatement node)
        {
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitExpressionWithTypeArguments(ExpressionWithTypeArguments node)
        {
            AppendOptionalNode(node.Expression);
            AppendNodes(node.TypeArguments);
        }

        /// <nodoc />
        public virtual void VisitExternalModuleReference(ExternalModuleReference node)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public virtual void VisitForInStatement(ForInStatement node)
        {
            Writer
                .AppendToken("for")
                .Whitespace()
                .AppendToken("(");
            AppendNode(node.Initializer);
            Writer.Whitespace()
                .AppendToken("in")
                .Whitespace();
            AppendNode(node.Expression);
            Writer.AppendToken(")")
                .Whitespace();
            AppendNode(node.Statement);
        }

        /// <nodoc />
        public virtual void VisitForOfStatement(ForOfStatement node)
        {
            Writer
                .AppendToken("for")
                .Whitespace()
                .AppendToken("(");
            AppendNode(node.Initializer);
            Writer.Whitespace()
                .AppendToken("of").Whitespace();
            AppendNode(node.Expression);
            Writer.AppendToken(")").Whitespace();
            AppendNode(node.Statement);
        }

        /// <nodoc />
        public virtual void VisitForStatement(ForStatement node)
        {
            Writer
                .AppendToken("for")
                .Whitespace()
                .AppendToken("(");
            AppendOptionalNode(node.Initializer);
            Writer.AppendToken(";").Whitespace();
            AppendOptionalNode(node.Condition);
            Writer.AppendToken(";").Whitespace();
            AppendOptionalNode(node.Incrementor);
            Writer.AppendToken(")").Whitespace();
            AppendNode(node.Statement);
        }

        /// <nodoc />
        public virtual void VisitFunctionOrConstructorTypeNode(FunctionOrConstructorTypeNode node)
        {
            AppendFlags(node.Flags);
            AppendTypeParameters(node.TypeParameters);
            Writer.AppendToken("(");
            AppendNodes(node.Parameters);
            Writer.AppendToken(")");

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.Whitespace().AppendToken("=>").Whitespace();
                AppendNode(node.Type);
            }
        }

        /// <nodoc />
        public virtual void VisitHeritageClause(HeritageClause node)
        {
            Writer
                .AppendToken(node.Token.ToDisplayString()).Whitespace();
            AppendNodes(node.Types);
        }

        /// <nodoc />
        public virtual void VisitIdentifier(IIdentifier node)
        {
            Writer.AppendToken(Utils.UnescapeIdentifier(node.Text));
        }

        /// <nodoc />
        public virtual void VisitIfStatement(IfStatement node)
        {
            Writer
                .AppendToken("if").Whitespace()
                .AppendToken("(");
            AppendNode(node.Expression);
            Writer.AppendToken(")").Whitespace();
            AppendNode(node.ThenStatement);

            if (node.ThenStatement.Kind != SyntaxKind.Block)
            {
                Writer.AppendToken(";");
                Writer.NewLine();
            }

            if (node.ElseStatement.HasValue)
            {
                Writer
                    .NoNewLine()
                    .Whitespace()
                    .AppendToken("else").Whitespace();
                AppendNode(node.ElseStatement.Value);
                if (node.ElseStatement.Value.Kind != SyntaxKind.Block)
                {
                    Writer.AppendToken(";");
                    Writer.NewLine();
                }
            }
        }

        /// <nodoc />
        public virtual void VisitIndexSignatureDeclaration(IndexSignatureDeclaration node)
        {
            AppendDecorators(node);
            AppendList(
                node.Parameters,
                separatorToken: ",",
                startBlockToken: "[",
                endBlockToken: "]",
                placeSeparatorOnLastElement: false,
                minimumCountBeforeNewLines: 5,
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void VisitInterfaceDeclaration(InterfaceDeclaration node)
        {
            AppendDecorators(node);
            AppendFlags(node.Flags);
            Writer.AppendToken("interface").Whitespace();
            AppendNode(node.Name);
            AppendTypeParameters(node.TypeParameters);

            if (!node.HeritageClauses.IsNullOrEmpty())
            {
                Writer.Whitespace();
                AppendNodes(node.HeritageClauses);
                Writer.Whitespace();
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            Writer.Whitespace(); // whitespace is required between interface name and open curly
            AppendBlock(node.Members);
        }

        /// <nodoc />
        public virtual void VisitLabeledStatement(LabeledStatement node)
        {
            throw new NotImplementedException("Labels are not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitLiteralExpression(LiteralExpression node)
        {
            char separator;
            if (LiteralExpression.LiteralExpressionToCharMap.TryGetValue(node.LiteralKind, out separator))
            {
                Writer.AppendQuotedString(node.Text, isPathString: false, quote: separator);
            }
            else
            {
                Writer.AppendToken(node.Text);
            }
        }

        /// <nodoc />
        public virtual void VisitMethodSignature(MethodSignature node)
        {
            Writer.AppendToken(node.Name.ToDisplayString());
            AppendTypeParameters(node.TypeParameters);
            Writer.AppendToken("(");
            AppendNodes(node.Parameters);
            Writer.AppendToken(")");

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }
        }

        /// <nodoc />
        public virtual void VisitModifier(Modifier node)
        {
            Writer.AppendToken(node.Kind);
        }

        /// <nodoc />
        public virtual void VisitModuleBlock(ModuleBlock node)
        {
            AppendBlock(node.Statements);
        }

        /// <nodoc />
        public virtual void VisitModuleDeclaration(ModuleDeclaration node)
        {
            // 'export namespace' both part of flags declaration
            // No need to put 'namespace' keyword explicitly.
            AppendDecorators(node);

            // If this is a DScript V2 spec, we remove the export flag on the namespace if present, since
            // the explicit export is not needed in V2
            AppendModuleFlags(node);
            Writer.AppendToken(string.Join(".", node.GetFullName()));

            var body = node.GetModuleBlock();
            Writer.Whitespace();
            AppendNode(body);
        }

        /// <nodoc />
        protected void AppendModuleFlags(ModuleDeclaration node)
        {
            AppendFlags(node.Flags & ~NodeFlags.Export);
        }

        /// <nodoc />
        public virtual void VisitNamedExports(NamedExports node)
        {
            AppendList(node.Elements, ",", "{", "}", true, 5, false, n => AppendNode(n));
        }

        /// <nodoc />
        public virtual void VisitNewExpression(NewExpression node)
        {
            Writer
                .AppendToken("new").Whitespace();
            AppendNode(node.Expression);
            AppendTypeArguments(node.TypeArguments);
            Writer.AppendToken("(");
            AppendNodes(node.Arguments);
            Writer.AppendToken(")");
        }

        /// <nodoc />
        public virtual void VisitObjectLiteralExpression(ObjectLiteralExpression node)
        {
            AppendOptionalNode(node.Name);
            AppendList(
                node.Properties,
                separatorToken: ",",
                startBlockToken: ScriptWriter.StartBlockToken,
                endBlockToken: ScriptWriter.EndBlockToken,
                placeSeparatorOnLastElement: true,
                minimumCountBeforeNewLines: 2,
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void VisitParameterDeclaration(ParameterDeclaration node)
        {
            AppendFlags(node.Flags);
            AppendOptionalNode(node.DotDotDotToken.ValueOrDefault);
            AppendNode(node.Name);
            AppendOptionalNode(node.QuestionToken.ValueOrDefault);
            AppendOptionalNode(node.Type, ":");
            AppendOptionalNode(node.Initializer, "=");
        }

        /// <nodoc />
        public virtual void VisitParenthesizedExpression(ParenthesizedExpression node)
        {
            Writer.AppendToken("(");
            AppendNode(node.Expression);
            Writer.AppendToken(")");
        }

        /// <nodoc />
        public virtual void VisitParenthesizedTypeNode(ParenthesizedTypeNode node)
        {
            Writer.AppendToken("(");
            AppendNode(node.Type);
            Writer.AppendToken(")");
        }

        /// <nodoc />
        public virtual void VisitPostfixUnaryExpression(PostfixUnaryExpression node)
        {
            AppendNode(node.Operand);
            Writer.AppendToken(node.Operator.ToDisplayString());
        }

        /// <nodoc />
        public virtual void VisitPrefixUnaryExpression(PrefixUnaryExpression node)
        {
            Writer.AppendToken(node.Operator.ToDisplayString());
            AppendNode(node.Operand);
        }

        /// <nodoc />
        public virtual void VisitPrimaryExpression(PrimaryExpression node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.TrueKeyword:
                    Writer.AppendToken("true");
                    break;
                case SyntaxKind.FalseKeyword:
                    Writer.AppendToken("false");
                    break;
                case SyntaxKind.NullKeyword:
                    Writer.AppendToken("null");
                    break;
                case SyntaxKind.ThisKeyword:
                    Writer.AppendToken("this");
                    break;
                case SyntaxKind.SuperKeyword:
                    Writer.AppendToken("super");
                    break;
                default:
                    throw Contract.AssertFailure("Unexpected SyntaxKind for PrimaryExpression");
            }
        }

        /// <nodoc />
        public virtual void VisitPropertyAccessExpression(PropertyAccessExpression node)
        {
            AppendNode(node.Expression);

            if (node.DotToken == null)
            {
                Writer.AppendToken(".");
            }
            else
            {
                AppendNode(node.DotToken);
            }

            AppendNode(node.Name);
        }

        /// <nodoc />
        public virtual void VisitPropertyAssignment(PropertyAssignment node)
        {
            AppendNode(node.Name);

            AppendOptionalNode(node.QuestionToken.ValueOrDefault);
            Writer.AppendToken(":").Whitespace();
            AppendNode(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitPropertyDeclaration(PropertyDeclaration node)
        {
            AppendDecorators(node);
            Writer.AppendToken(node.Name.ToDisplayString());
            AppendOptionalNode(node.QuestionToken.ValueOrDefault);
            Writer.AppendToken(":").Whitespace();
            AppendNode(node.Type);
            AppendInitializerIfNeeded(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitPropertySignature(PropertySignature node)
        {
            AppendDecorators(node);
            Writer.AppendToken(node.Name.ToDisplayString());
            AppendOptionalNode(node.QuestionToken.ValueOrDefault);
            Writer.AppendToken(":").Whitespace();
            AppendNode(node.Type);
            AppendInitializerIfNeeded(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitQualifiedName(QualifiedName node)
        {
            AppendNode(node.Left);
            Writer.AppendToken(".");
            AppendNode(node.Right);
        }

        /// <nodoc />
        public virtual void VisitReturnStatement(ReturnStatement node)
        {
            Writer
                .AppendToken("return").Whitespace();
            AppendOptionalNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitSemicolonClassElement(SemicolonClassElement node)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public virtual void VisitShorthandPropertyAssignment(ShorthandPropertyAssignment node)
        {
            AppendOptionalNode(node.QuestionToken.ValueOrDefault);
            AppendOptionalNode(node.EqualsToken.ValueOrDefault);
            AppendOptionalNode(node.ObjectAssignmentInitializer);
            AppendOptionalNode(node.Name);
        }

        /// <nodoc />
        public virtual void VisitSpreadElementExpression(SpreadElementExpression node)
        {
            Writer
                .AppendToken("...");
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitStatement(Statement node)
        {
            // Statement is only used for missing declarations.
            // The only thing that we can do is to get string representation
            // from ToDisplayString method that will ended up with 'missing node' text.
            Writer.AppendToken(node.ToDisplayString());
        }

        /// <nodoc />
        public virtual void VisitStringLiteralTypeNode(StringLiteralTypeNode node)
        {
            if (!node.Decorators.IsNullOrEmpty())
            {
                // Need to add a new line to get: type foo =\r\n@@foo()\r\n'foo';
                Writer.NewLine();
                AppendDecorators(node);
            }

            var quote = node.LiteralKind == LiteralExpressionKind.SingleQuote ? '\'' : '\"';
            Writer.AppendQuotedString(node.Text, isPathString: false, quote: quote);
        }

        /// <nodoc />
        public virtual void VisitSwitchStatement(SwitchStatement node)
        {
            Writer
                .AppendToken("switch")
                .Whitespace()
                .AppendToken("(");
            AppendNode(node.Expression);
            Writer.AppendToken(")")
                .Whitespace();
            AppendNode(node.CaseBlock);
        }

        /// <nodoc />
        public virtual void VisitTaggedTemplateExpression(TaggedTemplateExpression node)
        {
            AppendNode(node.Tag);
            AppendNode(node.TemplateExpression);
        }

        /// <nodoc />
        public virtual void VisitTemplateExpression(TemplateExpression node)
        {
            Writer.AppendToken("`");
            AppendNode(node.Head);

            if (!node.TemplateSpans.IsNullOrEmpty())
            {
                foreach (var span in node.TemplateSpans.AsStructEnumerable())
                {
                    Writer.AppendToken("${");
                    AppendNode(span.Expression);
                    Writer.AppendToken("}");
                    AppendNode(span.Literal);
                }
            }

            Writer.AppendToken("`");
        }

        /// <nodoc />
        public virtual void VisitTemplateLiteralFragment(ITemplateLiteralFragment node)
        {
            Writer.AppendToken(node.Text);
        }

        /// <nodoc />
        public virtual void VisitTemplateSpan(TemplateSpan node)
        {
            AppendNode(node.Expression);
            if (node.Literal != null)
            {
                AppendNode(node.Literal);
            }
        }

        /// <nodoc />
        public virtual void VisitThisTypeNode(ThisTypeNode node)
        {
            throw new NotImplementedException("'this' keyword is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitThrowStatement(ThrowStatement node)
        {
            throw new NotImplementedException("'throw' statement is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitTokenNode(TokenNodeBase node)
        {
            Writer.AppendToken(node.Kind);
        }

        /// <nodoc />
        public virtual void VisitTryStatement(TryStatement node)
        {
            throw new NotImplementedException("'try' statement is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitTupleTypeNode(TupleTypeNode node)
        {
            Writer
                .AppendToken("[");
            AppendNodes(node.ElementTypes);
            Writer.AppendToken("]");
        }

        /// <nodoc />
        public virtual void VisitTypeAliasDeclaration(TypeAliasDeclaration node)
        {
            AppendDecorators(node);
            AppendFlags(node.Flags);
            Writer.AppendToken("type").Whitespace();
            AppendNode(node.Name);
            AppendGenericTypeParameters(node.TypeParameters);
            Writer.Whitespace();
            Writer.AppendToken("=").Whitespace();
            AppendNode(node.Type);
        }

        /// <nodoc />
        public virtual void VisitTypeAssertion(TypeAssertion node)
        {
            // <Type>expr;
            Writer
                .AppendToken("<");
            AppendNode(node.Type);
            Writer.AppendToken(">");
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitTypeLiteralNode(TypeLiteralNode node)
        {
            VisitTypeLiteralNode(node, 3); // For type literals if there are more than 3 elements always split on new lines.
        }

        /// <nodoc />
        public virtual void VisitTypeLiteralNode(ITypeLiteralNode node, int minimumCountBeforeNewLines)
        {
            AppendOptionalNode(node.Name);

            AppendList(
                node.Members,
                separatorToken: ScriptWriter.SeparateTypeLiteralToken,
                startBlockToken: ScriptWriter.StartTypeLiteralToken,
                endBlockToken: ScriptWriter.EndTypeLiteralToken,
                placeSeparatorOnLastElement: true,
                minimumCountBeforeNewLines: minimumCountBeforeNewLines,
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void VisitTypeNode(TypeNode node)
        {
            Writer.AppendToken(node.Kind);
        }

        /// <nodoc />
        public virtual void VisitTypeOfExpression(TypeOfExpression node)
        {
            Writer.AppendToken("typeof").Whitespace();
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitTypeParameterDeclaration(TypeParameterDeclaration node)
        {
            AppendOptionalNode(node.Name);
        }

        /// <nodoc />
        public virtual void VisitTypePredicateNode(TypePredicateNode node)
        {
            AppendNode(node.ParameterName);

            Writer.Whitespace()
                .AppendToken("is").Whitespace();
            AppendNode(node.Type);
        }

        /// <nodoc />
        public virtual void VisitTypeQueryNode(TypeQueryNode node)
        {
            Writer
                .AppendToken("typeof").Whitespace();
            AppendNode(node.ExprName);
        }

        /// <nodoc />
        public virtual void VisitTypeReferenceNode(TypeReferenceNode node)
        {
            AppendNode(node.TypeName);
            AppendGenericTypeParameters(node.TypeArguments);
        }

        /// <nodoc />
        public virtual void VisitUnionOrIntersectionTypeNode(UnionOrIntersectionTypeNode node)
        {
            AppendNodes(node.Types, " |");
        }

        /// <nodoc />
        public virtual void VisitVariableDeclaration(VariableDeclaration node)
        {
            AppendFlags(node.Flags);
            AppendNode(node.Name);
            if (node.Type != null)
            {
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }

            AppendInitializerIfNeeded(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitVariableDeclarationList(VariableDeclarationList node)
        {
            // Declaration list has following structure
            // (flags) (comma separated list of (names (:type) (= initializer));

            // var statement doesn't have flags
            var declarationToken = node.Flags == NodeFlags.None ? "var" : node.Flags.ToDisplayString();
            Writer.AppendToken(declarationToken).Whitespace();

            bool needsComma = false;
            for (int i = 0; i < node.Declarations.Length; i++)
            {
                // Need to add comma separator between variable declarations,
                // and writ optional initializer for the last declaration
                if (needsComma)
                {
                    Writer.AppendToken(",").Whitespace();
                }

                AppendNode(node.Declarations[i]);
                needsComma = true;
            }

            if (node.Declarations.HasTrailingComma)
            {
                Writer.AppendToken(",").Whitespace();
            }
        }

        /// <nodoc />
        public virtual void VisitVariableStatement(VariableStatement node)
        {
            // TODO: add comment! export modifier is applied on the statement, not on the declaration list!
            AppendDecorators(node);
            AppendFlags(node.Flags);
            AppendNode(node.DeclarationList);
        }

        /// <nodoc />
        public virtual void VisitVoidExpression(VoidExpression node)
        {
            AppendNode(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitWhileStatement(WhileStatement node)
        {
            Writer.AppendToken("while").Whitespace().AppendToken("(");
            AppendNode(node.Expression);
            Writer.AppendToken(")").Whitespace();
            AppendNode(node.Statement);
        }

        /// <nodoc />
        public virtual void VisitWithStatement(WithStatement node)
        {
            throw new NotImplementedException("'with' statement is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitYieldExpression(YieldExpression node)
        {
            throw new NotImplementedException("'yield' expression is not supported in DScript");
        }

        /// <nodoc />
        public virtual void VisitSourceFile(SourceFile node)
        {
            if (node.Statements.IsNullOrEmpty())
            {
                return;
            }

            AppendSourceFileStatements(node, node.Statements);
        }

        /// <nodoc />
        protected void AppendSourceFileStatements(ISourceFile sourceFile, [JetBrains.Annotations.NotNull] INodeArray<IStatement> statements)
        {
            for (int i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];

                Writer.TryToPreserveNewLines(statement);

                if (Api.Extensions.IsInjectedForDScript(statement))
                {
                    continue;
                }

                AppendNode(statement, skipTrailingComments: true);
                if (statement.NeedSeparatorAfter())
                {
                    Writer.NoNewLine().AppendToken(";");
                }

                Writer.TryWriteTrailingComments(statement);

                if (i != sourceFile.Statements.Count - 1 && statement.Kind != SyntaxKind.BlankLineStatement)
                {
                    Writer.NewLine();
                }

                // Since the list of statements could be big, we shortcut the visitation if anybody requested a cancellation
                if (CancellationRequested)
                {
                    return;
                }
            }
        }

        /// <nodoc />
        public virtual void VisitCommentExpression(ICommentExpression node)
        {
            Writer.AppendToken(node.Text).NewLine();
        }

        /// <nodoc />
        public virtual void VisitCommentStatement(CommentStatement node)
        {
            AppendNode(node.CommentExpression);
        }

        /// <nodoc />
        public virtual void PrintClassLikeDeclaration(ClassLikeDeclarationBase node)
        {
            AppendDecorators(node);
            AppendFlags(node.Flags);
            Writer.AppendToken("class").Whitespace();

            // Name would be missing for class expressions
            AppendOptionalNode(node.Name);

            if (!node.HeritageClauses.IsNullOrEmpty())
            {
                Writer.Whitespace();
                AppendNodes(node.HeritageClauses);
                Writer.Whitespace();
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            Writer.Whitespace(); // whitespace is required between interface name and open curly
            AppendBlock(node.Members);
        }

        /// <nodoc />
        protected virtual void AppendNode([JetBrains.Annotations.NotNull] INode node, bool skipTrailingComments = false)
        {
            // If cancellation was requested, then we don't visit nodes anymore
            if (CancellationRequested)
            {
                return;
            }

            // We don't print injected nodes
            if (!Api.Extensions.IsInjectedForDScript(node))
            {
                Writer.TryWriteLeadingComments(node);
                node.Cast<IVisitableNode>().Accept(this);
                if (!skipTrailingComments)
                {
                    Writer.TryWriteTrailingComments(node);
                }
            }
        }

        /// <nodoc />
        public virtual void AppendGenericTypeParameters(INodeArray<INode> types)
        {
            if (types != null && types.Count > 0)
            {
                Writer.AppendToken("<");
                AppendNodes(types);
                Writer.AppendToken(">");
            }
        }

        /// <nodoc />
        public virtual void AppendInitializerIfNeeded(IExpression initializer)
        {
            if (initializer != null)
            {
                // If there are multiple lines of types written, continue initialization on the same line.
                Writer.NoNewLine()
                    .Whitespace()
                    .AppendToken("=")
                    .Whitespace();
                AppendNode(initializer);
            }
        }

        /// <nodoc />
        public virtual void AppendFlags(NodeFlags flags)
        {
            if (flags != NodeFlags.None)
            {
                Writer.AppendToken(flags.ToDisplayString()).Whitespace();
            }
        }

        /// <nodoc />
        public virtual void AppendOptionalNode(INode node, string prefix)
        {
            if (node != null)
            {
                Writer.AppendToken(prefix)
                    .Whitespace();
                AppendOptionalNode(node);
            }
        }

        /// <nodoc />
        public virtual void AppendNodes(INodeArray<INode> nodes, string separator = ",", bool noNewLine = true)
        {
            if (nodes.IsNullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                AppendOptionalNode(nodes[i]);

                if (i != nodes.Count - 1)
                {
                    bool newLineRequired = noNewLine || nodes[i] == null ? false : nodes[i].NeedToAddNewLineAfter() && nodes[i].NeedSeparatorAfter();

                    Writer.AppendWithNewLineIfNeeded(separator, newLineRequired: newLineRequired);

                    if (!newLineRequired && !string.IsNullOrEmpty(separator))
                    {
                        Writer.Whitespace();
                    }
                }
            }
        }

        /// <nodoc />
        public virtual void AppendOptionalNode(INode node)
        {
            if (node != null)
            {
                AppendNode(node);
            }
        }

        /// <nodoc />
        public virtual void AppendTypeParameters([CanBeNull] INodeArray<ITypeParameterDeclaration> typeParameters)
        {
            if (!typeParameters.IsNullOrEmpty())
            {
                Writer.AppendToken("<");
                AppendNodes(typeParameters, noNewLine: true);
                Writer.AppendToken(">");
            }
        }

        /// <nodoc />
        public virtual void AppendTypeArguments([CanBeNull] INodeArray<ITypeNode> typeParameters)
        {
            if (!typeParameters.IsNullOrEmpty())
            {
                Writer.AppendToken("<");
                AppendNodes(typeParameters);
                Writer.AppendToken(">");
            }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public virtual void AppendStatements([CanBeNull] INodeArray<IStatement> members, Block block)
        {
            using (Writer.Block())
            {
                if (!members.IsNullOrEmpty())
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        Writer.TryToPreserveNewLines(members[i]);

                        AppendNode(members[i], skipTrailingComments: true);
                        AppendSeparatorIfNeeded(members[i]);

                        // A block may have a big number of statements. Shortcuting
                        // visitation if anybody requested a cancellation
                        if (CancellationRequested)
                        {
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Appends a separator if the statement needs one
        /// </summary>
        protected void AppendSeparatorIfNeeded(IStatement statement)
        {
            if (statement.NeedSeparatorAfter())
            {
                Writer
                    .NoNewLine()
                    .AppendToken(Separator)
                    .TryWriteTrailingComments(statement)
                    .NewLine();
            }
        }

        /// <summary>
        /// Appends the designated separator to the writer
        /// </summary>
        protected static ScriptWriter AppendSeparatorToken(ScriptWriter writer)
        {
            return writer.AppendToken(Separator);
        }

        /// <nodoc />
        public virtual void AppendBlock([CanBeNull] INodeArray<INode> members)
        {
            AppendList(
                members,
                separatorToken: ScriptWriter.SeparateBlockToken,
                startBlockToken: ScriptWriter.StartBlockToken,
                endBlockToken: ScriptWriter.EndBlockToken,
                placeSeparatorOnLastElement: true,
                minimumCountBeforeNewLines: 0, // always use newlines
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void AppendArgumentsOrParameters([CanBeNull] INodeArray<INode> members)
        {
            AppendList(
                members,
                separatorToken: ScriptWriter.SeparateArgumentsToken,
                startBlockToken: ScriptWriter.StartArgumentsToken,
                endBlockToken: ScriptWriter.EndArgumentsToken,
                placeSeparatorOnLastElement: false,
                minimumCountBeforeNewLines: 5,
                printTrailingComments: true,
                visitItem: n => AppendNode(n, skipTrailingComments: true));
        }

        /// <nodoc />
        public virtual void AppendList(
            [CanBeNull] INodeArray<INode> members,
            string separatorToken,
            string startBlockToken,
            string endBlockToken,
            bool placeSeparatorOnLastElement,
            int minimumCountBeforeNewLines,
            bool printTrailingComments,
            [JetBrains.Annotations.NotNull] Action<INode> visitItem)
        {
            var useNewLine = true;
            if (members != null && members.Count <= minimumCountBeforeNewLines)
            {
                int space = LengthPredictor.FitsOnOneLine(members, separatorToken.Length, Writer.CharactersRemainingOnCurrentLine);
                useNewLine = space < 0;
            }

            using (Writer.Block(useNewLine, startBlockToken, endBlockToken))
            {
                AppendListMembers(members, separatorToken, placeSeparatorOnLastElement, useNewLine, printTrailingComments, visitItem);
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private void AppendListMembers(INodeArray<INode> members, string separatorToken, bool placeSeparatorOnLastElement, bool useNewLine, bool printTrailingComments, [JetBrains.Annotations.NotNull] Action<INode> visitItem)
        {
            if (!members.IsNullOrEmpty())
            {
                Contract.Assert(members != null);

                // DScript-specific. Tries to get the first statement before the first element
                // of the list by going to the parent of the first member.
                // This in general is not always reliable for computing where to start, but
                // ShouldTryToPreserveNewLines should take care of ruling out the cases we don't handle yet.
                var sourceFile = members[0].GetSourceFile();

                for (int i = 0; i < members.Count; i++)
                {
                    if (AttemptToPreserveNewlinesForListMembers && ShouldTryToPreserveNewLines(sourceFile, members[i].Parent))
                    {
                        Writer.TryToPreserveNewLines(members[i]);
                    }

                    visitItem(members[i]);

                    Writer.NoNewLine(); // need to remove new line marker even if member think it is needed

                    // we should consider adding a separator if we have more elements, or we are writing per-line and we'd like a separator at the end of the list.
                    var needSeparator = i < members.Count - 1 || (placeSeparatorOnLastElement && useNewLine);
                    if (needSeparator && members[i].NeedSeparatorAfter())
                    {
                        if (!useNewLine)
                        {
                            // If we are not on newlines and we print trailing comments, they are before the separator
                            Writer.TryWriteTrailingComments(members[i]);
                        }

                        Writer.NoWhitespace().AppendToken(separatorToken);
                        if (!useNewLine)
                        {
                            Writer.Whitespace();
                        }
                        else
                        {
                            // If we are printing newlines, we will print after the separator.
                            Writer.TryWriteTrailingComments(members[i]);
                        }
                    }

                    // Print newline if needed. We need to skip this for injected nodes that don't print unless it is the last one.
                    if (useNewLine && (!Api.Extensions.IsInjectedForDScript(members[i]) || i == members.Count - 1))
                    {
                        Writer.NoWhitespace().AppendLine(string.Empty);
                    }
                }

                if (!useNewLine)
                {
                    Writer.NoNewLine();
                }
            }
        }

        /// <summary>
        /// We can only preserve new lines if the source file and container are present. Furthermore,
        /// we bail out as well when we are under a call expression, since computing the container position
        /// is not accurate (we should check the position of the functor instead)
        /// </summary>
        /// <remarks>DScript-specific</remarks>
        /// TODO: Check if there are other cases under which we should abandon as well.
        private static bool ShouldTryToPreserveNewLines(ISourceFile sourceFile, INode container)
        {
            return sourceFile != null && container != null && container.Kind != SyntaxKind.CallExpression;
        }

        /// <nodoc />
        public virtual void AppendDecorators(INode node)
        {
            Contract.Requires(node != null);

            if (!node.Decorators.IsNullOrEmpty())
            {
                foreach (var decorator in node.Decorators)
                {
                    AppendNode(decorator);
                }

                Writer.NewLine();
            }
        }

        #region Functions, Methods

        /// <nodoc />
        public virtual void VisitFunctionDeclaration(FunctionDeclaration node)
        {
            AppendDecorators(node);
            AppendFlags(node.Flags);
            Writer.AppendToken("function").Whitespace();
            AppendOptionalNode(node.Name);
            AppendTypeParameters(node.TypeParameters);
            AppendArgumentsOrParameters(node.Parameters);

            // The type or body curly should not have a newline
            Writer.NoNewLine();

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.Whitespace();
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            var body = node.Cast<IFunctionLikeDeclaration>().Body;
            if (body != null)
            {
                Writer
                    .Whitespace();
                AppendNode(body);
            }
        }

        /// <nodoc />
        public virtual void VisitFunctionExpression(FunctionExpression node)
        {
            AppendFlags(node.Flags);
            Writer.AppendToken("function");
            AppendTypeParameters(node.TypeParameters);
            AppendArgumentsOrParameters(node.Parameters);

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            Writer
                .Whitespace();
            AppendNode(node.Body);
        }

        /// <nodoc />
        public virtual void VisitArrowFunction(ArrowFunction node)
        {
            VisitArrowFunction(node, n => AppendNode(n));
        }

        /// <nodoc />
        public virtual void VisitArrowFunction(IArrowFunction node, Action<INode> visitBody)
        {
            AppendFlags(node.Flags);
            AppendTypeParameters(node.TypeParameters);

            if (node.Parameters?.Count == 1 && node.Parameters[0].Type == null)
            {
                // for one parameter with no type annotation special syntax could be used for lambda: n => statement;
                AppendNode(node.Parameters[0]);
            }
            else
            {
                // For 0 and more than 1 argument, braces should be used: () => statement; or (n, z) => statement;
                AppendArgumentsOrParameters(node.Parameters);
            }

            // Need to print body if it is an expression body.
            if (node.Body.Node is IExpression)
            {
                Writer.Whitespace().AppendToken("=>").Whitespace();
                visitBody(node.Body);
            }

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            if (node.Body.Node is IBlock)
            {
                Writer.Whitespace().AppendToken("=>").Whitespace();
                visitBody(node.Body);
            }
        }

        /// <nodoc />
        public virtual void VisitConstructorDeclaration(ConstructorDeclaration node)
        {
            AppendFlags(node.Flags);
            Writer.AppendToken("constructor");
            AppendTypeParameters(node.TypeParameters);
            Writer.AppendToken("(");
            AppendNodes(node.Parameters);
            Writer.AppendToken(")");

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            Writer
                .Whitespace();
            AppendNode(node.Cast<IFunctionLikeDeclaration>().Body);
        }

        /// <nodoc />
        public virtual void VisitMethodDeclaration(MethodDeclaration node)
        {
            AppendFlags(node.Flags);
            AppendNode(node.Name);
            AppendTypeParameters(node.TypeParameters);
            Writer.AppendToken("(");
            AppendNodes(node.Parameters);
            Writer.AppendToken(")");

            // type is a return type of the function. It is optional.
            if (node.Type != null)
            {
                Writer.AppendToken(":").Whitespace();
                AppendNode(node.Type);
            }

            if (OnlyPrintHeader)
            {
                return;
            }

            Writer.Whitespace();
            AppendNode(node.Body);
        }

        #endregion

        #region Imports

        /// <nodoc />
        public virtual void VisitImportDeclaration(ImportDeclaration node)
        {
            AppendDecorators(node);
            Writer.AppendToken("import").Whitespace();
            AppendOptionalNode(node.ImportClause);
            Writer.AppendToken("from").Whitespace();
            AppendOptionalNode(node.ModuleSpecifier);
        }

        /// <nodoc />
        public virtual void VisitImportClause(ImportClause node)
        {
            AppendOptionalNode(node.Name);
            AppendNode(node.NamedBindings);
        }

        /// <nodoc />
        public virtual void VisitImportEqualsDeclaration(ImportEqualsDeclaration node)
        {
            if (node.Name != null)
            {
                AppendNode(node.Name);
                Writer.Whitespace();
            }

            if (node.ModuleReference != null)
            {
                AppendNode(node.ModuleReference);
            }
        }

        /// <nodoc />
        public virtual void VisitImportSpecifier(ImportSpecifier node)
        {
            if (node.PropertyName != null)
            {
                AppendNode(node.PropertyName);

                Writer.Whitespace()
                    .AppendToken("as").Whitespace();
            }

            AppendNode(node.Name);
        }

        /// <nodoc />
        public virtual void VisitNamedImports(NamedImports node)
        {
            Writer
                .AppendToken("{");
            AppendNodes(node.Elements);
            Writer.AppendToken("}").Whitespace();
        }

        /// <nodoc />
        public virtual void VisitNamespaceImport(NamespaceImport node)
        {
            Writer
                .AppendToken("*").Whitespace();

            if (!node.IsImport)
            {
                Writer.AppendToken("as").Whitespace();
                AppendNode(node.Name);
                Writer.Whitespace();
            }
        }

        #endregion

        #region DScript specific nodes

        /// <nodoc />
        public virtual void VisitPathLikeLiteral(IPathLikeLiteralExpression node)
        {
            Writer.AppendQuotedString(node.Text, isPathString: false, quote: '`');
        }

        #endregion
    }
}
