// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.CLI.Utils;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities.CLI
{
    /// <nodoc/>
    public enum PrefixKind
    {
        /// <summary>Indicates that a short form prefix was specified, when different from the long form (e.g., "-").</summary>
        Short,

        /// <summary>Indicates that a long form prefix was specified, when different from the short from (e.g., "--").</summary>
        Long,

        /// <summary>Indicates that a prefix was specified, when both short and long forms are the same (e.g., "/").</summary>
        Either,

        /// <summary>Indicates that no prefix was specified.</summary>
        None,
    }

    /// <summary>
    ///     Stores string values for option's key/value, as well as its prefix kind.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    public sealed class ParsedOption
    {
        /// <summary>Reusable 0-length array</summary>
        public static readonly IEnumerable<ParsedOption> EmptyCollection = CollectionUtilities.EmptyArray<ParsedOption>();

        /// <nodoc/>
        public PrefixKind PrefixKind { get; }

        /// <nodoc/>
        public string Key { get; }

        /// <nodoc/>
        public string Value { get; }

        /// <nodoc/>
        public ParsedOption(PrefixKind prefixKind, string key, string value)
        {
            PrefixKind = prefixKind;
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    ///     Parsers should accomodate different styles of command line options (e.g., /key:value vs. --key value).
    /// </summary>
    public interface IParser
    {
        /// <summary>
        ///     Parses one option from a given queue of string arguments.  The parser is at liberty to
        ///     decide how many elements to dequeue from <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        ///     <b>Mutates <paramref name="args"/></b> (by dequeuing elements from it).
        /// </remarks>
        [NotNull]
        ParsedOption Parse(Queue<string> args);

        /// <summary>
        ///     Produces a single-line string rendered option values from <paramref name="conf"/>.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        string Render([NotNull]Config conf);

        /// <summary>
        ///     Renders a single option name/value pair with a given prefix.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        string RenderSingleOption(PrefixKind prefixKind, [NotNull]string name, [CanBeNull]string value);

        /// <summary>
        ///     Splits a given non-null string into command-line args, following conventions for
        ///     quoting and escaping.  If there is a mismatched quote, a closing quote at the end
        ///     is assumed (so that an error is never thrown).
        ///
        ///     Examples:
        ///         - arg1 "this is arg2" arg3 --> [ "arg1", "this is arg2", "arg3" ]
        ///         - arg1 this\ is\ arg2 arg3 --> [ "arg1", "this is arg2", "arg3" ]
        ///         - arg1 "this \"is\" arg2" arg3 --> [ "arg1", "this \"is\" arg2", "arg3" ]
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        string[] SplitArgs([NotNull]string args);
    }

    /// <summary>
    ///     Extension methods for <see cref="IParser"/>.
    /// </summary>
    public static class ParserExtensions
    {
        /// <summary>
        ///     Simply expands <paramref name="option"/> and delegates the call to
        ///     <see cref="IParser.RenderSingleOption(PrefixKind, string, string)"/>.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        public static string RenderSingleOption(this IParser parser, ParsedOption option)
        {
            return parser.RenderSingleOption(option.PrefixKind, option.Key, option.Value);
        }

        /// <summary>
        ///     Render usage for given options.
        /// </summary>
        public static string Usage(this IParser parser, Option[] options, string indent = "", string tab = "    ")
        {
            Contract.Requires(options != null);

            var result = new StringBuilder();
            foreach (var o in options)
            {
                var typeName = "[" + o.ValueType.Name + "]";
                var longNameWithType = parser.RenderSingleOption(PrefixKind.Long, o.LongName, typeName);
                var switches = string.IsNullOrWhiteSpace(o.ShortName)
                    ? longNameWithType
                    : parser.RenderSingleOption(PrefixKind.Short, o.ShortName, string.Empty) + ", " + longNameWithType;
                result.Append(indent).Append(o.IsRequired ? "(REQUIRED) " : string.Empty).AppendLine(switches);
                result.Append(indent).Append(tab).AppendLine(o.HelpText);
                result.AppendLine();
            }

            return result.ToString();
        }
    }

    /// <summary>
    ///     Abstract parser, providing a few utility methods for concrete parsers to use.
    /// </summary>
    public abstract class AbstractParser
    {
        /// <summary>
        ///     Template method which concrete parsers are likely to want to use: it iterates through all
        ///     <see cref="Config.ConfiguredOptionValues"/>, and for each value calls virtual
        ///     <see cref="RenderSingleOption"/>, which concrete parsers must implement.
        /// </summary>
        public string Render(Config conf)
        {
            Contract.Requires(conf != null);

            return string.Join(
                " ",
                conf.ConfiguredOptionValues.SelectMany(grp =>
                    grp.Select(val => RenderSingleOption(PrefixKind.Long, grp.Key.LongName, val))));
        }

#pragma warning disable CA1822 // Member SplitArgs does not access instance data and can be marked as static
        /// <summary><see cref="CommonSplitArgs(string)"/></summary>
        public string[] SplitArgs(string args)
        {
            Contract.Requires(args != null);
            return CommonSplitArgs(args);
        }
#pragma warning restore CA1822 // Member SplitArgs does not access instance data and can be marked as static

        /// <summary>
        ///     Escapes a given string value so that when rendered (see <see cref="Render"/>) and
        ///     then parsed again (see <see cref="IParser.Parse"/>) the same value is obtained.
        /// </summary>
        protected virtual string EscapeArg(string value) => CommonEscapeArg(value);

        /// <summary>
        ///     Renders a single option name/value pair with a given prefix. <paramref name="value"/> may be null.
        /// </summary>
        public virtual string RenderSingleOption(PrefixKind prefixKind, string name, string value)
        {
            Contract.Requires(name != null);

            var prefix = GetPrefixString(string.IsNullOrWhiteSpace(name) ? PrefixKind.None : prefixKind);
            var separator = GetSeparatorString(prefixKind, name, value);
            return I($"{prefix}{EscapeArg(name)}{separator}{EscapeArg(value ?? string.Empty)}");
        }

        /// <summary>Returns the prefix to use when rendering a single option (<seealso cref="RenderSingleOption"/>).</summary>
        protected abstract string GetPrefixString(PrefixKind prefix);

        /// <summary>Returns the separator (between key vand value) to use when rendering a single option (<seealso cref="RenderSingleOption"/>).</summary>
        protected abstract string GetSeparatorString(PrefixKind prefix, string name, string value);

        /// <summary>
        ///     Generic implementation from escaping argument values that most parsers will reuse.
        /// </summary>
        public static string CommonEscapeArg(string value)
        {
            value = value.Replace("\"", "\\\"").Replace(@"\", @"\\");
            return value.IndexOf(" ", StringComparison.Ordinal) != -1
                ? "\"" + value + "\""
                : value;
        }

        /// <summary>
        ///     Generic implementation for <see cref="IParser.SplitArgs(string)"/>.
        /// </summary>
        public static string[] CommonSplitArgs(string cmdLine)
        {
            var chars = cmdLine.ToCharArray();
            var result = new List<string>();
            var quoting = false;
            var currentArg = new StringBuilder();

            Action fnAddArg = () =>
            {
                var arg = currentArg.ToString();
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    result.Add(arg);
                }

                currentArg.Clear();
            };

            Func<int, int> fnAddChar = (idx) =>
            {
                var j = idx;
                var ch = chars[j++];
                var nextCharIdx = j;
                if (ch == '\\' && nextCharIdx < chars.Length && // current char is '\' and there exists next char, AND
                    ((!quoting && In(chars[nextCharIdx], '\\', ' ', '"')) || //   (not quoting and next char in ['\', ' ', '"'] OR
                     (quoting && chars[nextCharIdx] == '"')) /* quoting and next char is '"' */)
                {
                    ch = chars[j++];
                }

                currentArg.Append(ch);
                return j;
            };

            int i = 0;
            while (i < chars.Length)
            {
                var ch = chars[i];
                if (quoting)
                {
                    if (ch == '"')
                    {
                        quoting = false;
                        i++;
                    }
                    else
                    {
                        i = fnAddChar(i);
                    }
                }
                else
                {
                    if (ch == '"')
                    {
                        quoting = true;
                        i++;
                    }
                    else if (ch == ' ')
                    {
                        fnAddArg();
                        i++;
                    }
                    else
                    {
                        i = fnAddChar(i);
                    }
                }
            }

            fnAddArg();
            return result.ToArray();
        }

        private static bool In(char ch, params char[] set)
        {
            return set.Contains(ch);
        }
    }

    /// <summary>
    ///     Parser implementing Windows-style options.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    public sealed class WinParser : AbstractParser, IParser
    {
        /// <summary>Shareable instance.</summary>
        public static readonly WinParser Instance = new WinParser();

        /// <inheritdoc/>
        /// <summary>
        ///     Always dequeues just one argument from <paramref name="args"/> and passes it down to <see cref="ParseWinOption"/>.
        /// </summary>
        public ParsedOption Parse(Queue<string> args)
        {
            Contract.Requires(args != null);
            Contract.Requires(args.Count > 0);
            return ParseWinOption(args.Dequeue());
        }

        /// <inheritdoc/>
        protected override string GetPrefixString(PrefixKind prefix) => prefix == PrefixKind.None ? string.Empty : "/";

        /// <inheritdoc/>
        protected override string GetSeparatorString(PrefixKind prefix, string name, string value) => string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) ? string.Empty : ":";

        /// <summary>
        ///     Follows the standard Windows-style format, e.g., /key:value, /key, /key+, /key-, etc.
        /// </summary>
        public static ParsedOption ParseWinOption(string str)
        {
            Contract.Requires(str != null);

            var format = StartsWith(str, "/") ? PrefixKind.Either : PrefixKind.None;
            if (format == PrefixKind.None)
            {
                return new ParsedOption(format, string.Empty, str);
            }

            str = str.Substring(1);
            int indexOfColon = IndexOf(str, ":");
            var keyValue = indexOfColon == -1
                ? new KeyValuePair<string, string>(str, null)
                : new KeyValuePair<string, string>(str.Substring(0, indexOfColon), str.Substring(indexOfColon + 1));
            var finalKeyValue =
                (keyValue.Value == null && EndsWith(keyValue.Key, "+")) ? new KeyValuePair<string, string>(keyValue.Key.Substring(0, keyValue.Key.Length - 1), "true") :
                (keyValue.Value == null && EndsWith(keyValue.Key, "-")) ? new KeyValuePair<string, string>(keyValue.Key.Substring(0, keyValue.Key.Length - 1), "false") :
                keyValue;
            return new ParsedOption(format, finalKeyValue.Key, finalKeyValue.Value);
        }
    }

    /// <summary>
    ///     Parser implementing Unix-style options.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    public sealed class UnixParser : AbstractParser, IParser
    {
        private const string ShortPrefix = "-";
        private const string LongPrefix = "--";

        /// <summary>Shareable instance.</summary>
        public static readonly UnixParser Instance = new UnixParser();

        /// <inheritdoc/>
        /// <summary>
        ///     Dequeues one argument from <paramref name="args"/> and parses the key from it; then peeks
        ///     at the next argument in the queue, and if doesn't start with the "-" prefix, dequeues it
        ///     and treats it as the value (otherwise the value is null).
        /// </summary>
        public ParsedOption Parse(Queue<string> args)
        {
            Contract.Requires(args != null);
            Contract.Requires(args.Count > 0);

            var first = args.Dequeue();
            var second = (args.Count > 0 && StartsWith(first, ShortPrefix) && !StartsWith(args.Peek(), ShortPrefix)) ? args.Dequeue() : null;
            return ParseUnixOption(first, second);
        }

        /// <inheritdoc/>
        protected override string GetPrefixString(PrefixKind prefix) =>
            prefix == PrefixKind.Long ? LongPrefix :
            prefix == PrefixKind.Either ? LongPrefix :
            prefix == PrefixKind.Short ? ShortPrefix : string.Empty;

        /// <inheritdoc/>
        protected override string GetSeparatorString(PrefixKind prefix, string name, string value) => string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) ? string.Empty : " ";

        /// <summary>
        ///     Follows the standard Unix-style format, e.g., --longKey value, -lk value, etc.
        /// </summary>
        public static ParsedOption ParseUnixOption(string key, string value)
        {
            Contract.Requires(key != null);

            var format =
                StartsWith(key, LongPrefix) ? PrefixKind.Long :
                StartsWith(key, ShortPrefix) ? PrefixKind.Short :
                PrefixKind.None;

            var prefix = format == PrefixKind.Long ? LongPrefix : ShortPrefix;

            return format == PrefixKind.None
                ? new ParsedOption(format, string.Empty, key)
                : new ParsedOption(format, key.Substring(prefix.Length), value);
        }
    }

    internal static class Utils
    {
        internal static bool StartsWith(string str, string prefix) => str.StartsWith(prefix, StringComparison.Ordinal);

        internal static bool EndsWith(string str, string suffix) => str.EndsWith(suffix, StringComparison.Ordinal);

        internal static int IndexOf(string str, string substring) => str.IndexOf(substring, StringComparison.Ordinal);
    }
}
