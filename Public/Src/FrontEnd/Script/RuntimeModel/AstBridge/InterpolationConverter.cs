// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Expressions.CompositeExpressions;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using Expression = BuildXL.FrontEnd.Script.Expressions.Expression;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Delegate that is used by <see cref="InterpolationConverter"/> to convert expressions.
    /// </summary>
    /// <remarks>
    /// This delegate untangles <see cref="InterpolationConverter"/> from <see cref="AstConverter"/>.
    /// </remarks>
    internal delegate Expression ExpressionConverter(IExpression expression, FunctionScope escapedVars);

    /// <summary>
    /// Helper class that is responsible for converting different kind of interpolated strings into
    /// evaluation AST.
    /// </summary>
    internal sealed class InterpolationConverter
    {
        private readonly AstConverter m_converter;
        private readonly AstConversionContext m_context;
        private readonly LiteralConverter m_literalConverter;

        private RuntimeModelContext RuntimeModelContext => m_context.RuntimeModelContext;

        private ISourceFile SourceFile => m_context.CurrentSourceFile;

        private readonly SelectorExpression m_stringInterpolationSelectorExpression;
        private readonly SelectorExpression m_pathAtomInterpolateSelectorExpression;

        public InterpolationConverter(AstConverter astConverter, AstConversionContext conversionContext)
        {
            Contract.Requires(astConverter != null);
            Contract.Requires(conversionContext != null);

            m_converter = astConverter;
            m_context = conversionContext;

            m_literalConverter = new LiteralConverter(conversionContext);
            m_pathAtomInterpolateSelectorExpression = CreatePathAtomInterpolateSelectorExpression();
            m_stringInterpolationSelectorExpression = CreateStringInterpolateSelectorExpression();
        }

        /// <summary>
        /// Method that converts tagged ast node into <see cref="Expression"/>.
        /// </summary>
        public Expression ConvertInterpolation(ITaggedTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            (var interpolationKind, ILiteralExpression literal, ITemplateLiteralFragment head, INodeArray<ITemplateSpan> templateSpans) = source;
            var tagTemplate = new ProcessedTagTemplateExpression(source, interpolationKind, literal, head, templateSpans);

            switch (interpolationKind)
            {
                case InterpolationKind.PathInterpolation:
                    return ConvertPathInterpolation(ref tagTemplate, escapes, currentQualifierSpaceId);
                case InterpolationKind.FileInterpolation:
                    return ConvertFileInterpolation(ref tagTemplate, escapes, currentQualifierSpaceId);
                case InterpolationKind.DirectoryInterpolation:
                    return ConvertDirectoryInterpolation(ref tagTemplate, escapes, currentQualifierSpaceId);
                case InterpolationKind.PathAtomInterpolation:
                    return ConvertPathAtomInterpolation(ref tagTemplate, escapes, currentQualifierSpaceId);
                case InterpolationKind.RelativePathInterpolation:
                    return ConvertRelativePathInterpolation(ref tagTemplate, escapes, currentQualifierSpaceId);
                default:
                    throw Contract.AssertFailure(I($"Unknown interpolation kind '{interpolationKind}'."));
            }
        }

        /// <summary>
        /// Converts string interpolation expression like <code>let x = `${foo}`;</code>
        /// </summary>
        public Expression ConvertStringInterpolation(ITemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            var taggedExpressions = EnumerateTaggedExpressionsForStringInterpolation(source, escapes, currentQualifierSpaceId);

            // There is one corner cases here:
            // If tagged expression is just a string literal, but has no expressions, we can just return it
            if (taggedExpressions.Count == 1)
            {
                if (taggedExpressions[0] is StringLiteral stringLiteral)
                {
                    return stringLiteral;
                }
            }

            var applyExpression = ApplyExpression.Create(m_stringInterpolationSelectorExpression, taggedExpressions.ToArray(), LineInfo(source));
            return new StringLiteralExpression(applyExpression, applyExpression.Location);
        }

        private List<Expression> EnumerateTaggedExpressionsForStringInterpolation(ITemplateExpression template, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            // Creating a list that will hold all potential expressions
            List<Expression> result = new List<Expression>((template.TemplateSpans.Length * 2) + 1);

            var head = template.Head.Text;

            if (!string.IsNullOrEmpty(head))
            {
                result.Add(LiteralConverter.ConvertStringLiteral(head, Location(template.Head)));
            }

            for (int i = 0; i < template.TemplateSpans.Length; i++)
            {
                var span = template.TemplateSpans[i];
                if (span.Expression != null)
                {
                    var convertedExpression = m_converter.ConvertExpression(span.Expression, escapes, currentQualifierSpaceId);
                    if (convertedExpression != null)
                    {
                        result.Add(convertedExpression);
                    }
                }

                var fragment = span.Literal.Text;

                if (!string.IsNullOrEmpty(fragment))
                {
                    result.Add(LiteralConverter.ConvertStringLiteral(fragment, Location(template.Head)));
                }
            }

            return result;
        }

        private List<Expression> EnumerateTemplateSpans(
            ITemplateLiteralFragment headNode,
            INodeArray<ITemplateSpan> templateSpans,
            FunctionScope escapes,
            QualifierSpaceId currentQualifierSpaceId,
            bool isRelativePath)
        {
            Contract.Requires(headNode != null);
            Contract.Requires(templateSpans != null);

            // Creating a list that will hold all potential expressions
            List<Expression> result = new List<Expression>((templateSpans.Length * 2) + 1);

            // Example: 'path/to/{x}/abc/{y}'.
            // - Head is 'path/to/'
            // - Spans:
            //   1. '{x}/abc/': expr 'x', literal '/abc/'
            //   2. '{y}': expr 'y', no literal.

            // For instance in this case: p`foo/${x}/${y}` the result equals p`foo`.combine(x).combine(y);
            // and for this case: p`${x}` the result equals x.
            // Note that  p`path/to/abc` equals as p`./path/to/abc`. Thus for p`${x}/path/to/abc`, x should evaluate to an absolute path,
            // and such a construct equals x.combine("path").combine("to"). combine("abc").
            string head = headNode.Text;

            if (!string.IsNullOrEmpty(head))
            {
                // Example: 'path/to/'
                if (!HasTailingPathSeparator(head))
                {
                    string message = I($"Path fragment '{head}' does not have a tailing path separator.");
                    RuntimeModelContext.Logger.ReportInvalidPathInterpolationExpression(
                        RuntimeModelContext.LoggingContext,
                        Location(headNode).AsLoggingLocation(),
                        message);
                    return null;
                }

                if (!isRelativePath)
                {
                    // Tagged expression is expected to be an absolute path.
                    var convertedPathLiteral =
                        m_literalConverter.ConvertPathLiteral(
                            RemoveLeadingAndTailingPathSeparatorIfNeeded(head, isAbsolutePath: true),
                            Location(headNode));

                    if (convertedPathLiteral == null)
                    {
                        // Error has been reported.
                        return null;
                    }

                    result.Add(convertedPathLiteral);
                }
                else
                {
                    // Tagged expression is expected to be a relative path.
                    var convertedRelativePathLiteral = m_literalConverter.ConvertRelativePathLiteral(
                        RemoveLeadingAndTailingPathSeparatorIfNeeded(head, isAbsolutePath: false),
                        Location(headNode));

                    if (convertedRelativePathLiteral == null)
                    {
                        // Error has been reported.
                        return null;
                    }

                    result.Add(convertedRelativePathLiteral);
                }
            }

            for (int i = 0; i < templateSpans.Length; i++)
            {
                //// TODO: Currently fragment is string literal. This somehow defeats the purpose of paths.
                //// TODO: We need to find a syntactic representation of relative path that differs from string.
                var span = templateSpans[i];

                // Example: span is '{x}/abc/'.
                if (span.Expression != null)
                {
                    // Grab 'x' from '{x}/abc/'.
                    var convertedExpression = m_converter.ConvertExpression(span.Expression, escapes, currentQualifierSpaceId);
                    if (convertedExpression != null)
                    {
                        result.Add(convertedExpression);
                    }
                }

                // Fragment is '/abc/'.
                var fragment = span.Literal.Text;

                // For every expression (except last one), interpolated path should have a separator
                if (string.IsNullOrEmpty(fragment) && span.Expression != null)
                {
                    // Fragment is empty or consists only of whitespaces, but expression is present, e.g., span (2) -- '{y}'.
                    if (i == templateSpans.Length - 1)
                    {
                        // Last template span, nothing to do, separator could be empty.
                        continue;
                    }

                    // Not the last template span, thus needs a path separator.
                    string message = "Each path fragment in interpolated path literal should have a path separator between expressions.";
                    RuntimeModelContext.Logger.ReportInvalidPathInterpolationExpression(
                        RuntimeModelContext.LoggingContext,
                        Location(span.Literal).AsLoggingLocation(),
                        message);
                    return null;
                }

                // Skip if fragment is only a separator, e.g., '{w}/{z}'.
                if (IsPathSeparator(fragment))
                {
                    continue;
                }

                // Fragments should start with path separator, e.g., '/abc/'.
                if (!HasLeadingPathSeparator(fragment))
                {
                    string message = I($"Path fragment '{fragment}' does not have a leading path separator.");
                    RuntimeModelContext.Logger.ReportInvalidPathInterpolationExpression(
                        RuntimeModelContext.LoggingContext,
                        Location(span.Literal).AsLoggingLocation(),
                        message);
                    return null;
                }

                // All fragments except last one must have a trailing separator, e.g., '/abc/'.
                if (i != templateSpans.Length - 1 && !HasTailingPathSeparator(fragment))
                {
                    string message = I($"Path fragment '{fragment}' does not have a trailing path separator.");
                    RuntimeModelContext.Logger.ReportInvalidPathInterpolationExpression(
                        RuntimeModelContext.LoggingContext,
                        Location(span.Literal).AsLoggingLocation(),
                        message);
                    return null;
                }

                // Remove '/' from '/abc/'.
                var textFragment = RemoveLeadingAndTailingPathSeparatorIfNeeded(fragment, isAbsolutePath: false);
                string literal = textFragment.Length == fragment.Length ? fragment : textFragment.ToString();
                result.Add(new StringLiteral(literal, LineInfo(span.Literal)));
            }

            return result;
        }

        private List<Expression> EnumerateTaggedExpressionsForPathAtomInterpolation(
            ITemplateLiteralFragment headNode,
            INodeArray<ITemplateSpan> templateSpans,
            FunctionScope escapes,
            QualifierSpaceId currentQualifierSpaceId)
        {
            // Creating a list that will hold all potential expressions
            List<Expression> result = new List<Expression>((templateSpans.Length * 2) + 1);

            string head = headNode.Text;

            if (!string.IsNullOrEmpty(head))
            {
                var convertedPathAtomLiteral = m_literalConverter.ConvertPathAtomLiteral(head, Location(headNode));

                if (convertedPathAtomLiteral == null)
                {
                    // Error has been reported.
                    return null;
                }

                result.Add(convertedPathAtomLiteral);
            }

            foreach (var span in templateSpans.AsStructEnumerable())
            {
                if (span.Expression != null)
                {
                    var convertedExpression = m_converter.ConvertExpression(span.Expression, escapes, currentQualifierSpaceId);
                    if (convertedExpression != null)
                    {
                        result.Add(convertedExpression);
                    }
                }

                var fragment = span.Literal.Text;

                if (!string.IsNullOrEmpty(fragment))
                {
                    var convertedPathAtomLiteral = m_literalConverter.ConvertPathAtomLiteral(fragment, Location(span.Literal));

                    if (convertedPathAtomLiteral == null)
                    {
                        // Error has been reported.
                        return null;
                    }

                    result.Add(convertedPathAtomLiteral);
                }
            }

            return result;
        }

        private Expression ConvertPathInterpolation(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            if (source.Literal != null)
            {
                return m_literalConverter.ConvertPathLiteral(source.Literal, Location(source.TaggedTemplate));
            }

            return ConvertPathInterpolationExpression(ref source, escapes, currentQualifierSpaceId, isRelativePath: false);
        }

        private Expression ConvertRelativePathInterpolation(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            if (source.Literal != null)
            {
                return m_literalConverter.ConvertRelativePathLiteral(source.Literal, Location(source.TaggedTemplate));
            }

            return ConvertPathInterpolationExpression(ref source, escapes, currentQualifierSpaceId, isRelativePath: true);
        }

        private Expression ConvertPathAtomInterpolation(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            if (source.Literal != null)
            {
                return m_literalConverter.ConvertPathAtomLiteral(source.Literal, Location(source.TaggedTemplate));
            }

            return CurrentPathAtomInterpolationExpression(ref source, escapes, currentQualifierSpaceId);
        }

        private Expression ConvertPathInterpolationExpression(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId, bool isRelativePath)
        {
            var expressions = EnumerateTemplateSpans(source.Head, source.TemplateSpans, escapes, currentQualifierSpaceId, isRelativePath: isRelativePath);

            // Expressions could be empty only for the case like p``;
            if (expressions == null || expressions.Count == 0)
            {
                return null;
            }

            return new InterpolatedPaths(expressions, isRelativePath, expressions[0].Location);
        }

        private Expression CurrentPathAtomInterpolationExpression(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            var expressions = EnumerateTaggedExpressionsForPathAtomInterpolation(source.Head, source.TemplateSpans, escapes, currentQualifierSpaceId);

            // Expressions could be empty only for the case like a``;
            if (expressions == null || expressions.Count == 0)
            {
                return null;
            }

            return ApplyExpression.Create(m_pathAtomInterpolateSelectorExpression, expressions.ToArray(), expressions[0].Location);
        }

        private static StringSegment RemoveLeadingAndTailingPathSeparatorIfNeeded(string pathFragment, bool isAbsolutePath)
        {
            Contract.Requires(!string.IsNullOrEmpty(pathFragment));

            int startIndex = 0;
            // For Unix-based systems, if the fragment is an absolute path, then we don't remove the leading path
            // separator, since this means the file system root, not a relative path
            if (!(isAbsolutePath && OperatingSystemHelper.IsUnixOS) && HasLeadingPathSeparator(pathFragment))
            {
                startIndex = 1;
            }

            int length = pathFragment.Length - startIndex;

            if (HasTailingPathSeparator(pathFragment))
            {
                length--;
            }

            return new StringSegment(pathFragment, startIndex, length);
        }

        private static bool HasLeadingPathSeparator(string pathFragment)
        {
            Contract.Requires(!string.IsNullOrEmpty(pathFragment));

            return IsPathSeparator(pathFragment[0]);
        }

        private static bool HasTailingPathSeparator(string pathFragment)
        {
            Contract.Requires(!string.IsNullOrEmpty(pathFragment));

            return IsPathSeparator(pathFragment[pathFragment.Length - 1]);
        }

        internal static bool ContainsPathSeparator(string str)
        {
            return str.Any(c => IsPathSeparator(c));
        }

        private static bool IsPathSeparator(char c)
        {
            return c == '/' || c == '\\';
        }

        private static bool IsPathSeparator(string s)
        {
            return s.Length == 1 && IsPathSeparator(s[0]);
        }

        private Expression ConvertFileInterpolation(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            var pathExpression = ConvertPathInterpolation(ref source, escapes, currentQualifierSpaceId);

            if (pathExpression == null)
            {
                // Error occurred. Error was already logged
                return null;
            }

            if (pathExpression is PathLiteral pathLiteral)
            {
                return new FileLiteral(pathLiteral.Value, pathLiteral.Location);
            }

            return new FileLiteralExpression(pathExpression, pathExpression.Location);
        }

        private Expression ConvertDirectoryInterpolation(ref ProcessedTagTemplateExpression source, FunctionScope escapes, QualifierSpaceId currentQualifierSpaceId)
        {
            var pathExpression = ConvertPathInterpolation(ref source, escapes, currentQualifierSpaceId);
            if (pathExpression == null)
            {
                // Error occurred. Error was already logged
                return null;
            }

            return new DirectoryLiteralExpression(pathExpression, pathExpression.Location);
        }

        private UniversalLocation Location(INode node)
        {
            return node.Location(SourceFile, SourceFile.GetAbsolutePath(RuntimeModelContext.PathTable), RuntimeModelContext.PathTable);
        }

        private LineInfo LineInfo(INode node)
        {
            return Location(node).AsLineInfo();
        }

        private SelectorExpression CreatePathAtomInterpolateSelectorExpression()
        {
            return new SelectorExpression(
                new ModuleIdExpression(FullSymbol.Create(RuntimeModelContext.SymbolTable, Names.PathAtomNamespace), location: default(LineInfo)),
                SymbolAtom.Create(RuntimeModelContext.StringTable, Names.InterpolateString),
                location: default(LineInfo));
        }

        private SelectorExpression CreateStringInterpolateSelectorExpression()
        {
            return new SelectorExpression(
                new ModuleIdExpression(FullSymbol.Create(RuntimeModelContext.SymbolTable, Names.StringNamespace), location: default(LineInfo)),
                SymbolAtom.Create(RuntimeModelContext.StringTable, Names.InterpolateString),
                location: default(LineInfo));
        }

        /// <summary>
        /// Helper struct that carries all necessary information from pre-processed <see cref="ITaggedTemplateExpression"/>.
        /// </summary>
        private readonly struct ProcessedTagTemplateExpression
        {
            public ITaggedTemplateExpression TaggedTemplate { get; }

            public InterpolationKind Kind { get; }

            public ILiteralExpression Literal { get; }

            public ITemplateLiteralFragment Head { get; }

            public INodeArray<ITemplateSpan> TemplateSpans { get; }

            public ProcessedTagTemplateExpression(
                ITaggedTemplateExpression taggedTemplate,
                InterpolationKind kind,
                ILiteralExpression literal,
                ITemplateLiteralFragment head,
                INodeArray<ITemplateSpan> templateSpans)
            {
                Contract.Requires(taggedTemplate != null);
                Contract.Requires(
                    kind == InterpolationKind.Unknown || (literal != null || (head != null && templateSpans != null)),
                    "If interpolation is a well-known factory method, then Literal or Head+Templates should be valid.");

                TaggedTemplate = taggedTemplate;
                Kind = kind;
                Literal = literal;
                Head = head;
                TemplateSpans = templateSpans;
            }
        }
    }
}
