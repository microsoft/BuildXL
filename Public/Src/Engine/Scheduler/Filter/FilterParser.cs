// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

#pragma warning disable SA1203 // Constant fields should appear before non-constant fields

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Parses filter expressions
    /// </summary>
    public sealed class FilterParser
    {
        /// <summary>
        /// Tries to get a path for a mount
        /// </summary>
        /// <param name="mountName">name of the mount</param>
        /// <param name="path">path of the root of the mount</param>
        /// <returns>true if retrieved</returns>
        public delegate bool TryGetPathByMountName(string mountName, out AbsolutePath path);

        private readonly PipExecutionContext m_context;
        private readonly TryGetPathByMountName m_pathResolver;
        private readonly string m_expression;
        private int m_position;

        #region Control characters and strings for parsing filters
        private const char LegacyDependentsFlag = '+';
        private const string DependentsFunction = "dpt";
        private const string DependenciesFunction = "dpc";
        private const string RequiredInputsFunction = "requiredfor";
        private const string CopyDependentsFunction = "copydpt";
        private const char FilterArgumentStartEnd = '\'';
        private const char Negation = '~';
        private const char FilterArgumentSeparator = '=';
        private const char StartGroup = '(';
        private const char EndGroup = ')';

        private const string AndOperator = "and";
        private const string OrOperator = "or";

        private readonly string DirectorySpecs = $"{Path.DirectorySeparatorChar}.";
        private const string PathWildcard = "*";
        private static readonly string RecursiveDirectorySpecs = Path.DirectorySeparatorChar + PathWildcard;
        private const string MountPrefix = "Mount[";
        private const char MountSuffix = ']';
        private readonly char MountRelativePathSeparator = Path.DirectorySeparatorChar;

        private const string FilterTypeTag = "tag";
        private const string FilterTypeInput = "input";
        private const string FilterTypeSpec = "spec";
        private const string FilterTypeSpecValueTransitive = "spec_valuetransitive";
        private const string FilterTypeSpecRef = "specref";
        private const string FilterTypeValue = "value";
        private const string FilterTypeValueTransitive = "valuetransitive";
        private const string FilterTypeId = "id";
        private const string FilterTypeOutput = "output";
        private const string FilterTypeModule = "module";

        #endregion

        /// <summary>
        /// Constructor for a FilterParser
        /// </summary>
        public FilterParser(PipExecutionContext context, TryGetPathByMountName pathResolver, string filterText)
        {
            Contract.Requires(context != null);
            Contract.Requires(pathResolver != null);
            m_context = context;
            m_pathResolver = pathResolver;
            m_expression = filterText;
            m_position = 0;
        }

        /// <summary>
        /// Attempts to parse a filter string from the command line to a RootFilter
        /// </summary>
        /// <remarks>
        /// Filter string is of the format:
        /// [DependencyOperator][Filter]
        ///
        /// where [DependencyOperator] is implicitly all dependencies or "+" for all dependencies and dependents
        /// where [Filter] is a filter of form [filter type]='[filter value]' or  [negation]([Filter]) or [negation]([Filter][operator][Filter])
        /// where [negation] is either "~" or empty
        /// where [operator] is "and" or "or"
        /// </remarks>
        /// <param name="rootFilter">Filter that was parsed</param>
        /// <param name="error">Error from parsing filter string</param>
        /// <returns>true if parsing was successful</returns>
        public bool TryParse(out RootFilter rootFilter, out FilterParserError error)
        {
            Contract.Ensures(Contract.Result<bool>() ^ Contract.ValueAtReturn<RootFilter>(out rootFilter) == null);
            Contract.Ensures(Contract.Result<bool>() ^ Contract.ValueAtReturn<FilterParserError>(out error) != null);

            rootFilter = null;

            if (string.IsNullOrEmpty(m_expression))
            {
                error = new FilterParserError(0, ErrorMessages.NullEmptyFilter);
                return false;
            }

            SkipWhitespace();

            try
            {
                rootFilter = ParseRootFilter();

                // Ensure parsing the filter consumed the entire string
                if (m_position != m_expression.Length)
                {
                    rootFilter = null;
                    error = new FilterParserError(m_position, ErrorMessages.ExpectedEndOfFilter, StartGroup, EndGroup);
                    return false;
                }
            }
            catch (FilterParserException ex)
            {
                error = ex.Error;
                return false;
            }

            error = null;
            return true;
        }

        // General convention in parsing methods: All whitespaces are consumed immediately after advancing the position.
        // Methods can assume they do not need to advance past whitespace when they are called.
        #region Parsing methods
        private RootFilter ParseRootFilter()
        {
            bool includeDependents = false;

            // Parse dependency selection. This may only be specified at the beginning of the root filter, outside any grouping
            if (MatchesNext(LegacyDependentsFlag))
            {
                includeDependents = true;
                m_position++;
            }

            SkipWhitespace();

            PipFilter filter = ParseExpression();
            if (includeDependents)
            {
                filter = new DependentsFilter(filter);
            }

            return new RootFilter(filter, m_expression);
        }

        private PipFilter ParseExpression()
        {
            return ParseBinary();
        }

        private static readonly FilterOperator[] s_filterOperatorsPrecedenceOrder = new[]
        {
            FilterOperator.And,
            FilterOperator.Or
        };

        private PipFilter ParseBinary()
        {
            // Iteratively collect filters and operators used to combine filters
            LinkedList<PipFilter> filters = new LinkedList<PipFilter>();
            LinkedList<FilterOperator> filterOperators = new LinkedList<FilterOperator>();

            while (true)
            {
                PipFilter left = ParseFilterGroup();
                filters.AddFirst(left);

                if (MatchesNext(AndOperator))
                {
                    AdvancePast(AndOperator);
                    SkipWhitespace();
                    filterOperators.AddFirst(FilterOperator.And);
                    continue;
                }
                else if (MatchesNext(OrOperator))
                {
                    AdvancePast(OrOperator);
                    SkipWhitespace();
                    filterOperators.AddFirst(FilterOperator.Or);
                    continue;
                }

                break;
            }
            
            // In precedence order, combine the filter using the operators
            foreach (var filterOperator in s_filterOperatorsPrecedenceOrder)
            {
                LinkedListNode<PipFilter> filterNode = filters.First;
                LinkedListNode<FilterOperator> filterOperatorNode = filterOperators.First;

                while (filterOperatorNode != null)
                {
                    var nextFilterOperatorNode = filterOperatorNode.Next;
                    var nextFilterNode = filterNode.Next;

                    // Check operator if the operator matches the current type of operator being combined
                    if (filterOperatorNode.Value == filterOperator)
                    {
                        // Combine the filters and replace the two filter nodes with a node for the combined filter
                        // This is done by replacing the value in one node and removing the other node
                        filterNode.Next.Value = new BinaryFilter(
                            left: filterNode.Next.Value, 
                            op: filterOperator, 
                            right: filterNode.Value);
                        filters.Remove(filterNode);

                        // Remove the operator now that it is processed
                        filterOperators.Remove(filterOperatorNode);
                    }

                    filterOperatorNode = nextFilterOperatorNode;
                    filterNode = nextFilterNode;
                }
            }

            Contract.Assert(filters.Count == 1);
            return filters.First.Value;
        }

        private PipFilter ParseFilterGroup()
        {
            string matched;
            if (TryMatch(DependentsFunction, out matched) ||
                TryMatch(DependenciesFunction, out matched) ||
                TryMatch(RequiredInputsFunction, out matched) ||
                TryMatch(CopyDependentsFunction, out matched))
            {
                AdvancePast(matched);
                SkipWhitespace();

                // Filter functions (i.e. dpt) is only allowed outside of group operators (). Check that the next character is the start
                // of a group but don't consume is since that is handled by the recursive call
                if (!MatchesNext(StartGroup))
                {
                    throw CreateException(ErrorMessages.FilterFunctionMustBeOustideOfGroupDelimiters, StartGroup, EndGroup, DependentsFunction);
                }

                PipFilter result = ParseFilterGroup();
                switch (matched)
                {
                    case DependentsFunction:
                        result = new DependentsFilter(result);
                        break;
                    case DependenciesFunction:
                        result = new DependenciesFilter(result);
                        break;
                    case RequiredInputsFunction:
                        result = new DependenciesFilter(result, ClosureMode.DirectExcludingSelf);
                        break;
                    case CopyDependentsFunction:
                    default:
                        Contract.Assert(matched == CopyDependentsFunction, "Unexpected match");
                        result = new CopyDependentsFilter(result);
                        break;
                }

                return result;
            }
            else if (MatchesNext(Negation))
            {
                ExpectAndAdvancePast(Negation);
                SkipWhitespace();

                // Negation is only allowed outside of group operators (). Check that the next character is the start
                // of a group but don't consume is since that is handled by the recursive call
                if (!MatchesNext(StartGroup))
                {
                    throw CreateException(ErrorMessages.NetagionMustBeOustideOfGroupDelimiters, StartGroup, EndGroup);
                }

                PipFilter result = ParseFilterGroup();
                result = result.Negate();
                return result;
            }
            else if (MatchesNext(StartGroup))
            {
                ExpectAndAdvancePast(StartGroup);
                SkipWhitespace();
                PipFilter result = ParseExpression();
                ExpectAndAdvancePast(EndGroup);
                SkipWhitespace();

                return result;
            }
            else
            {
                return ParseFilterTuple();
            }
        }

        private PipFilter ParseFilterTuple()
        {
            SkipWhitespace();

            // Extract filterType and filterArgument for filters of the form:
            // filterType            =                         '             filterArgument          '
            // filterType [FilterArgumentSeparator] [FilterArgumentStartEnd] filterArgument [FilterArgumentStartEnd]
            int startFilterType = m_position;
            SkipWhitespace();

            if (!SeekToNextInstanceOfChar(FilterArgumentSeparator))
            {
                throw CreateException(ErrorMessages.MissingFilterArgumentSeparator, FilterArgumentSeparator);
            }

            int separatorPosition = m_position;
            string filterType = m_expression.Substring(startFilterType, separatorPosition - startFilterType).Trim();

            // Advance past the separator and find the filter argument start delimiter
            ExpectAndAdvancePast(FilterArgumentSeparator);
            SkipWhitespace();
            if (!MatchesNext(FilterArgumentStartEnd))
            {
                throw CreateException(ErrorMessages.MissingStartArgumentDelimiter, FilterArgumentStartEnd);
            }

            // go past the delimiter to find the actual argument
            ExpectAndAdvancePast(FilterArgumentStartEnd);
            int argumentStart = m_position;

            if (!SeekToNextInstanceOfChar(FilterArgumentStartEnd))
            {
                throw CreateException(ErrorMessages.MissingEndArgumentDelimiter, FilterArgumentStartEnd);
            }

            string filterArgument = m_expression.Substring(argumentStart, m_position - argumentStart);
            ExpectAndAdvancePast(FilterArgumentStartEnd);
            SkipWhitespace();

            MatchMode matchMode;
            AbsolutePath absPath;
            string pathWildcard;
            bool pathFromMount;

            // Create the specific filter type
            switch (filterType)
            {
                case FilterTypeTag:
                    return new TagFilter(StringId.Create(m_context.PathTable.StringTable, filterArgument));
                case FilterTypeInput:
                    ParsePathBasedFilter(filterArgument, argumentStart, out matchMode, out absPath, out pathWildcard, out pathFromMount);
                    return new InputFileFilter(absPath, pathWildcard, matchMode, pathFromMount);
                case FilterTypeSpec:
                case FilterTypeSpecValueTransitive:
                case FilterTypeSpecRef:
                    ParsePathBasedFilter(filterArgument, argumentStart, out matchMode, out absPath, out pathWildcard, out pathFromMount);
                    return new SpecFileFilter(absPath, pathWildcard, matchMode, pathFromMount, valueTransitive: filterType == FilterTypeSpecValueTransitive, specDependencies: filterType == FilterTypeSpecRef);
                case FilterTypeModule:
                    return new ModuleFilter(StringId.Create(m_context.PathTable.StringTable, filterArgument));
                case FilterTypeId:
                    // Strip the fixed prefix that we print to the user.
                    if (filterArgument.StartsWith(Pip.SemiStableHashPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        filterArgument = filterArgument.Substring(Pip.SemiStableHashPrefix.Length);
                    }

                    long pipId;
                    if (!long.TryParse(filterArgument, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pipId))
                    {
                        throw CreateException(argumentStart, ErrorMessages.FailedToParsePipId, filterArgument);
                    }

                    return new PipIdFilter(pipId);
                case FilterTypeValue:
                case FilterTypeValueTransitive:
                    FullSymbol symbol;
                    int charWithError;
                    if (FullSymbol.TryCreate(m_context.SymbolTable, filterArgument, out symbol, out charWithError) != FullSymbol.ParseResult.Success)
                    {
                        throw CreateException(argumentStart, ErrorMessages.FailedToParseValueIdentifier, filterArgument);
                    }

                    return new ValueFilter(symbol, valueTransitive: filterType == FilterTypeValueTransitive);
                case FilterTypeOutput:
                    ParsePathBasedFilter(filterArgument, argumentStart, out matchMode, out absPath, out pathWildcard, out pathFromMount);
                    return new OutputFileFilter(absPath, pathWildcard, matchMode, pathFromMount);
                default:
                    if (filterType.IndexOf(LegacyDependentsFlag) >= 0)
                    {
                        throw CreateException(startFilterType, ErrorMessages.DependentsOperatorMayOnlyBeUsedInOuterScope, LegacyDependentsFlag);
                    }

                    throw CreateException(startFilterType, ErrorMessages.UnknownFilterType, filterType);
            }
        }
        #endregion

        #region Parsing Helpers
        private void ParsePathBasedFilter(string filterArgument, int positionInOriginal, out MatchMode matchMode, out AbsolutePath absPath, out string pathWildcard, out bool pathFromMount)
        {
            matchMode = MatchMode.FilePath;

            pathFromMount = false;
            if (filterArgument.StartsWith(MountPrefix, StringComparison.OrdinalIgnoreCase))
            {
                pathFromMount = true;
                string mountName = null;
                string mountRelativePath = null;
                pathWildcard = null;
                int mountRelativePathStart = 0;

                for (int i = MountPrefix.Length, mountNameLength = 0; i < filterArgument.Length; i++, mountNameLength++)
                {
                    if (filterArgument[i] == MountSuffix)
                    {
                        mountName = filterArgument.Substring(MountPrefix.Length, mountNameLength);
                        mountRelativePath = filterArgument.Substring(i + 1);
                        mountRelativePathStart = i;
                        break;
                    }
                }

                if (mountName == null)
                {
                    absPath = AbsolutePath.Invalid;
                    throw CreateException(positionInOriginal + filterArgument.Length, ErrorMessages.ExpectingMountSuffix, MountSuffix);
                }

                if (!m_pathResolver(mountName, out absPath))
                {
                    throw CreateException(positionInOriginal + MountPrefix.Length, ErrorMessages.UnknownMount, mountName);
                }

                if (mountRelativePath.Length == 0)
                {
                    // For a filter where just a mount is specified (ie no trailing relative path)
                    // match everything under the mount.
                    matchMode = MatchMode.WithinDirectoryAndSubdirectories;
                    return;
                }
                else if (mountRelativePath[0] != MountRelativePathSeparator)
                {
                    // Need to have a path separator between and mount and the trailing relative path
                    throw CreateException(positionInOriginal + mountRelativePathStart, ErrorMessages.ExpectingPathSeparatorAfterMountSuffix, MountRelativePathSeparator);
                }

                // Detokenize the mount and parse the filter as normal
                filterArgument = absPath.ToString(m_context.PathTable) + mountRelativePath;
            }

            bool startsWithWildcard = filterArgument.StartsWith(PathWildcard, StringComparison.Ordinal);
            bool endsWithWildcard = filterArgument.EndsWith(PathWildcard, StringComparison.Ordinal);

            if (filterArgument.Length > 1 && filterArgument.IndexOf(PathWildcard, 1, StringComparison.OrdinalIgnoreCase) > -1 &&
                filterArgument.IndexOf(PathWildcard, 1, StringComparison.OrdinalIgnoreCase) != (filterArgument.Length - 1))
            {
                throw CreateException(positionInOriginal, ErrorMessages.WildcardWithinPath, filterArgument);
            }
            else if (startsWithWildcard && endsWithWildcard)
            {
                matchMode = MatchMode.PathPrefixAndSuffixWildcard;
                // Remove trailing PathWildcard
                filterArgument = filterArgument.Remove(filterArgument.Length - PathWildcard.Length, PathWildcard.Length);

                // Remove leading path wildcard
                if (filterArgument.Length >= PathWildcard.Length)
                {
                    filterArgument = filterArgument.Substring(PathWildcard.Length, filterArgument.Length - PathWildcard.Length);
                }

                if (filterArgument.Equals(String.Empty))
                {
                    // Handling special cases: '*' and  '**'
                    // If '*' is given, we'd like to include all. That's why, the filter argument is '\' because all paths have '\'.
                    filterArgument = Path.DirectorySeparatorChar.ToString();
                }
                
                pathWildcard = filterArgument;
                absPath = AbsolutePath.Invalid;
                return;
            }
            else if (filterArgument.EndsWith(RecursiveDirectorySpecs, StringComparison.Ordinal))
            {
                // Check if the path ends with '\*' to denote all paths within the directory and all subdirectories
                matchMode = MatchMode.WithinDirectoryAndSubdirectories;
                filterArgument = filterArgument.Remove(filterArgument.Length - RecursiveDirectorySpecs.Length, RecursiveDirectorySpecs.Length);
                pathWildcard = null;
                ParseAbsolutePath(filterArgument, positionInOriginal + RecursiveDirectorySpecs.Length, out absPath);
                return;
            }
            else if (filterArgument.EndsWith(DirectorySpecs, StringComparison.Ordinal))
            {
                // Check if the path ends with '.' to denote paths only directly with the directory
                matchMode = MatchMode.WithinDirectory;
                filterArgument = filterArgument.Remove(filterArgument.Length - DirectorySpecs.Length, DirectorySpecs.Length);
                pathWildcard = null;
                ParseAbsolutePath(filterArgument, positionInOriginal + DirectorySpecs.Length, out absPath);
                return;
            }
            else if (endsWithWildcard)
            {
                matchMode = MatchMode.PathSuffixWildcard;
                filterArgument = filterArgument.Remove(filterArgument.Length - PathWildcard.Length, PathWildcard.Length);

                // The wildcard may have been midway through the last path part. To prevent GetFullPath from messing with it,
                // we must trim off the end and add it back on
                var dirSeparatorIndex = filterArgument.LastIndexOf(Path.DirectorySeparatorChar);
                if (dirSeparatorIndex != -1)
                {
                    pathWildcard = GetFullPath(filterArgument.Substring(0, dirSeparatorIndex), positionInOriginal);
                    pathWildcard += filterArgument.Substring(dirSeparatorIndex);
                }
                else
                {
                    pathWildcard = GetFullPath(filterArgument, positionInOriginal);
                }

                absPath = AbsolutePath.Invalid;
                return;
            }
            else if (startsWithWildcard)
            {
                matchMode = MatchMode.PathPrefixWildcard;
                filterArgument = filterArgument.Substring(PathWildcard.Length, filterArgument.Length - PathWildcard.Length);
                pathWildcard = filterArgument;
                absPath = AbsolutePath.Invalid;
                return;
            }
            else
            {
                ParseAbsolutePath(filterArgument, positionInOriginal, out absPath);
                pathWildcard = null;
                return;
            }
        }

        private void ParseAbsolutePath(string path, int position, out AbsolutePath resultPath)
        {
            string expanded = GetFullPath(path, position);
            if (!AbsolutePath.TryCreate(m_context.PathTable, expanded, out resultPath))
            {
                throw CreateException(position, ErrorMessages.FailedToCreateAbsolutePath, expanded);
            }
        }

        private static string GetFullPath(string path, int position)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (IOException ex)
            {
                throw CreateException(position, ErrorMessages.GetFullPathFailed, path, ex.Message);
            }
            catch (ArgumentException ex)
            {
                throw CreateException(position, ErrorMessages.GetFullPathFailed, path, ex.Message);
            }
            catch (NotSupportedException ex)
            {
                throw CreateException(position, ErrorMessages.GetFullPathFailed, path, ex.Message);
            }
        }

        private FilterParserException CreateException(string errorMessage, params object[] args)
        {
            return CreateException(m_position, errorMessage, args);
        }

        private static FilterParserException CreateException(int position, string errorMessage, params object[] args)
        {
            return new FilterParserException(
                new FilterParserError(position, errorMessage, args));
        }
        #endregion

        #region Expression checking and advancing
        private bool TryMatch(string next, out string matched)
        {
            if (MatchesNext(next))
            {
                matched = next;
                return true;
            }

            matched = null;
            return false;
        }

        private bool MatchesNext(string next)
        {
            int originalPosition = m_position;

            foreach (char c in next)
            {
                if (!MatchesNext(c))
                {
                    m_position = originalPosition;
                    return false;
                }

                m_position++;
            }

            m_position = originalPosition;
            return true;
        }

        private bool MatchesNext(char next)
        {
            if (m_expression.Length > m_position && m_expression[m_position] == next)
            {
                return true;
            }

            return false;
        }

        private void AdvancePast(string next)
        {
            if (!MatchesNext(next))
            {
                throw new FilterParserException(
                    new FilterParserError(m_position, ErrorMessages.ExpectedToEncounter, next));
            }

            m_position = m_position + next.Length;
        }

        private void ExpectAndAdvancePast(char expected)
        {
            if (!MatchesNext(expected))
            {
                throw new FilterParserException(
                    new FilterParserError(m_position, ErrorMessages.ExpectedToEncounter, expected));
            }

            m_position++;
        }

        private bool SeekToNextInstanceOfChar(char character)
        {
            for (int i = m_position; i < m_expression.Length; i++)
            {
                if (m_expression[i] == character)
                {
                    m_position = i;
                    return true;
                }
            }

            return false;
        }

        private void SkipWhitespace()
        {
            while (m_position < m_expression.Length && char.IsWhiteSpace(m_expression[m_position]))
            {
                m_position++;
            }

            return;
        }
        #endregion

        /// <summary>
        /// Exception used internally to signal an error and stop parsing the filter.
        /// </summary>
        /// <remarks>
        /// All thrown instances should be handled within FilterParser and not escape out public methods
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2237:MarkISerializableTypesWithSerializable",
            Justification = "Exceptions do not propagate outside this class")]
        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors",
            Justification = "Exceptions do not propagate outside this class")]
        [SuppressMessage("Microsoft.Design", "CA1064:ExceptionsShouldBePublic",
            Justification = "Exceptions do not propagate outside this class")]
        private sealed class FilterParserException : Exception
        {
            /// <summary>
            /// Creates a FilterParserException from an error
            /// </summary>
            public FilterParserException(FilterParserError error)
            {
                Error = error;
            }

            /// <summary>
            /// The Error
            /// </summary>
            public FilterParserError Error { get; }
        }
    }
}
