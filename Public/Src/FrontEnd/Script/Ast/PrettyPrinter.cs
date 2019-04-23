// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Expressions.CompositeExpressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// PrettyPrinter visitor.
    /// </summary>
    public class PrettyPrinter : Visitor
    {
        /// <summary>
        /// Size of indentation in number of spaces
        /// </summary>
        private const int IndentSize = 4;

        private const string DecoratorMarker = "@@";

        /// <summary>
        /// Size of threshold where we will prefer wrapping things on new lines.
        /// </summary>
        private readonly int m_wrapThreshold;

        /// <summary>
        /// Number of elements in lists that can be printed inline. Longer enumerations are printed one element per line.
        /// </summary>
        private readonly int m_wrapItemNumberThreshold;

        /// <summary>
        /// The current position of the line;
        /// </summary>
        /// <remarks>
        /// This is used in decisions to place things on next lines or not.
        /// </remarks>
        private int m_currentPosition;

        /// <summary>
        /// Current indentation level
        /// </summary>
        private int m_currentIndent;

        /// <summary>
        /// Writter used for output of pretty printing
        /// </summary>
        private readonly TextWriter m_textWriter;

        /// <summary>
        /// Front-end context.
        /// </summary>
        private readonly FrontEndContext m_frontEndContext;

        /// <summary>
        /// The current folder used for printing absolute paths as relative ones.
        /// </summary>
        private readonly AbsolutePath m_currentFolder;

        /// <summary>
        /// The current root (package or config) used for printing absolute paths as relative ones.
        /// </summary>
        private readonly AbsolutePath m_currentRoot;

        private string Indent => !m_oneLiner ? new string(' ', m_currentIndent * IndentSize) : " ";

        private readonly bool m_oneLiner;

        /// <summary>
        /// The number of characters printed since the last newline (or beginning of the stream).
        /// </summary>
        protected int CurrentPosition => m_currentPosition;

        /// <nodoc />
        public PrettyPrinter(
            FrontEndContext frontEndContext,
            TextWriter textWriter,
            AbsolutePath currentFolder,
            AbsolutePath currentRoot,
            int currentIndent = 0,
            int wrapThreshold = 80,
            int wrapItemNumberThreshold = 2,
            bool oneLiner = false)
        {
            Contract.Requires(frontEndContext != null);
            Contract.Requires(textWriter != null);
            Contract.Requires(currentFolder.IsValid);

            m_frontEndContext = frontEndContext;
            m_textWriter = textWriter;
            m_currentFolder = currentFolder;
            m_currentRoot = currentRoot;
            m_currentIndent = currentIndent;
            m_oneLiner = oneLiner;
            m_wrapThreshold = wrapThreshold;
            m_wrapItemNumberThreshold = wrapItemNumberThreshold;
        }

        #region Utilities

        /// <summary>
        /// Print a full symbol (as dotted string) directly to the output stream.
        /// </summary>
        /// <param name="s">Some full symbol.</param>
        protected void Print(FullSymbol s)
        {
            Print(s.ToString(m_frontEndContext.SymbolTable));
        }

        /// <summary>
        /// Print a string id (as string) directly to the output stream.
        /// </summary>
        /// <param name="s">Some string id</param>
        protected void Print(StringId s)
        {
            Print(s.ToString(m_frontEndContext.StringTable));
        }

        /// <summary>
        /// Print a symbol atom (as string) directly to the output stream.
        /// </summary>
        /// <param name="s">Some symbol atom</param>
        protected void Print(SymbolAtom s)
        {
            Print(s.ToString(m_frontEndContext.SymbolTable));
        }

        /// <summary>
        /// Print a string directly to the output stream.
        /// </summary>
        /// <param name="s">Some string</param>
        protected void Print(string s)
        {
            Contract.Assume(!s.Contains(Environment.NewLine));

            m_currentPosition += s.Length;
            m_textWriter.Write(s);
        }

        /// <summary>
        /// Print a new line character (combination) directly to the output stream
        /// </summary>
        protected void PrintNewLine()
        {
            m_currentPosition = 0;

            if (!m_oneLiner)
            {
                m_textWriter.Write(Environment.NewLine);
            }
        }

        /// <summary>
        /// Print a string, then print a new line directly to the output stream.
        /// </summary>
        /// <param name="s">Some string</param>
        protected void PrintThenNewLine(string s)
        {
            Print(s);
            PrintNewLine();
        }

        /// <summary>
        /// Print a string to the output stream, preceded by an appropriate amount of whitespace for the current indentation level.
        /// </summary>
        /// <param name="s">Some string</param>
        protected void PrintIndented(string s)
        {
            Print(Indent);
            Print(s);
        }

        /// <summary>
        /// Print a string to the output stream, preceded by an appropriate amount of whitespace for the current indentation level. Then print a newline.
        /// </summary>
        /// <param name="s">Some string</param>
        protected void PrintIndentedThenNewLine(string s)
        {
            Print(Indent);
            Print(s);
            PrintNewLine();
        }

        /// <summary>
        /// Print a list of items to the output stream.
        /// </summary>
        /// <remarks>
        /// Currently, up to two items will be printed inline (unless oneLineRequired is specified), whereas longer lists are broken up into a line-by-line representation.
        /// </remarks>
        /// <typeparam name="T">Type of elements to print</typeparam>
        /// <param name="startSymbol">Prefix of list to print (e.g., "[" for arrays, or "(" for parameter lists)</param>
        /// <param name="endSymbol">Suffix of list to print (e.g., "]" for arrays, or ")" for parameter lists)</param>
        /// <param name="collection">Actual collection of elements to print</param>
        /// <param name="doPrintjob">Closure that does the actual printing of elements</param>
        /// <param name="separator">Separator character used between printed elements</param>
        /// <param name="oneLineRequired">If true, forces all elements to be printed in one line (useful for lists of formal parameters and things)</param>
        /// <param name="exactSeparator">If true, forces that only the separator is printed between elements (i.e., no additional whitespace)</param>
        protected void PrintList<T>(
            string startSymbol,
            string endSymbol,
            IReadOnlyList<T> collection,
            Action<T> doPrintjob,
            string separator = ",",
            bool oneLineRequired = false,
            bool exactSeparator = false)
        {
            // If there are more than WrapItemNumberThreshold values, just list each of them in a new line.
            // TODO: Decide whether the elements should be listed or written next to each other based on the text length instead of the number of elements.
            int size = collection.Count;

            Print(startSymbol);

            bool isMultipleLines = m_currentPosition >= m_wrapThreshold || (size > m_wrapItemNumberThreshold && !oneLineRequired);

            // Never line-break for an empty list:
            if (collection.Count == 0)
            {
                isMultipleLines = false;
            }

            if (isMultipleLines)
            {
                ++m_currentIndent;
                PrintNewLine();
                Print(Indent);
            }
            else if (!exactSeparator)
            {
                separator += " ";
            }

            for (int i = 0; i < size; i++)
            {
                doPrintjob(collection[i]);

                // if this is NOT the last element
                if (i != size - 1)
                {
                    Print(separator);
                    if (isMultipleLines)
                    {
                        PrintNewLine();
                        Print(Indent);
                    }
                }
            }

            if (isMultipleLines)
            {
                --m_currentIndent;
                PrintNewLine();
                Print(Indent);
            }

            Print(endSymbol);
        }

        /// <summary>
        /// Print a quoted path to the output stream, surrounded by a quoting character, and escaped.
        /// </summary>
        /// <param name="s">Some string</param>
        /// <param name="prefixCharacter">Character printed before the initial quote (for p`...`, f`...`, ...). If it is \0, it isn't printed.</param>
        protected void PrintQuotedPath(string s, char prefixCharacter)
        {
            PrintQuoted(s, '`', isInTheContextOfPathLikeInterpolation: true, markerCharacter: '\0', prefixCharacter: prefixCharacter);
        }

        /// <summary>
        /// Print a quoted string to the output stream, surrounded by a quoting character, and escaped.
        /// </summary>
        /// <param name="s">Some string</param>
        /// <param name="quote">The quote-delimiter character (i.e., usually '"'). If it is \0, it isn't printed.</param>
        /// <param name="isInTheContextOfPathLikeInterpolation">Escaping has its own especial rules when the quoted string represents a path</param>
        /// <param name="markerCharacter">Character printed after the initial quote, but before the actual string. If it is \0, it isn't printed.</param>
        /// <param name="prefixCharacter">Character printed before the initial quote (for p`...`, f`...`, ...). If it is \0, it isn't printed.</param>
        protected void PrintQuoted(string s, char quote, bool isInTheContextOfPathLikeInterpolation, char markerCharacter = '\0', char prefixCharacter = '\0')
        {
            if (prefixCharacter != '\0')
            {
                m_textWriter.Write(prefixCharacter);
                ++m_currentPosition;
            }

            if (quote != '\0')
            {
                m_textWriter.Write(quote);
                ++m_currentPosition;
            }

            if (markerCharacter != '\0')
            {
                m_textWriter.Write(markerCharacter);
                ++m_currentPosition;
            }

            PrintEscapedString(s, quote, isInTheContextOfPathLikeInterpolation);

            if (quote != '\0')
            {
                m_textWriter.Write(quote);
                ++m_currentPosition;
            }
        }

        /// <summary>
        /// Prints a string to the output stream, escaping some common non-printable characters.
        /// </summary>
        /// <param name="s">Some string</param>
        /// <param name="quote">If '\'', '"' or '\`', occurrences of this in s will be escaped as well. If '\`', '$' will be escaped as well. Otherwise ignored.</param>
        /// <param name = "isPathLike">In the context of a path-like interpolation, ` is escaped as `` and \ is not a escape character</param>
        protected void PrintEscapedString(string s, char quote, bool isPathLike)
        {
            foreach (char c in s)
            {
                string encoded = null;
                switch (c)
                {
                    case '\'':
                        encoded = quote == c ? @"\'" : null;
                        break;
                    case '\"':
                        encoded = quote == c ? @"\""" : null;
                        break;
                    case '`':
                        encoded = quote == c ? (isPathLike ? "``" : @"\`") : null;
                        break;
                    case '$':
                        encoded = quote == '`' ? (isPathLike ? "$" : @"\$") : null;
                        break;
                    case '\\':
                        encoded = isPathLike ? @"\" : @"\\";
                        break;
                    case '\n':
                        encoded = @"\n";
                        break;
                    case '\r':
                        encoded = @"\r";
                        break;
                    case '\t':
                        encoded = @"\t";
                        break;
                    case '\f':
                        encoded = @"\f";
                        break;
                    case '\b':
                        encoded = @"\b";
                        break;
                    case '\v':
                        encoded = @"\v";
                        break;
                }

                if (encoded != null)
                {
                    m_currentPosition += encoded.Length;
                    m_textWriter.Write(encoded);
                }
                else
                {
                    ++m_currentPosition;
                    m_textWriter.Write(c);
                }
            }
        }

        /// <summary>
        /// Helper method to print an optional list of generic arguments
        /// </summary>
        protected void PrintOptionalGenericTypeList(IReadOnlyList<Node> genericTypeList)
        {
            if (genericTypeList.Count > 0)
            {
                PrintList("<", ">", genericTypeList, type => type.Accept(this), oneLineRequired: true);
            }
        }

        /// <summary>
        /// Print a path directly to the output stream. Will be normalized relative to the given root of the project.
        /// </summary>
        /// <param name="path">Some absolute path</param>
        /// <param name="prefix">Prefix for factory method</param>
        protected void PrintPath(AbsolutePath path, char prefix = '\0')
        {
            prefix = prefix == '\0' ? Constants.Names.PathInterpolationFactory : prefix;

            if (m_currentFolder == path)
            {
                Print(prefix + "`.`");
            }
            else if (m_currentFolder.TryGetRelative(m_frontEndContext.PathTable, path, out RelativePath relativePath))
            {
                PrintQuotedPath(
                    PathUtil.NormalizePath(relativePath.ToString(m_frontEndContext.StringTable, PathFormat.Script)),
                    prefixCharacter: prefix);
            }
            else if (m_currentRoot.IsValid && m_currentRoot.TryGetRelative(m_frontEndContext.PathTable, path, out relativePath))
            {
                PrintQuotedPath(
                    PathFormatter.GetPathSeparator(PathFormat.Script) +
                    PathUtil.NormalizePath(relativePath.ToString(m_frontEndContext.StringTable, PathFormat.Script)),
                    prefixCharacter: prefix);
            }
            else
            {
                PrintQuotedPath(PathUtil.NormalizePath(path.ToString(m_frontEndContext.PathTable, PathFormat.Script)), prefixCharacter: prefix);
            }
        }

        #endregion Utilities

        #region Type visits

        ////////////// Types.

        /// <inheritdoc />
        public override void Visit(ArrayType arrayType)
        {
            arrayType.ElementType.Accept(this);
            Print("[]");
        }

        /// <inheritdoc />
        public override void Visit(CallSignature callSignature)
        {
            PrintOptionalGenericTypeList(callSignature.TypeParameters);
            PrintList("(", ")", callSignature.Parameters, parameter => parameter.Accept(this));

            if (callSignature.ReturnType != null)
            {
                Print(": ");
                callSignature.ReturnType.Accept(this);
            }
        }

        /// <inheritdoc />
        public override void Visit(FunctionType functionType)
        {
            PrintOptionalGenericTypeList(functionType.TypeParameters);
            PrintList("(", ")", functionType.Parameters, parameter => parameter.Accept(this));

            if (functionType.ReturnType != null)
            {
                Print(" => ");
                functionType.ReturnType.Accept(this);
            }
        }

        /// <inheritdoc />
        public override void Visit(NamedTypeReference namedTypeReference)
        {
            PrintList(string.Empty, string.Empty, namedTypeReference.TypeName, Print, ".", oneLineRequired: true, exactSeparator: true);
            PrintOptionalGenericTypeList(namedTypeReference.TypeArguments);
        }

        /// <inheritdoc />
        public override void Visit(TypeQuery typeQuery)
        {
            Print("typeof ");
            typeQuery.EntityName.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(ObjectType objectType)
        {
            if (objectType.Members.Count == 0)
            {
                Print("{}");
            }
            else
            {
                PrintList(
                    "{",
                    "}",
                    objectType.Members,
                    member =>
                    {
                        // Have to do a funy visit here. Can't call accept becaue it would only visit the type member.
                        // I want the actual visit method of the concrete type to be callsed i.e., do a
                        // 'virtual dispatch on the derived types of the member' i.e., PropertySignature
                        // But that doesn't work... So I'll have to do a call to Visit instead of accept
                        if (member is PropertySignature propertySignature)
                        {
                            Visit(propertySignature);
                            return;
                        }

                        if (member is CallSignature callSignature)
                        {
                            Visit(callSignature);
                            return;
                        }

                        Contract.Assert(false, "Unexpected signature type");
                    },
                    ";");
            }
        }

        /// <inheritdoc />
        public override void Visit(Parameter parameter)
        {
            if (parameter.ParameterName.IsValid)
            {
                var kind = parameter.ParameterKind;
                if (kind == ParameterKind.Rest)
                {
                    Print("...");
                }

                Print(parameter.ParameterName);

                if (kind == ParameterKind.Optional)
                {
                    Print("?");
                }

                if (parameter.ParameterType != null)
                {
                    Print(": ");
                }
            }

            parameter.ParameterType?.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(PrimitiveType primitiveType)
        {
            string kind = primitiveType.ToDebugString();
            Print(kind);
        }

        /// <inheritdoc />
        public override void Visit(PropertySignature propertySignature)
        {
            if (propertySignature.Decorators.Count > 0)
            {
                foreach (var decorator in propertySignature.Decorators)
                {
                    Print(DecoratorMarker);
                    decorator.Accept(this);
                    PrintNewLine();
                    Print(Indent);
                }
            }

            Print(propertySignature.PropertyName);
            if (propertySignature.IsOptional)
            {
                Print("?");
            }

            Print(": ");
            propertySignature.PropertyType.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(TupleType tupleType)
        {
            PrintList("[", "]", tupleType.ElementTypes, type => type.Accept(this));
        }

        /// <inheritdoc />
        public override void Visit(TypeParameter typeParameter)
        {
            Print(typeParameter.ParameterName);
            if (typeParameter.ExtendedType != null)
            {
                Print(" extends ");
                typeParameter.ExtendedType.Accept(this);
            }
        }

        /// <inheritdoc />
        public override void Visit(UnionType unionType)
        {
            PrintList(string.Empty, string.Empty, unionType.Types, type => type.Accept(this), " | ", oneLineRequired: true, exactSeparator: true);
        }

        #endregion Type visits

        #region Expression visits

        ////////////// Expressions.

        /// <inheritdoc />
        public override void Visit(NameBasedSymbolReference nameBasedSymbolReference)
        {
            Print(nameBasedSymbolReference.Name);
        }

        /// <inheritdoc />
        public override void Visit(LocationBasedSymbolReference locationBasedSymbolReference)
        {
            Print(locationBasedSymbolReference.Name);
        }

        /// <inheritdoc />
        public override void Visit(ModuleIdExpression idExpression)
        {
            Print(idExpression.Name);
        }

        /// <inheritdoc />
        public override void Visit(LocalReferenceExpression localReferenceExpression)
        {
            Print(localReferenceExpression.Name);
        }

        /// <inheritdoc />
        public override void Visit(UnaryExpression unaryExpression)
        {
            Print(unaryExpression.OperatorKind.ToDisplayString());

            bool isNumberLiteralOrId = unaryExpression.Expression.Kind == SyntaxKind.NumberLiteral ||
                                       unaryExpression.Expression.Kind == SyntaxKind.BoolLiteral ||
                                       unaryExpression.Expression.Kind == SyntaxKind.FullNameBasedSymbolReference ||
                                       unaryExpression.Expression.Kind == SyntaxKind.ModuleReferenceExpression ||
                                       unaryExpression.Expression.Kind == SyntaxKind.NameBasedSymbolReference;

            if (!isNumberLiteralOrId || unaryExpression.OperatorKind == UnaryOperator.TypeOf)
            {
                Print("(");
            }

            unaryExpression.Expression.Accept(this);

            if (!isNumberLiteralOrId || unaryExpression.OperatorKind == UnaryOperator.TypeOf)
            {
                Print(")");
            }
        }

        /// <inheritdoc />
        public override void Visit(BinaryExpression binaryExpression)
        {
            bool isParenthesized = binaryExpression.LeftExpression.Kind == SyntaxKind.BinaryExpression;

            if (isParenthesized)
            {
                Print("(");
            }

            binaryExpression.LeftExpression.Accept(this);

            if (isParenthesized)
            {
                Print(")");
            }

            Print(" ");
            Print(binaryExpression.OperatorKind.ToDisplayString());
            Print(" ");

            isParenthesized = binaryExpression.RightExpression.Kind == SyntaxKind.BinaryExpression;
            if (isParenthesized)
            {
                Print("(");
            }

            binaryExpression.RightExpression.Accept(this);

            if (isParenthesized)
            {
                Print(")");
            }
        }

        /// <inheritdoc />
        public override void Visit(AssignmentExpression assignmentExpression)
        {
            Print(assignmentExpression.LeftExpression);
            Print(" ");
            Print(assignmentExpression.OperatorKind.ToDisplayString());
            Print(" ");
            assignmentExpression.RightExpression.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(IncrementDecrementExpression incrementDecrementExpression)
        {
            if ((incrementDecrementExpression.OperatorKind & IncrementDecrementOperator.PrefixPostfixMask) == IncrementDecrementOperator.Prefix)
            {
                Print(incrementDecrementExpression.OperatorKind.ToDisplayString());
            }

            Print(incrementDecrementExpression.Operand);
            if ((incrementDecrementExpression.OperatorKind & IncrementDecrementOperator.PrefixPostfixMask) == IncrementDecrementOperator.Postfix)
            {
                Print(incrementDecrementExpression.OperatorKind.ToDisplayString());
            }
        }

        /// <inheritdoc />
        public override void Visit(ConditionalExpression conditionalExpression)
        {
            conditionalExpression.ConditionExpression.Accept(this);
            Print(" ? ");
            conditionalExpression.ThenExpression.Accept(this);
            Print(" : ");
            conditionalExpression.ElseExpression.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(SwitchExpression switchExpression)
        {
            switchExpression.Expression.Accept(this);
            Print(" switch ");
            PrintThenNewLine("{");
            m_currentIndent++;

            foreach (var clause in switchExpression.Clauses) {
                clause.Accept(this);
                PrintThenNewLine(",");
            }

            m_currentIndent--;
            PrintThenNewLine("}");
        }

        /// <inheritdoc />
        public override void Visit(SwitchExpressionClause switchExpressionClause)
        {
            if (switchExpressionClause.IsDefaultFallthrough)
            {
                Print("default");
            }
            else
            {
                switchExpressionClause.Match.Accept(this);
            }

            Print(": ");
            switchExpressionClause.Expression.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(SelectorExpressionBase selectorExpression)
        {
            var thisExpression = selectorExpression.ThisExpression;

            bool printParen = thisExpression.Kind == SyntaxKind.BinaryExpression
                              || thisExpression.Kind == SyntaxKind.IteExpression
                              || thisExpression.Kind == SyntaxKind.SwitchExpression
                              || thisExpression.Kind == SyntaxKind.UnaryExpression
                              || thisExpression.Kind == SyntaxKind.LambdaExpression
                              || thisExpression.Kind == SyntaxKind.AssignmentExpression
                              || thisExpression.Kind == SyntaxKind.IncrementDecrementExpression;

            if (printParen)
            {
                Print("(");
            }

            thisExpression.Accept(this);

            if (printParen)
            {
                Print(")");
            }

            Print(".");
            Print(selectorExpression.Selector);
        }

        /// <inheritdoc />
        public override void Visit(ModuleSelectorExpression moduleSelectorExpression)
        {
            var thisExpression = moduleSelectorExpression.ThisExpression;

            bool printParen = thisExpression.Kind == SyntaxKind.BinaryExpression
                              || thisExpression.Kind == SyntaxKind.IteExpression
                              || thisExpression.Kind == SyntaxKind.SwitchExpression
                              || thisExpression.Kind == SyntaxKind.UnaryExpression
                              || thisExpression.Kind == SyntaxKind.LambdaExpression
                              || thisExpression.Kind == SyntaxKind.AssignmentExpression
                              || thisExpression.Kind == SyntaxKind.IncrementDecrementExpression;

            if (printParen)
            {
                Print("(");
            }

            thisExpression.Accept(this);

            if (printParen)
            {
                Print(")");
            }

            Print(".");
            Print(moduleSelectorExpression.Selector);
        }

        /// <inheritdoc />
        public override void Visit(ArrayExpression arrayExpression)
        {
            PrintList("[", "]", arrayExpression.Values, value => value.Accept(this));
        }

        /// <inheritdoc />
        public override void Visit(ArrayLiteral arrayLiteral)
        {
            PrintList("[", "]", arrayLiteral.Values, DoPrint);

            void DoPrint(EvaluationResult value)
            {
                if (value.Value is Expression e)
                {
                    e.Accept(this);
                }
                else
                {
                    Print(value.Value?.ToString() ?? "undefined");
                }
            }
        }

        /// <inheritdoc />
        public override void Visit(ObjectLiteralN value)
        {
            PrintList("{", "}", value.Members.ToList(), Print);

            void Print(KeyValuePair<StringId, EvaluationResult> kvp)
            {
                string name = kvp.Key.ToString(m_frontEndContext.StringTable);
                bool isValidId = StringUtil.IsValidId(name);

                if (!isValidId)
                {
                    this.Print("\"");
                }

                PrintEscapedString(name, '\"', isPathLike: false);

                if (!isValidId)
                {
                    this.Print("\"");
                }

                this.Print(": ");
                if (kvp.Value.Value is Expression e)
                {
                    e.Accept(this);
                }
                else
                {
                    this.Print(kvp.Value.Value?.ToString() ?? "undefined");
                }
            }
        }

        /// <inheritdoc />
        public override void Visit(ObjectLiteral0 value)
        {
            Print("{ }");
        }

        /// <inheritdoc />
        public override void Visit(ObjectLiteralSlim value)
        {
            value.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(IndexExpression indexExpression)
        {
            indexExpression.ThisExpression.Accept(this);
            Print("[");
            indexExpression.Index.Accept(this);
            Print("]");
        }

        /// <inheritdoc />
        public override void Visit(ApplyExpression applyExpression)
        {
            applyExpression.Functor.Accept(this);
            PrintList("(", ")", applyExpression.Arguments, argument => argument.Accept(this));
        }

        /// <inheritdoc />
        public override void Visit(ApplyExpressionWithTypeArguments applyExpression)
        {
            applyExpression.Functor.Accept(this);

            PrintOptionalGenericTypeList(applyExpression.TypeArguments);
            PrintList("(", ")", applyExpression.Arguments, argument => argument.Accept(this));
        }

        /// <inheritdoc />
        public override void Visit(FunctionLikeExpression lambdaExpression)
        {
            lambdaExpression.CallSignature.Accept(this);
            Print(" => ");

            if (lambdaExpression.Body != null)
            {
                lambdaExpression.Body.Accept(this);
            }
            else
            {
                Print(lambdaExpression.ToDebugString());
            }
        }

        /// <inheritdoc />
        public override void Visit(CastExpression castExpression)
        {
            bool shouldParenthesize = castExpression.Expression.Kind == SyntaxKind.BinaryExpression;
            string leftParen = shouldParenthesize ? "(" : string.Empty;
            string rightParen = shouldParenthesize ? ")" : string.Empty;

            if (castExpression.CastKind == CastExpression.TypeAssertionKind.TypeCast)
            {
                Print("<");
                castExpression.TargetType.Accept(this);
                Print(">");
                Print(leftParen);
                castExpression.Expression.Accept(this);
                Print(rightParen);
            }
            else if (castExpression.CastKind == CastExpression.TypeAssertionKind.AsCast)
            {
                Print(leftParen);
                castExpression.Expression.Accept(this);
                Print(rightParen);
                Print(" as ");
                castExpression.TargetType.Accept(this);
            }
            else
            {
                Contract.Assert(false);
            }
        }

        #endregion Expression visits

        #region Literal visits

        /// <inheritdoc />
        public override void Visit(PathLiteral pathLiteral)
        {
            PrintPath(pathLiteral.Value, prefix: Constants.Names.PathInterpolationFactory);
        }

        /// <inheritdoc />
        public override void Visit(ResolvedStringLiteral resolvedStringLiteral)
        {
            Print(resolvedStringLiteral.OriginalValue);
        }

        /// <inheritdoc />
        public override void Visit(PathAtomLiteral pathAtomLiteral)
        {
            PrintQuotedPath(pathAtomLiteral.Value.ToString(m_frontEndContext.StringTable), prefixCharacter: Constants.Names.PathAtomInterpolationFactory);
        }

        /// <inheritdoc />
        public override void Visit(RelativePathLiteral relativePathLiteral)
        {
            PrintQuotedPath(relativePathLiteral.Value.ToString(m_frontEndContext.StringTable).Replace('\\', '/'), prefixCharacter: Constants.Names.RelativePathInterpolationFactory);
        }

        /// <inheritdoc />
        public override void Visit(StringLiteral stringLiteral)
        {
            PrintQuoted(stringLiteral.Value, '\"', isInTheContextOfPathLikeInterpolation: false);
        }

        /// <inheritdoc />
        public override void Visit(BoolLiteral boolLiteral)
        {
            Print(boolLiteral.ToDebugString());
        }

        /// <inheritdoc />
        public override void Visit(UndefinedLiteral undefinedLiteral)
        {
            Print(undefinedLiteral.ToDebugString());
        }

        /// <inheritdoc />
        public override void Visit(NumberLiteral numberLiteral)
        {
            Print(numberLiteral.ToDebugString());
        }

        /// <inheritdoc />
        public override void Visit(FileLiteral fileLiteral)
        {
            PrintPath(fileLiteral.Value.Path, prefix: Constants.Names.FileInterpolationFactory);
        }

        /// <inheritdoc />
        public override void Visit(FileLiteralExpression fileLiteralExpression)
        {
            if (fileLiteralExpression.PathExpression is PathLiteral pathLiteral)
            {
                PrintPath(pathLiteral.Value, prefix: Constants.Names.FileInterpolationFactory);
            }
            else
            {
                Print(Constants.Names.FileInterpolationFactory + "`");
                fileLiteralExpression.PathExpression.Accept(this);
                Print("`");
            }
        }

        /// <inheritdoc />
        public override void Visit(DirectoryLiteralExpression directoryLiteralExpression)
        {
            if (directoryLiteralExpression.PathExpression is PathLiteral pathLiteral)
            {
                PrintPath(pathLiteral.Value, prefix: Constants.Names.FileInterpolationFactory);
            }
            else
            {
                Print(Constants.Names.FileInterpolationFactory + "`");
                directoryLiteralExpression.PathExpression.Accept(this);
                Print("`");
            }
        }

        #endregion

        #region Statement visits

        ////////////// Statements.

        /// <inheritdoc />
        public override void Visit(VarStatement varStatement)
        {
            // Technically, this is not correct, because local variable could be const or let.
            // TODO: consider adding const/non-const flags to VarStatement.
            PrintIndented("let ");

            Print(varStatement.Name);
            if (varStatement.Type != null)
            {
                Print(" : ");
                varStatement.Type.Accept(this);
            }

            if (varStatement.Initializer != null)
            {
                Print(" = ");
                varStatement.Initializer.Accept(this);
            }

            PrintThenNewLine(";");
        }

        /// <inheritdoc />
        public override void Visit(ExpressionStatement expressionStatement)
        {
            PrintIndented(string.Empty);
            expressionStatement.Expression.Accept(this);
            PrintThenNewLine(";");
        }

        /// <inheritdoc />
        public override void Visit(BlockStatement blockStatement)
        {
            PrintThenNewLine("{");
            ++m_currentIndent;

            foreach (var stmt in blockStatement.Statements)
            {
                stmt.Accept(this);
            }

            --m_currentIndent;
            PrintIndented("}");
        }

        /// <inheritdoc />
        public override void Visit(IfStatement ifStatement)
        {
            var thenStatement = ifStatement.ThenStatement;
            var elseStatement = ifStatement.ElseStatement;

            PrintIndented("if (");
            ifStatement.Condition.Accept(this);
            Print(") ");

            var isThenBlock = thenStatement.Kind == SyntaxKind.BlockStatement;
            if (!isThenBlock)
            {
                ++m_currentIndent;
                PrintNewLine();
            }

            thenStatement.Accept(this);

            if (isThenBlock)
            {
                PrintNewLine();
            }
            else
            {
                --m_currentIndent;
            }

            if (elseStatement != null)
            {
                var isElseBlock = elseStatement.Kind == SyntaxKind.BlockStatement;

                PrintIndented("else ");

                if (!isElseBlock)
                {
                    ++m_currentIndent;
                    PrintNewLine();
                }

                elseStatement.Accept(this);

                if (isElseBlock)
                {
                    PrintNewLine();
                }
                else
                {
                    --m_currentIndent;
                }
            }
        }

        /// <inheritdoc />
        public override void Visit(ReturnStatement returnStatement)
        {
            PrintIndented("return");

            if (returnStatement.ReturnExpression != null)
            {
                Print(" ");
                returnStatement.ReturnExpression.Accept(this);
            }

            PrintThenNewLine(";");
        }

        /// <inheritdoc />
        public override void Visit(SwitchStatement switchStatement)
        {
            PrintIndented("switch (");
            switchStatement.Control.Accept(this);
            PrintThenNewLine(") {");
            ++m_currentIndent;

            foreach (var caseClause in switchStatement.CaseClauses)
            {
                caseClause.Accept(this);
            }

            switchStatement.DefaultClause?.Accept(this);

            --m_currentIndent;
            PrintIndentedThenNewLine("}");
        }

        /// <inheritdoc />
        public override void Visit(CaseClause caseClause)
        {
            PrintIndented("case ");
            caseClause.CaseExpression.Accept(this);
            PrintThenNewLine(":");
            ++m_currentIndent;

            foreach (var stmt in caseClause.Statements)
            {
                stmt.Accept(this);
            }

            --m_currentIndent;
        }

        /// <inheritdoc />
        public override void Visit(DefaultClause defaultClause)
        {
            PrintIndentedThenNewLine("default:");
            ++m_currentIndent;

            foreach (var stmt in defaultClause.Statements)
            {
                stmt.Accept(this);
            }

            --m_currentIndent;
        }

        /// <inheritdoc />
        public override void Visit(BreakStatement breakStatement)
        {
            PrintIndentedThenNewLine("break;");
        }

        /// <inheritdoc />
        public override void Visit(ForStatement forStatement)
        {
            PrintIndented("for (");

            if (forStatement.Initializer is ExpressionStatement exprStatement
                && exprStatement.Expression is AssignmentExpression varAssignment)
            {
                Contract.Assume(varAssignment.OperatorKind == AssignmentOperator.Assignment);

                Print(varAssignment.LeftExpression);
                Print(" = ");
                varAssignment.RightExpression.Accept(this);
            }
            else if (forStatement.Initializer is VarStatement varDeclaration)
            {
                Print("let ");
                Print(varDeclaration.Name);
                Print(" = ");
                varDeclaration.Initializer?.Accept(this);
            }
            else
            {
                Contract.Assume(false);
            }

            Print("; ");
            forStatement.Condition?.Accept(this);
            Print("; ");
            forStatement.Incrementor?.Accept(this);
            Print(") ");

            forStatement.Body.Accept(this);
            PrintNewLine();
        }

        /// <inheritdoc />
        public override void Visit(ForOfStatement forOfStatement)
        {
            PrintIndented("for (");

            Contract.Assume(forOfStatement.Name.Initializer == null);

            Print("let ");
            Print(forOfStatement.Name.Name);
            Print(" of ");
            forOfStatement.Expression.Accept(this);
            Print(") ");

            forOfStatement.Body.Accept(this);
            PrintNewLine();
        }

        /// <inheritdoc />
        public override void Visit(WhileStatement whileStatement)
        {
            PrintIndented("while (");
            whileStatement.Condition.Accept(this);
            Print(") { ");

            ++m_currentIndent;
            PrintNewLine();

            whileStatement.Body.Accept(this);

            --m_currentIndent;
            PrintNewLine();
            PrintIndentedThenNewLine("}");
        }

        #endregion Statement visits

        #region Declaration visits

        ////////////// Declaration.

        /// <inheritdoc />
        public override void Visit(VarDeclaration varDeclaration)
        {
            PrintIndented(varDeclaration.GetModifierString());
            Print("const ");

            Print(varDeclaration.Name);
            if (varDeclaration.Type != null)
            {
                Print(" : ");
                varDeclaration.Type.Accept(this);
            }

            if (varDeclaration.Initializer != null)
            {
                Print(" = ");
                varDeclaration.Initializer.Accept(this);
            }

            PrintThenNewLine(";");
        }

        /// <inheritdoc />
        public override void Visit(FunctionDeclaration functionDeclaration)
        {
            PrintIndented(functionDeclaration.GetModifierString());
            Print("function ");
            Print(functionDeclaration.Name);
            functionDeclaration.CallSignature.Accept(this);
            Print(" ");

            if ((functionDeclaration.Modifier & Declaration.DeclarationFlags.Ambient) != 0)
            {
                PrintThenNewLine(";");
            }
            else
            {
                functionDeclaration.Body.Accept(this);
                PrintNewLine();
            }
        }

        /// <inheritdoc />
        public override void Visit(ConfigurationDeclaration configurationDeclaration)
        {
            Print(configurationDeclaration.ConfigKeyword);
            Print("(");
            configurationDeclaration.ConfigExpression.Accept(this);
            PrintThenNewLine(");");
        }

        /// <inheritdoc />
        public override void Visit(PackageDeclaration packageDeclaration)
        {
            foreach (var packageExpression in packageDeclaration.PackageExpressions)
            {
                Print(packageDeclaration.PackageKeyword);
                Print("(");
                packageExpression.Accept(this);
                PrintThenNewLine(");");
            }
        }

        /// <inheritdoc />
        public override void Visit(QualifierSpaceDeclaration qualifierSpaceDeclaration)
        {
            Print(qualifierSpaceDeclaration.QualifierSpaceKeyword);
            Print("(");
            qualifierSpaceDeclaration.QualifierSpaceExpression.Accept(this);
            PrintThenNewLine(");");
        }

        /// <inheritdoc />
        public override void Visit(ImportDeclaration importDeclaration)
        {
            if (importDeclaration.Decorators.Count > 0)
            {
                foreach (var decorator in importDeclaration.Decorators)
                {
                    Print(DecoratorMarker);
                    decorator.Accept(this);
                    PrintNewLine();
                    Print(Indent);
                }
            }

            if ((importDeclaration.Modifier & Declaration.DeclarationFlags.Export) != 0)
            {
                Print("export ");
            }

            PrintIndented("import ");
            importDeclaration.ImportOrExportClause.Accept(this);
            Print(" from ");
            importDeclaration.PathSpecifier.Accept(this);

            if (importDeclaration.Qualifier != null)
            {
                Print(" with ");
                importDeclaration.Qualifier.Accept(this);
            }

            PrintThenNewLine(";");
        }

        /// <summary>
        /// Visits export declaration.
        /// </summary>
        public override void Visit(ExportDeclaration exportDeclaration)
        {
            PrintIndented("export ");
            exportDeclaration.ImportOrExportClause.Accept(this);

            if (exportDeclaration.PathSpecifier != null)
            {
                Print(" from ");
                exportDeclaration.PathSpecifier.Accept(this);
            }

            PrintThenNewLine(";");
        }

        /// <summary>
        /// Visits namespace import.
        /// </summary>
        public override void Visit(NamespaceImport namespaceImport)
        {
            Print("*");

            if (namespaceImport.Name.IsValid)
            {
                Print(" as ");
                Print(namespaceImport.Name);
            }
        }

        /// <inheritdoc />
        public override void Visit(NamespaceAsVarImport namespaceAsVarImport)
        {
            Print("*");
            Print(" as ");
            Print(namespaceAsVarImport.Name);
        }

        /// <inheritdoc />
        public override void Visit(ImportOrExportModuleSpecifier importOrExportModuleSpecifier)
        {
            if (importOrExportModuleSpecifier.PropertyName.IsValid)
            {
                Print(importOrExportModuleSpecifier.PropertyName);
                Print(" as ");
            }

            Print(importOrExportModuleSpecifier.Name);
        }

        /// <inheritdoc />
        public override void Visit(ImportOrExportVarSpecifier importOrExportVarSpecifier)
        {
            if (importOrExportVarSpecifier.PropertyName.IsValid)
            {
                Print(importOrExportVarSpecifier.PropertyName);
                Print(" as ");
            }

            Print(importOrExportVarSpecifier.Name);
        }

        /// <inheritdoc />
        public override void Visit(NamedImportsOrExports namedImportsOrExports)
        {
            Print("{");

            for (int i = 0; i < namedImportsOrExports.Elements.Count; ++i)
            {
                namedImportsOrExports.Elements[i].Accept(this);

                if (i < namedImportsOrExports.Elements.Count - 1)
                {
                    Print(", ");
                }
            }

            Print("}");
        }

        /// <inheritdoc />
        public override void Visit(ImportOrExportClause importOrExportClause)
        {
            importOrExportClause.NamedBinding?.Accept(this);
        }

        /// <inheritdoc />
        public override void Visit(InterfaceDeclaration interfaceDeclaration)
        {
            if (interfaceDeclaration.Decorators.Count > 0)
            {
                foreach (var decorator in interfaceDeclaration.Decorators)
                {
                    Print(DecoratorMarker);
                    decorator.Accept(this);
                    PrintNewLine();
                    Print(Indent);
                }
            }

            if ((interfaceDeclaration.Modifier & Declaration.DeclarationFlags.Export) != 0)
            {
                Print("export ");
            }

            if ((interfaceDeclaration.Modifier & Declaration.DeclarationFlags.Ambient) != 0)
            {
                Print("declare ");
            }

            Print("interface ");
            Print(interfaceDeclaration.Name);
            PrintOptionalGenericTypeList(interfaceDeclaration.TypeParameters);
            if (interfaceDeclaration.ExtendedTypes.Count > 0)
            {
                PrintList(" extends ", string.Empty, interfaceDeclaration.ExtendedTypes, type => type.Accept(this));
            }

            Print(" ");

            interfaceDeclaration.Body.Accept(this);

            PrintNewLine();
        }

        /// <inheritdoc />
        public override void Visit(EnumDeclaration enumDeclaration)
        {
            if (enumDeclaration.Decorators.Count > 0)
            {
                foreach (var decorator in enumDeclaration.Decorators)
                {
                    Print(DecoratorMarker);
                    decorator.Accept(this);
                    PrintNewLine();
                    Print(Indent);
                }
            }

            PrintIndented(enumDeclaration.GetModifierString());
            Print("enum ");
            Print(enumDeclaration.Name);
            Print(" ");

            Action<Node> job = node =>
                               {
                                   var enumerator = (EnumMemberDeclaration)node;
                                   if (enumerator.Decorators.Count > 0)
                                   {
                                       foreach (var decorator in enumerator.Decorators)
                                       {
                                           Print(DecoratorMarker);
                                           decorator.Accept(this);
                                           PrintNewLine();
                                           Print(Indent);
                                       }
                                   }

                                   Print(enumerator.Name);
                                   if (enumerator.Expression != null)
                                   {
                                       Print(" = ");
                                       enumerator.Expression.Accept(this);
                                   }
                               };

            PrintList("{", "}", enumDeclaration.EnumMemberDeclarations, job);

            PrintNewLine();
        }

        /// <inheritdoc />
        public override void Visit(ModuleDeclaration moduleDeclaration)
        {
            PrintIndented(moduleDeclaration.GetModifierString());
            Print("namespace ");
            Print(moduleDeclaration.Name);

            var currentModule = moduleDeclaration;

            while (currentModule.Declarations.Count == 1 && currentModule.Declarations[0] is ModuleDeclaration)
            {
                currentModule = (ModuleDeclaration)currentModule.Declarations[0];
                Print(".");
                Print(currentModule.Name);
            }

            Print(" ");

            PrintThenNewLine("{");
            m_currentIndent++;

            foreach (var decl in currentModule.Declarations)
            {
                decl.Accept(this);
            }

            m_currentIndent--;
            PrintIndented("}");

            PrintNewLine();
        }

        /// <inheritdoc />
        public override void Visit(TypeAliasDeclaration typeAliasDeclaration)
        {
            PrintIndented("type ");
            Print(typeAliasDeclaration.Name);
            if (typeAliasDeclaration.TypeParameters.Count > 0)
            {
                PrintList("<", ">", typeAliasDeclaration.TypeParameters, (p) => p.Accept(this));
            }

            Print(" = ");
            typeAliasDeclaration.Type.Accept(this);
            Print(";");

            PrintNewLine();
        }

        /// <inheritdoc />
        public override void Visit(InterpolatedPaths interpolatedPaths)
        {
            Print(interpolatedPaths.HeadIsRelativePath ? "r`" : "p`");
            for (int index = 0; index < interpolatedPaths.GetPaths().Count; index++)
            {
                var expression = interpolatedPaths.GetPaths()[index];
                if (expression is IConstantExpression constantExpression)
                {
                    Print(
                        constantExpression is IPathLikeLiteral pathLike
                            ? pathLike.ToDisplayString(m_frontEndContext.PathTable, m_currentFolder)
                            : constantExpression.Value.ToString());
                }
                else
                {
                    Print("${");
                    expression.Accept(this);
                    Print("}");
                }

                if (index != interpolatedPaths.GetPaths().Count - 1)
                {
                    Print("/");
                }
            }

            Print("`");
        }

        #endregion Declaration visits

        #region Logging

        /// <summary>
        /// Gets a string representation of an AST node for logging or reporting.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static string GetLogStringRepr(
            FrontEndContext frontEndContext,
            StringWriter writer,
            AbsolutePath currentFolder,
            AbsolutePath currentRoot,
            Node node)
        {
            Contract.Requires(frontEndContext != null);
            Contract.Requires(writer != null);
            Contract.Requires(currentFolder.IsValid);
            Contract.Requires(node != null);

            var astPrinter = new PrettyPrinter(frontEndContext, writer, currentFolder, currentRoot, oneLiner: true);
            node.Accept(astPrinter);
            string result = writer.ToString();

            // Cut if it is too long.
            if (result.Length > 80)
            {
                result = result.Substring(0, 76) + " ...";
            }

            return result;
        }

        #endregion Logging
    }
}
