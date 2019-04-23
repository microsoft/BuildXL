// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Class that normalizes paths to use consistent path separators as well as casing. Can
    /// analyze or fix paths.
    /// </summary>
    public sealed class PathFixer
    {
        /// <summary>
        /// Constructs a PathFixer object
        /// </summary>
        /// <param name="lowerCaseDirectories">Whether or not to lowercase directory segments in the path</param>
        /// <param name="slashType">The slash type to validate/normalize to</param>
        public PathFixer(bool lowerCaseDirectories, SlashType slashType)
        {
            switch (slashType)
            {
                case SlashType.Default:
                case SlashType.Unix:
                    ExpectedPathSeparator = '/';
                    IllegalPathSeparator = '\\';
                    break;
                case SlashType.Windows:
                    ExpectedPathSeparator = '\\';
                    IllegalPathSeparator = '/';
                    break;
                default:
                    throw Contract.AssertFailure("Unexpected enum value for SlashType");
            }

            LowerCaseDirectories = lowerCaseDirectories;
        }

        /// <summary>
        /// The path separator that is expected to be used in all paths
        /// </summary>
        private char ExpectedPathSeparator { get; } = '/';

        /// <summary>
        /// The path separator that is illegal in the spec
        /// </summary>
        private char IllegalPathSeparator { get; } = '\\';

        /// <summary>
        /// Whether all directories should be lowercased
        /// </summary>
        private bool LowerCaseDirectories { get; }

        /// <summary>
        /// Fixes a path node
        /// </summary>
        /// <returns>true if the path was fixed, false otherwise</returns>
        public bool Fix(INode node)
        {
            return PathFix(node, FixPathFragmentLiteral);
        }

        /// <summary>
        /// Analyzes a path node
        /// </summary>
        /// <returns>true if the path has no analysis errors, false otherwise</returns>
        public bool Analyze(INode node, DiagnosticsContext context, Logger logger, LoggingContext loggingContext)
        {
            return PathFix(node, (literalLikeNode, maintainCaseOfLastStep) => AnalyzePathFragmentLiteral(literalLikeNode, maintainCaseOfLastStep, context, logger, loggingContext));
        }

        private static bool PathFix(INode node, Func<ILiteralLikeNode, bool, bool> fixerFunc)
        {
            var taggedTemplateExpression = node.Cast<ITaggedTemplateExpression>();

            if (!taggedTemplateExpression.IsPathInterpolation())
            {
                // Simply skip this node.
                return true;
            }

            bool maintainCaseOfLastStep = taggedTemplateExpression.GetInterpolationKind() != InterpolationKind.DirectoryInterpolation;

            // in case of just literal:
            var literal = taggedTemplateExpression.TemplateExpression.As<ILiteralExpression>();
            if (literal != null)
            {
                return fixerFunc(literal, maintainCaseOfLastStep);
            }

            // in case of more complicated template expression
            var template = taggedTemplateExpression.TemplateExpression.Cast<ITemplateExpression>();

            if (template.Head != null)
            {
                bool thisIsLastFragment = template.TemplateSpans.Count == 0;
                if (!fixerFunc(template.Head, maintainCaseOfLastStep && thisIsLastFragment))
                {
                    return false;
                }
            }

            var spans = template.TemplateSpans;
            for (int i = 0; i < spans.Length; i++)
            {
                var span = spans[i];

                if (span.Literal != null)
                {
                    bool thisIsLastFragment = i == spans.Length - 1;
                    if (!fixerFunc(span.Literal, maintainCaseOfLastStep && thisIsLastFragment))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private bool AnalyzePathFragmentLiteral(ILiteralLikeNode node, bool maintainCaseOfLast, DiagnosticsContext context, Logger logger, LoggingContext loggingContext)
        {
            Contract.Requires(context != null, "context is required when analyzing and validating.");

            var illegalIndex = node.Text.IndexOf(IllegalPathSeparator);
            if (illegalIndex >= 0)
            {
                var location = node.LocationForLogging(context.SourceFile);
                location.Position += illegalIndex;

                logger.PathFixerIllegalPathSeparator(loggingContext, location, node.Text, ExpectedPathSeparator, IllegalPathSeparator);
                return false;
            }

            if (!LowerCaseDirectories)
            {
                return true;
            }

            var fragments = node.Text.Split(ExpectedPathSeparator, IllegalPathSeparator);

            int charOffSetForError = 0;
            int nrOfFragmentsToFix = fragments.Length;
            if (maintainCaseOfLast)
            {
                // Don't address the case of the last fragment.
                nrOfFragmentsToFix--;
            }

            // lowercase all parts of the path when requested.
            for (int i = 0; i < nrOfFragmentsToFix; i++)
            {
                var fragment = fragments[i];
                var lowerFragment = fragment.ToLowerInvariant();

                if (!string.Equals(lowerFragment, fragment, StringComparison.Ordinal))
                {
                    // Try to find an estimate of the exact character that is a mismatch
                    var upperBound = Math.Min(lowerFragment.Length, fragment.Length);
                    for (int posInFragment = 0; posInFragment < upperBound; posInFragment++)
                    {
                        if (lowerFragment[posInFragment] != fragment[posInFragment])
                        {
                            charOffSetForError += posInFragment;
                        }
                    }

                    var location = node.LocationForLogging(context.SourceFile);
                    location.Position += charOffSetForError;

                    logger.PathFixerIllegalCasing(loggingContext, location, node.Text, fragment, lowerFragment);
                    return false;
                }

                charOffSetForError += fragment.Length + 1; /*1 extra for the path separator*/
            }

            return true;
        }

        /// <summary>
        /// Takes a literal node that is part of a path fragment and report errors or fixes up the paths.
        /// Example of a fragment is 'folder/subFolder\file.txt'
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private bool FixPathFragmentLiteral(ILiteralLikeNode node, bool maintainCaseOfLast)
        {
            var fragments = node.Text.Split(ExpectedPathSeparator, IllegalPathSeparator);
            if (LowerCaseDirectories)
            {
                int charOffSetForError = 0;
                int nrOfFragmentsToFix = fragments.Length;
                if (maintainCaseOfLast)
                {
                    // Don't address the case of the last fragment.
                    nrOfFragmentsToFix--;
                }

                // lowercase all parts of the path when requested.
                for (int i = 0; i < nrOfFragmentsToFix; i++)
                {
                    var fragment = fragments[i];
                    var lowerFragment = fragment.ToLowerInvariant();
                    fragments[i] = lowerFragment;
                    charOffSetForError += fragment.Length + 1; /*1 extra for the path separator*/
                }
            }

            node.Text = string.Join(ExpectedPathSeparator.ToString(), fragments);
            return true;
        }

        /// <summary>
        /// To which slashes to normalize
        /// </summary>
        public enum SlashType
        {
            /// <nodoc />
            Default = 0,

            /// <nodoc />
            Unix,

            /// <nodoc />
            Windows,
        }
    }
}
