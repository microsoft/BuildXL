// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Utilities
{
    /// <summary>
    /// Prints the nodes with some DScript specifics
    /// </summary>
    public class DScriptPrettyPrintVisitor : ReformatterVisitor
    {
        private const string AddIfFunctionName = "addIf";

        private bool IsInPathLiteralContext { get; set; }

        private bool IsInObjectLiteralContext { get; set; }

        /// <summary>
        /// Whether addIf calls should be printed using special formatting or not.
        /// </summary>
        /// <remarks>
        /// This will put the first argument on the same line as the parenthesis instead of the first argument on a new line.
        /// This will put a newline before ...addIf( if it is a member of an array
        /// </remarks>
        public bool SpecialAddIfFormatting { get; set; }

        /// <nodoc />
        public DScriptPrettyPrintVisitor(ScriptWriter writer, bool attemptToPreserveNewlinesForListMembers = true)
            : base(writer, onlyPrintHeader: false, attemptToPreserveNewlinesForListMembers: attemptToPreserveNewlinesForListMembers)
        {
        }

        #region Special handling for addIf function calls

        /// <summary>
        /// DScript has special non-standard formatting for addIf
        /// </summary>
        public override void VisitCallExpression(CallExpression node)
        {
            if (SpecialAddIfFormatting)
            {
                var expression = node.Expression;
                if (expression != null && expression.Kind == TypeScript.Net.Types.SyntaxKind.Identifier)
                {
                    var identifier = expression.Cast<IIdentifier>().Text;
                    if (string.Equals(AddIfFunctionName, identifier))
                    {
                        var arguments = node.Arguments;
                        var argumentsCount = arguments.Count;

                        Writer.AppendToken(AddIfFunctionName).AppendToken(ScriptWriter.StartArgumentsToken);

                        if (argumentsCount == 2 && arguments[1].Kind == TypeScript.Net.Types.SyntaxKind.ArrowFunction)
                        {
                            /*
                             * Print this case as:
                             * ...addIf(cond, () => [
                             *      item1,
                             * ])
                             */
                            AppendNode(arguments[0]);
                            Writer.AppendToken(",");
                            Writer.Whitespace();

                            var arrowFunction = arguments[1].Cast<IArrowFunction>();
                            VisitArrowFunction(
                                arrowFunction,
                                n =>
                                {
                                    if (n.Kind == TypeScript.Net.Types.SyntaxKind.ArrayLiteralExpression)
                                    {
                                        // Force array literal always to print items on newlines
                                        VisitArrayLiteralExpression(n.Cast<ArrayLiteralExpression>(), 0);
                                    }
                                    else
                                    {
                                        AppendNode(n);
                                    }
                                });
                            Writer.NoNewLine();
                            Writer.AppendToken(ScriptWriter.EndArgumentsToken);
                        }
                        else
                        {
                            /*
                             * Print this case as:
                             * ...addIf(cond,
                             *      item1,
                             * )
                             */
                            using (Writer.Indent())
                            {
                                for (int i = 0; i < argumentsCount; i++)
                                {
                                    AppendNode(arguments[i]);
                                    if (i < argumentsCount - 1)
                                    {
                                        Writer.AppendToken(",")
                                            .NewLine();
                                    }
                                }

                                Writer.AppendToken(ScriptWriter.EndArgumentsToken);
                            }
                        }

                        return;
                    }
                }
            }

            base.VisitCallExpression(node);
        }
        #endregion

        #region Special object literal handling

        /// <summary>
        /// DScript has slightly different formatting in objects for readability of large lists
        /// </summary>
        public override void VisitObjectLiteralExpression(ObjectLiteralExpression node)
        {
            var previousContext = IsInObjectLiteralContext;
            IsInObjectLiteralContext = true;

            base.VisitObjectLiteralExpression(node);

            IsInObjectLiteralContext = previousContext;
        }

        /// <summary>
        /// DScript prints a whitelist before each list with five or more elements
        /// </summary>
        public override void VisitPropertyAssignment(PropertyAssignment node)
        {
            if (IsInObjectLiteralContext)
            {
                var initializer = node.Initializer;
                if (initializer != null && initializer.Kind == TypeScript.Net.Types.SyntaxKind.ArrayLiteralExpression)
                {
                    var arrayLiteral = initializer.Cast<IArrayLiteralExpression>();
                    if (arrayLiteral.Elements.Count >= 5)
                    {
                        // unless it is the first
                        var parentArray = node.Parent.TryCast<IObjectLiteralExpression>();
                        if (parentArray != null && parentArray.Properties[0] != node)
                        {
                            // Add a newline before each array literals with 5 or more elements.
                            Writer.AdditionalNewLine();
                        }
                    }
                }
            }

            base.VisitPropertyAssignment(node);
        }

        #endregion Special object literal handling

        /// <summary>
        /// Array literals inside object literals should mostly be spread on multiple lines, single items can still be on one line.
        /// </summary>
        public override void VisitArrayLiteralExpression(ArrayLiteralExpression node)
        {
            var actualCount = IsInObjectLiteralContext ? 1 : 3;

            AppendList(
                node.Elements,
                separatorToken: ScriptWriter.SeparateArrayToken,
                startBlockToken: ScriptWriter.StartArrayToken,
                endBlockToken: ScriptWriter.EndArrayToken,
                placeSeparatorOnLastElement: true,
                minimumCountBeforeNewLines: actualCount,
                printTrailingComments: true,
                visitItem: n => HandleArrayLiteralElement(n));
        }

        private void HandleArrayLiteralElement(INode node)
        {
            if (SpecialAddIfFormatting)
            {
                if (node.Kind == TypeScript.Net.Types.SyntaxKind.SpreadElementExpression)
                {
                    var spread = node.Cast<ISpreadElementExpression>();
                    if (spread.Expression.Kind == TypeScript.Net.Types.SyntaxKind.CallExpression)
                    {
                        var call = spread.Expression.Cast<ICallExpression>();
                        if (call.Expression.Kind == TypeScript.Net.Types.SyntaxKind.Identifier)
                        {
                            var identifier = call.Expression.Cast<IIdentifier>().Text;
                            if (string.Equals(AddIfFunctionName, identifier))
                            {
                                Writer.ExplicitlyAddNewLine();
                            }
                        }
                    }
                }
            }

            AppendNode(node, skipTrailingComments: true);
        }

        #region NonStandard path Handling

        /// <summary>
        /// Track whether we are in a path literal context
        /// </summary>
        public override void VisitTaggedTemplateExpression(TaggedTemplateExpression node)
        {
            AppendNode(node.Tag);

            var previousIsInSpecialPathContext = IsInPathLiteralContext;
            IsInPathLiteralContext |= node.IsPathInterpolation();

            AppendNode(node.TemplateExpression);

            IsInPathLiteralContext = previousIsInSpecialPathContext;
        }

        /// <summary>
        /// Ensure the expressions in a template expression is not using special path encoding
        /// </summary>
        public override void VisitTemplateExpression(TemplateExpression node)
        {
            Writer.AppendToken("`");
            AppendNode(node.Head);

            if (!node.TemplateSpans.IsNullOrEmpty())
            {
                foreach (var span in node.TemplateSpans.AsStructEnumerable())
                {
                    var previousIsInSpecialPathContext = IsInPathLiteralContext;

                    Writer.AppendToken("${");

                    // When going into expression mode disable the specialPathContext
                    IsInPathLiteralContext = false;
                    AppendNode(span.Expression);
                    IsInPathLiteralContext = previousIsInSpecialPathContext;
                    Writer.AppendToken("}");

                    AppendNode(span.Literal);
                }
            }

            Writer.AppendToken("`");
        }

        /// <summary>
        /// Path literals are printed specially when in a path context.
        /// </summary>
        public override void VisitLiteralExpression(LiteralExpression node)
        {
            char separator;
            if (LiteralExpression.LiteralExpressionToCharMap.TryGetValue(node.LiteralKind, out separator))
            {
                Writer.AppendQuotedString(node.Text, IsInPathLiteralContext, separator);
            }
            else
            {
                Writer.AppendToken(node.Text);
            }
        }

        #endregion NonStandard path Handling

        #region special Qualifier declaration printing

        /// <inheritdoc />
        public override void VisitVariableDeclaration(VariableDeclaration node)
        {
            if (node.Type != null && string.Equals(Names.CurrentQualifier, node.Name.GetText()) && node.Type.Kind == TypeScript.Net.Types.SyntaxKind.TypeLiteral)
            {
                AppendFlags(node.Flags);
                AppendNode(node.Name);
                Writer.AppendToken(":").Whitespace();
                VisitTypeLiteralNode(node.Type.Cast<ITypeLiteralNode>(), 0);
                AppendInitializerIfNeeded(node.Initializer);
            }
            else
            {
                base.VisitVariableDeclaration(node);
            }
        }
        #endregion
    }
}
