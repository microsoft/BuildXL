// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Utilities
{
    /// <summary>
    /// Helper class with compare functions
    /// </summary>
    /// <remarks>
    /// The typical use-case is sorting array literals to canonicalize build specs.
    /// </remarks>
    [SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1501:StatementMustNotBeOnSingleLine", Justification = "This file has a lot of small null checks that are hard to factor, so allowing non-standard curlies here.")]
    public static class ExpressionComparers
    {
        /// <summary>
        /// Return a value less than zero if <see ref="left"/> is less than <see ref="right"/>,
        /// zero if <see ref="left"/> is equal to <see ref="right"/>,
        /// or a value greater than zero if <see ref="left"/> is greater than <see ref="right"/>.
        /// This means that a value less than zero means <see ref="left"/> should be sorted before <see ref="right"/>, zero they are equal and greater than one means the <see ref="right"/> value should be sorted before <see ref="left"/>.
        /// </summary>
        public static int CompareExpression(IExpression left, IExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var leftHelper = TryGetCompareHelper(left.Kind);
            var rightHelper = TryGetCompareHelper(right.Kind);

            if (leftHelper == null)
            {
                if (rightHelper == null)
                {
                    return CompareNodeAsText(left, right);
                }
                else
                {
                    return 1; // Right wins
                }
            }
            else
            {
                if (rightHelper == null)
                {
                    return -1; // Left wins
                }

                // lower number wins.
                var delta = leftHelper.RelativeSortOrder - rightHelper.RelativeSortOrder;
                if (delta == 0)
                {
                    // Same high-level order, rely on the more detailed specific comparer.
                    return leftHelper.Comparison(left, right);
                }

                return delta;
            }
        }

        private static readonly CompareHelper[] s_compareHelpers = CreateCompareHelpers();

        /// <summary>
        /// Do a quick lookup for sorting information for the given SyntaxKind of the Node.
        /// </summary>
        private static CompareHelper TryGetCompareHelper(TypeScript.Net.Types.SyntaxKind syntaxKind)
        {
            return s_compareHelpers[(int)syntaxKind];
        }

        /// <summary>
        /// SortOrderIndex per syntaxKind.
        /// Kinds with 0 are considered unsorted and will be placed at the end.
        /// </summary>
        private static CompareHelper[] CreateCompareHelpers()
        {
            int numberOfSyntaxKinds = (int)TypeScript.Net.Types.SyntaxKind.Count;

            var helpers = new CompareHelper[numberOfSyntaxKinds];

            // Start SortOrder with 1, to catch accidental uninitialized values.
            ushort sortOrder = 1;

            CompareHelper.Register<IExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.NumericLiteral, (l, r) => CompareNumericLiteralExpression(l, r));
            CompareHelper.Register<IExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.PrefixUnaryExpression, (l, r) => CompareNumericLiteralExpression(l, r));
            sortOrder++;
            CompareHelper.Register<ILiteralExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.StringLiteral, (l, r) => CompareStringLiteralExpression(l, r));
            CompareHelper.Register<ILiteralExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.FirstTemplateToken, (l, r) => CompareStringLiteralExpression(l, r));
            sortOrder++;
            CompareHelper.Register<ITemplateExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.TemplateExpression, (l, r) => CompareTemplateExpression(l, r));
            sortOrder++;
            CompareHelper.Register<ITaggedTemplateExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.TaggedTemplateExpression, (l, r) => CompareTaggedTemplateExpression(l, r));
            sortOrder++;
            CompareHelper.Register<IExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.Identifier, (l, r) => CompareReferences(l, r));
            CompareHelper.Register<IExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression, (l, r) => CompareReferences(l, r));
            sortOrder++;
            CompareHelper.Register<IElementAccessExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.ElementAccessExpression, (l, r) => CompareElementAccessExpression(l, r));
            sortOrder++;
            CompareHelper.Register<IExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.VoidExpression, (l, r) => CompareNodeAsText(l, r));
            sortOrder++;
            CompareHelper.Register<ICallExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.CallExpression, (l, r) => CompareCallExpression(l, r));
            sortOrder++;
            CompareHelper.Register<IParenthesizedExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.CallExpression, (l, r) => CompareParenthesisedExpression(l, r));
            sortOrder++;
            CompareHelper.Register<IConditionalExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.ConditionalExpression, (l, r) => CompareConditionalExpression(l, r));
            sortOrder++;
            CompareHelper.Register<ISwitchExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.SwitchExpression, (l, r) => CompareSwitchExpression(l, r));
            sortOrder++;
            CompareHelper.Register<ISpreadElementExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.SpreadElementExpression, (l, r) => CompareSpreadElementExpression(l, r));
            sortOrder++;
            CompareHelper.Register<IArrayLiteralExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.ArrayLiteralExpression, (l, r) => CompareArrayLiteralExpression(l, r));
            sortOrder++;
            CompareHelper.Register<IObjectLiteralExpression>(helpers, sortOrder, TypeScript.Net.Types.SyntaxKind.ObjectLiteralExpression, (l, r) => CompareObjectLiteralExpression(l, r));

            return helpers;
        }

        private static int CompareNumericLiteralExpression(IExpression left, IExpression right)
        {
            // No need for Standard left,ComparePathStepsright and null checks since TryExtractInt handles null
            int? leftInt = TryExtractInt(left);
            int? rightInt = TryExtractInt(right);

            if (!leftInt.HasValue)
            {
                if (!rightInt.HasValue)
                {
                    // Non-standard unary expressions sorted lexically
                    return CompareNodeAsText(left, right);
                }

                return -1; // left wins, place non-standard unary expressions at the top.
            }

            if (!rightInt.HasValue)
            {
                return 1; // right wins, place non-standard unary expression at the top.s
            }

            return leftInt.Value.CompareTo(rightInt.Value);
        }

        private static int? TryExtractInt(IExpression expression)
        {
            if (expression == null)
            {
                return null;
            }

            switch (expression.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.NumericLiteral:
                    int parsed;
                    if (int.TryParse(expression.As<ILiteralExpression>().Text, out parsed))
                    {
                        return parsed;
                    }

                    return null;
                case TypeScript.Net.Types.SyntaxKind.PrefixUnaryExpression:
                    var prefixExpression = expression.As<IPrefixUnaryExpression>();
                    var result = TryExtractInt(prefixExpression.Operand);
                    if (!result.HasValue)
                    {
                        return null;
                    }

                    switch (prefixExpression.Operator)
                    {
                        case TypeScript.Net.Types.SyntaxKind.MinusToken:
                            return 0 - result;
                        case TypeScript.Net.Types.SyntaxKind.PlusToken:
                            return result;
                        case TypeScript.Net.Types.SyntaxKind.TildeToken:
                        case TypeScript.Net.Types.SyntaxKind.ExclamationToken:
                        case TypeScript.Net.Types.SyntaxKind.MinusMinusToken:
                        case TypeScript.Net.Types.SyntaxKind.PlusPlusToken:
                        default:
                            return null;
                    }

                default:
                    return null;
            }
        }

        private static int CompareStringLiteralExpression(ILiteralExpression left, ILiteralExpression right, bool usePathCompareSemantics = false)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            // First try to sort on the character wrapper
            var leftKind = (int?)(left as IStringLiteral)?.LiteralKind;
            var rightKind = (int?)(right as IStringLiteral)?.LiteralKind;
            if (leftKind != rightKind)
            {
                return leftKind < rightKind ? -1 : 1;
            }

            if (usePathCompareSemantics)
            {
                return ComparePathStringLiteral(left.Text, right.Text);
            }

            return string.CompareOrdinal(left.Text, right.Text);
        }

        private static int ComparePathStringLiteral(string left, string right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var leftSteps = new List<Tuple<PathStepKind, string>>();
            var rightSteps = new List<Tuple<PathStepKind, string>>();
            CollectSteps(left, leftSteps);
            CollectSteps(right, rightSteps);

            return ComparePathSteps(leftSteps, rightSteps);
        }

        private static int ComparePathTemplateExpression(ITemplateExpression left, ITemplateExpression right)
        {
            Contract.Requires(left != null);
            Contract.Requires(right != null);

            var leftSteps = new List<Tuple<PathStepKind, string>>();
            var rightSteps = new List<Tuple<PathStepKind, string>>();
            CollectSteps(left, leftSteps);
            CollectSteps(right, rightSteps);

            return ComparePathSteps(leftSteps, rightSteps);
        }

        private static int ComparePathSteps(List<Tuple<PathStepKind, string>> left, List<Tuple<PathStepKind, string>> right)
        {
            Contract.Requires(left != null);
            Contract.Requires(right != null);

            // Special case all files before folders if it is a literal, so:
            if (left.Count == 1 && left[0].Item1 == PathStepKind.Text && right.Count > 1)
            {
                return -1; // Left wins
            }

            if (left.Count > 1 && right.Count == 1 && right[0].Item1 == PathStepKind.Text)
            {
                return 1; // Right wins
            }

            return CompareList(left, right,
                (l, r) =>
                {
                    if (l.Item1 != r.Item1)
                    {
                        return l.Item1 == PathStepKind.Text ? -1 : 1;
                    }
                    var lString = l.Item2;
                    var rString = r.Item2;

                    if (string.IsNullOrEmpty(lString))
                    {
                        if (string.IsNullOrEmpty(rString))
                        {
                            return 0;
                        }
                        return 1; // empty string means absolute path, those are sorted after relative paths.
                    }

                    if (string.IsNullOrEmpty(rString))
                    {
                        return -1; // empty string means absolute path, those are sorted after relative paths.
                    }

                    return string.CompareOrdinal(lString, rString);
                },
                lastSingleEntryWinsOverLexical: true);
        }

        private static void CollectSteps(ITemplateExpression template, List<Tuple<PathStepKind, string>> steps)
        {
            if (template.Head != null)
            {
                CollectSteps(template.Head.Text, steps);
            }

            foreach (var span in template.TemplateSpans.AsStructEnumerable())
            {
                if (span.Expression != null)
                {
                    steps.Add(Tuple.Create(PathStepKind.Expression, span.Expression.ToDisplayString()));
                }

                if (span.Literal != null)
                {
                    CollectSteps(span.Literal.Text, steps);
                }
            }
        }

        private enum PathStepKind : byte
        {
            Text,
            Expression,
        }

        private static void CollectSteps(string pathFragment, List<Tuple<PathStepKind, string>> steps)
        {
            foreach (var fragment in pathFragment.Split('\\', '/'))
            {
                steps.Add(Tuple.Create(PathStepKind.Text, fragment));
            }
        }

        private static int CompareTemplateExpression(ITemplateExpression left, ITemplateExpression right, bool usePathCompareSemantics = false)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            if (usePathCompareSemantics)
            {
                return ComparePathTemplateExpression(left, right);
            }

            int result = 0;
            if (left.Head != null)
            {
                if (right.Head != null)
                {
                    result = CompareStringLiteralExpression(left.Head.Cast<ILiteralExpression>(), right.Head.Cast<ILiteralExpression>(), usePathCompareSemantics);
                }
                else
                {
                    return -1; // Literals win over expressions;
                }
            }
            else if (right.Head == null)
            {
                return 1; // Literals win over expressions
            }

            if (result != 0)
            {
                return result;
            }

            return CompareNodeArrays(left.TemplateSpans, right.TemplateSpans, (l, r) => CompareTemplateSpan(l, r, usePathCompareSemantics));
        }

        private static int CompareTaggedTemplateExpression(ITaggedTemplateExpression left, ITaggedTemplateExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            // Compare tag
            var result = CompareNodeAsText(left.Tag, right.Tag);
            if (result != 0)
            {
                return result;
            }

            // Special case check for path literal.
            var interpolationKind = left.GetInterpolationKind();
            bool usePathCompareSemantics = false;
            switch (interpolationKind)
            {
                case InterpolationKind.PathInterpolation:
                case InterpolationKind.FileInterpolation:
                case InterpolationKind.DirectoryInterpolation:
                case InterpolationKind.RelativePathInterpolation:
                    usePathCompareSemantics = true;
                    break;
            }

            var leftIsLiteral = IsTemplateLiteral(left.TemplateExpression);
            var rightIsLiteral = IsTemplateLiteral(right.TemplateExpression);

            ITemplateExpression leftTemplate;
            ITemplateExpression rightTemplate;

            if (leftIsLiteral)
            {
                if (rightIsLiteral)
                {
                    return CompareStringLiteralExpression(left.TemplateExpression.Cast<ILiteralExpression>(), right.TemplateExpression.Cast<ILiteralExpression>(), usePathCompareSemantics);
                }

                leftTemplate = new TemplateExpression() { Head = new TemplateLiteralFragment() { Text = left.TemplateExpression.GetTemplateText() }, TemplateSpans = NodeArray<ITemplateSpan>.Empty };
                rightTemplate = right.TemplateExpression.Cast<ITemplateExpression>();
            }
            else if (rightIsLiteral)
            {
                leftTemplate = left.TemplateExpression.Cast<ITemplateExpression>();
                rightTemplate = new TemplateExpression() { Head = new TemplateLiteralFragment() { Text = right.TemplateExpression.GetTemplateText() }, TemplateSpans = NodeArray<ITemplateSpan>.Empty };
            }
            else
            {
                leftTemplate = left.TemplateExpression.Cast<ITemplateExpression>();
                rightTemplate = right.TemplateExpression.Cast<ITemplateExpression>();
            }

            return CompareTemplateExpression(leftTemplate, rightTemplate, usePathCompareSemantics);
        }

        private static bool IsTemplateLiteral(INode node)
        {
            var kind = node.Kind;
            return kind == TypeScript.Net.Types.SyntaxKind.StringLiteral || kind == TypeScript.Net.Types.SyntaxKind.FirstTemplateToken || kind == TypeScript.Net.Types.SyntaxKind.LastTemplateToken;
        }

        private static int CompareNodeArrays<T>(INodeArray<T> left, INodeArray<T> right, Comparison<T> comparer)
        {
            var leftCount = left.Count;
            var rightCount = right.Count;
            for (int i = 0; i < leftCount; i++)
            {
                if (i >= rightCount)
                {
                    return 1; // right is smaller
                }

                var result = comparer(left[i], right[i]);
                if (result != 0)
                {
                    return result;
                }
            }

            if (rightCount > leftCount)
            {
                return -1; // left is smaller;
            }

            return 0;
        }

        private static int CompareList<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Comparison<T> comparer, bool lastSingleEntryWinsOverLexical)
        {
            var leftCount = left.Count;
            var rightCount = right.Count;
            for (int i = 0; i < leftCount; i++)
            {
                // If we want to have A.lst sort before A.B.lst we check that we are the one before last and have more on the right. So left wins.
                if (lastSingleEntryWinsOverLexical)
                {
                    if (i == leftCount - 1 && rightCount > leftCount)
                    {
                        return -1;
                    }

                    if (i == rightCount - 1 && leftCount > rightCount)
                    {
                        return 1;
                    }
                }

                if (i >= rightCount)
                {
                    return 1; // right is smaller
                }

                var result = comparer(left[i], right[i]);
                if (result != 0)
                {
                    return result;
                }
            }

            if (rightCount > leftCount)
            {
                return -1; // left is smaller;
            }

            return 0;
        }

        private static int CompareTemplateSpan(ITemplateSpan left, ITemplateSpan right, bool usePathCompareSemantics = false)
        {
            var result = CompareNodeAsText(left.Expression, right.Expression);
            if (result != 0)
            {
                return result;
            }

            return CompareStringLiteralExpression(left.Literal.Cast<ILiteralExpression>(), right.Literal.Cast<ILiteralExpression>(), usePathCompareSemantics);
        }

        private static int CompareNodeAsText(INode left, INode right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var leftStr = left.ToDisplayString();
            var rightStr = right.ToDisplayString();
            return string.CompareOrdinal(leftStr, rightStr);
        }

        private static int CompareReferences(IExpression left, IExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var leftNames = new List<string>();
            var rightNames = new List<string>();

            var leftFullName = TryFlattenIdentifiers(left, leftNames);
            var rightFullName = TryFlattenIdentifiers(right, rightNames);

            var result = CompareList(leftNames, rightNames, string.CompareOrdinal, lastSingleEntryWinsOverLexical: true);

            if (result != 0)
            {
                return result;
            }

            if (leftFullName == rightFullName)
            {
                return 0;
            }

            return leftFullName ? 1 : -1;
        }

        private static bool TryFlattenIdentifiers(IExpression expression, List<string> names)
        {
            switch (expression.Kind)
            {
                case TypeScript.Net.Types.SyntaxKind.Identifier:
                    names.Add(expression.Cast<IIdentifier>().Text);
                    return true;
                case TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression:
                    var propertyAccess = expression.Cast<IPropertyAccessExpression>();
                    if (!TryFlattenIdentifiers(propertyAccess.Expression, names))
                    {
                        return false;
                    }

                    names.Add(propertyAccess.Name.Text);
                    return true;
                default:
                    return false;
            }
        }

        private static int CompareElementAccessExpression(IElementAccessExpression left, IElementAccessExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var result = CompareExpression(left.Expression, right.Expression);
            if (result != 0)
            {
                return result;
            }

            return CompareExpression(left.ArgumentExpression, right.ArgumentExpression);
        }

        private static int CompareCallExpression(ICallExpression left, ICallExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var result = CompareExpression(left.Expression, right.Expression);
            if (result != 0)
            {
                return result;
            }

            result = CompareNodeArrays(left.TypeArguments, right.TypeArguments, (l, r) => CompareNodeAsText(l, r));
            if (result != 0)
            {
                return result;
            }

            return CompareNodeArrays(left.Arguments, right.Arguments, (l, r) => CompareExpression(l, r));
        }

        private static int CompareParenthesisedExpression(IParenthesizedExpression left, IParenthesizedExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            return CompareExpression(left.Expression, right.Expression);
        }

        private static int CompareConditionalExpression(IConditionalExpression left, IConditionalExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var result = CompareExpression(left.Condition, right.Condition);
            if (result != 0)
            {
                return result;
            }

            result = CompareExpression(left.WhenTrue, right.WhenTrue);
            if (result != 0)
            {
                return result;
            }

            return CompareExpression(left.WhenFalse, right.WhenFalse);
        }

        private static int CompareSwitchExpression(ISwitchExpression left, ISwitchExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            var result = CompareExpression(left.Expression, right.Expression);
            if (result != 0)
            {
                return result;
            }

            return CompareNodeArrays(left.Clauses, right.Clauses, (l, r) => CompareSwitchExpressionClause(l, r));
        }

        private static int CompareSwitchExpressionClause(ISwitchExpressionClause left, ISwitchExpressionClause right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            if (left.IsDefaultFallthrough != right.IsDefaultFallthrough)
            {
                return left.IsDefaultFallthrough ? 1 : -1;
            }

            var result = CompareExpression(left.Match, right.Match);
            if (result != 0)
            {
                return result;
            }

            return CompareExpression(left.Expression, right.Expression);
        }

        private static int CompareSpreadElementExpression(ISpreadElementExpression left, ISpreadElementExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            return CompareExpression(left.Expression, right.Expression);
        }

        private static int CompareObjectLiteralExpression(IObjectLiteralExpression left, IObjectLiteralExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            return CompareNodeArrays(left.Properties, right.Properties, (l, r) => CompareNodeAsText(l, r));
        }

        private static int CompareArrayLiteralExpression(IArrayLiteralExpression left, IArrayLiteralExpression right)
        {
            // Standard left,right and null checks
            if (left == null && right == null) { return 0; }
            if (left == null) { return 1; }
            if (right == null) { return -1; }

            return CompareNodeArrays(left.Elements, right.Elements, (l, r) => CompareExpression(l, r));
        }

        private sealed class CompareHelper
        {
            /// <summary>
            /// The syntaxKind
            /// </summary>
            [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
            public TypeScript.Net.Types.SyntaxKind SyntaxKind { get; }

            /// <summary>
            /// Integer value representing relative sort order for expressions by kind.
            /// </summary>
            public ushort RelativeSortOrder { get; }

            /// <summary>
            /// The custom comparer for this SyntaxKind
            /// </summary>
            public Comparison<IExpression> Comparison { get; }

            /// <nodoc />
            private CompareHelper(TypeScript.Net.Types.SyntaxKind syntaxKind, ushort relativeSortOrder, Comparison<IExpression> comparison)
            {
                SyntaxKind = syntaxKind;
                RelativeSortOrder = relativeSortOrder;
                Comparison = comparison;
            }

            /// <nodoc />
            public static void Register<T>(CompareHelper[] helpers, ushort relativeSortOrder, TypeScript.Net.Types.SyntaxKind syntaxKind, Comparison<T> comparable)
                where T : class, IExpression
            {
                var helper = new CompareHelper(syntaxKind, relativeSortOrder,
                    (l, r) =>
                    {
                        var left = l.As<T>();
                        var right = r.As<T>();
                        if (left == null)
                        {
                            return right == null ? 0 : -1;
                        }

                        if (right == null)
                        {
                            return 1;
                        }

                        return comparable(left.As<T>(), right.As<T>());
                    });
                helpers[(int)syntaxKind] = helper;
            }
        }
    }
}
