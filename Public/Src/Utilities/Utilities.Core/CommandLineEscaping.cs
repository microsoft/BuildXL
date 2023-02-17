// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Text;

#nullable enable

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Utilities for escaping strings for use on a process' command-line.
    /// </summary>
    public static class CommandLineEscaping
    {
#if NETCOREAPP
        /// <summary>
        /// An element in the array of arguments obtained by splitting a command line string.
        /// </summary>
        public readonly struct Arg
        {
            /// <summary>
            /// Argument value.
            /// </summary>
            public ReadOnlyMemory<char> Value { get; init; }

            /// <summary>
            /// Span into the original command line string from which the argument was parsed.
            /// </summary>
            public ReadOnlyMemory<char> Raw { get; init; }
        }

        /// <summary>
        /// Splits a command line string into individual arguments, using Windows or Unix C runtime rules.
        /// 
        /// See: https://docs.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments?redirectedfrom=MSDN&amp;view=msvc-170
        /// </summary>
        /// <param name="arguments">The command line to split into arguments.</param>
        /// <param name="useWindowsRules">Whether to use Windows or Unix rules. Defaults to current platform's rules.</param>
        /// <returns>Non-materialized enumerator, i.e., returning args as it goes, one at a time.</returns>
        [SuppressMessage("Style", "SA1503: Braces should not be omitted", Justification = "It's ok")]
        public static IEnumerable<Arg> SplitArguments(string arguments, bool? useWindowsRules = null)
        {
            if (arguments.Length == 0)
            {
                yield break;
            }

            using var sbHandle = Pools.GetStringBuilder();
            StringBuilder currentArg = sbHandle.Instance;
            ReadOnlyMemory<char> chars = arguments.AsMemory();
            int currentArgStartIdx = 0;
            int currentRawArgStartIdx;
            bool quoting = false;
            bool isWindows = useWindowsRules ?? OperatingSystemHelper.IsWindowsOS;

            // Adds the char at position 'idx' in 'chars' to the current argument builder.
            // Additionally remembers the starting index of the argument the first time a char is added to it.
            void AddChar(int idx)
            {
                if (idx < chars.Length)
                {
                    if (currentArg.Length == 0) currentArgStartIdx = idx;
                    currentArg.Append(chars.Span[idx]);
                }
            }

            // Finishes the current argument. 
            Arg FinishCurrentArg(int currentIndex)
            {
                // original raw string (from start index to current index)
                ReadOnlyMemory<char> raw = arguments.AsMemory().Slice(currentRawArgStartIdx, length: currentIndex - currentRawArgStartIdx);

                // check if we can return a ReadOnlyMemory slice straight from 'arguments' instead of materializing the string from 'currentArg'
                bool matches = arguments.AsMemory().Slice(currentArgStartIdx).Span.StartsWith(currentArg.ToString().AsSpan(), StringComparison.Ordinal);
                ReadOnlyMemory<char> parsedValue = matches
                    ? arguments.AsMemory().Slice(currentArgStartIdx, length: currentArg.Length)
                    : currentArg.ToString().AsMemory();
                currentArg.Clear();
                currentArgStartIdx = currentIndex;
                return new Arg { Value = parsedValue, Raw = raw };
            }

            // returns the char at 'idx' if 'idx' is within bounds
            char PeekChar(int idx) => idx < chars.Length ? chars.Span[idx] : '\0';

            // returns true iff 'ch' is a whitespace character (not using char.IsWhiteSpace because it may include chars like \r and \n)
            static bool IsWhiteSpace(char ch) => ch == ' ' || ch == '\t';

            // returns true iff 'ch' is a backslash
            static bool IsBackslash(char ch) => ch == '\\';

            // increments 'idx' until the character at that position in 'arguments' is not a whitespace or 'idx' runs out of bounds
            int SkipWhiteSpace(int idx) => SkipWhile(idx, IsWhiteSpace);

            // increments 'idx' until 'pred' returns false for the character at that position in 'arguments' (or until 'idx' runs out of bounds)
            int SkipWhile(int idx, Predicate<char> pred)
            {
                while (idx < chars.Length && pred(chars.Span[idx])) idx++;
                return idx;
            }

            // returns whether the sequence of characters starting from 'idx' is a number of backslashes followed by a double quote
            bool IsBackslashesFollowedByDoubleQuote(int idx)
            {
                idx = SkipWhile(idx, IsBackslash);
                return idx < chars.Length && chars.Span[idx] == '"';
            }

            int i = currentRawArgStartIdx = SkipWhiteSpace(0);
            while (i < chars.Length)
            {
                char currChar = chars.Span[i];
                char nextChar = PeekChar(i + 1);
                switch (quoting, currChar, nextChar, isWindows)
                {
                    // [Escape next char]
                    //   - (Windows) a double quote escapes another double quote while quoting
                    //   - (Linux) a backslash escapes a whitespace while not quoting
                    //   - a backslash always escapes a double quote
                    //   - a backslash escapes another backslash: always on Linux, only when followed by a double quote on Windows
                    //     (example1: `a\\\"b` -> `a\"b` on both platforms)
                    //     (example2: `a\\b` -> `a\\b` on Windows and `a\b` on Linux)
                    case (quoting: true,  currChar: '"',  nextChar: '"', isWindows: true):
                    case (quoting: false, currChar: '\\', nextChar: ' ', isWindows: false):
                    case (quoting: _,     currChar: '\\', nextChar: '"', _):
                    case (quoting: _,     currChar: '\\', nextChar: '\\', _) when (!isWindows || IsBackslashesFollowedByDoubleQuote(i + 1)):
                        AddChar(i + 1);
                        i += 2;
                        break;
                    // [End Quoting]
                    case (quoting: true, currChar: '"', _, _):
                        quoting = false;
                        i++;
                        break;
                    // [Start Quoting]
                    case (quoting: false, currChar: '"', _, _):
                        quoting = true;
                        i++;
                        break;
                    // [Finish Current Arg]
                    case (quoting: false, currChar: _, _, _) when IsWhiteSpace(currChar):
                        yield return FinishCurrentArg(i);
                        currentRawArgStartIdx = i = SkipWhiteSpace(i + 1);
                        break;
                    // [Add current char to current arg]
                    default: 
                        AddChar(i);
                        i++;
                        break;
                }
            }

            // consume any unfinished arg
            if (!IsWhiteSpace(chars.Span[chars.Length - 1]))
            {
                yield return FinishCurrentArg(i);
            }
        }
#endif

        /// <summary>
        /// Escapes the given application name (<paramref name="exe" />; for CreateProcess) and appends it to the
        /// <see cref="StringBuilder" />
        /// See <see cref="EscapeAsCreateProcessApplicationName" />.
        /// </summary>
        private static void AppendEscapedCreateProcessApplicationName(this StringBuilder appendTo, string exe)
        {
            string trimmedExe = exe.Trim();

            Contract.AssertNotNullOrEmpty(trimmedExe, "Application names for CreateProcess cannot be empty (after whitespace is removed).");
            Contract.Assume(!trimmedExe.Contains("\""), "Application names for CreateProcess cannot be escaped to contain quotes.");

            bool quotesNeeded = ContainsDelimiter(trimmedExe);
            if (quotesNeeded)
            {
                appendTo.Append('"');
            }

            appendTo.Append(trimmedExe);

            if (quotesNeeded)
            {
                appendTo.Append('"');
            }
        }

        /// <summary>
        /// Escapes a string for usage as an app name embedded in the lpCommandLine parameter of CreateProcess
        /// (see http://msdn.microsoft.com/en-us/library/windows/desktop/ms682425(v=vs.85).aspx).
        /// <see cref="System.Diagnostics.Process" /> performs similar escaping for <see cref="System.Diagnostics.ProcessStartInfo.FileName" />
        /// It is invalid to attempt to escape a string that contains only whitespace or contains double-quotes.
        /// </summary>
        /// <param name="exe">Application name / path to escape. May not contain double quotes.</param>
        /// <returns>An escaped path</returns>
        public static string EscapeAsCreateProcessApplicationName(string exe)
        {
            using PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder();
            StringBuilder builder = wrap.Instance;
            builder.AppendEscapedCreateProcessApplicationName(exe);
            return builder.ToString();
        }

        /// <summary>
        /// Escapes the given command-line <paramref name="word" /> and appends it to the <see cref="StringBuilder" />
        /// See <see cref="EscapeAsCommandLineWord" />.
        /// </summary>
        /// <param name="builder">Optional builder.</param>
        /// <param name="word">Optional word.</param>
        /// <returns>Length of the escaped string</returns>
        public static int AppendEscapedCommandLineWord(this StringBuilder? builder, string word)
        {
            int length = 0;

            // Empty words are a special case. They don't contain a delimiter by definition, but need quotes anyway.
            if (word.Length == 0)
            {
                length += Append(builder, "\"\"");
                return length;
            }

            bool containsDelimiter = ContainsDelimiter(word);

            // Note that we do not check for backslashes here. A string such as C:\foo\ doesn't need any escaping
            // since backslashes only have meaning when succeeded by a ".
            if (!containsDelimiter && word.IndexOf('"') < 0)
            {
                length += Append(builder, word);
                return length;
            }

            // We count consecutive backslashes to append later, since we don't know what to do with them until we see
            // the next non-backslash character.
            int numberOfBufferedBackslashes = 0;

            // To be minimal, we only quote words with delimiters. We still need to handle quotes and backslashes.
            // e.g. a\"b -> a\\\"b
            if (containsDelimiter)
            {
                length += Append(builder, '"');
            }

            foreach (char c in word)
            {
                if (c == '\\')
                {
                    numberOfBufferedBackslashes++;
                }
                else if (c == '"')
                {
                    // Each buffered backslash needs to be escaped. \ -> \\
                    length += Repeat(builder, "\\\\", numberOfBufferedBackslashes);
                    numberOfBufferedBackslashes = 0;

                    // Since this is a literal quote, we need a backslash to escape it.
                    length += Append(builder, "\\\"");
                }
                else
                {
                    // Each buffered backslash can now be appended as-is, since they have no
                    // special meaning when preceding c.
                    length += Repeat(builder, "\\", numberOfBufferedBackslashes);
                    numberOfBufferedBackslashes = 0;

                    length += Append(builder, c);
                }
            }

            if (containsDelimiter)
            {
                // This is just like the c == '"' case, but the quote is special (not literal).
                // One less backslash is used.
                length += Repeat(builder, "\\\\", numberOfBufferedBackslashes);
                length += Append(builder, '"');
            }
            else
            {
                // This is just like the c != '"' (else) case. The backslashes are the final characters
                // in the string so do not have special meaning.
                length += Repeat(builder, "\\", numberOfBufferedBackslashes);
            }

            return length;
        }

        /// <summary>
        /// Calls <see cref="AppendEscapedCommandLineWord(StringBuilder, string)"/> and asserts that the return value
        /// is indeed the difference of the original and the final length of the builder.
        /// </summary>
        public static int AppendEscapedCommandLineWordAndAssertPostcondition(this StringBuilder builder, string word)
        {
            var originalLength = builder.Length;
            var numCharsAdded = builder.AppendEscapedCommandLineWord(word);
            Contract.Assert(originalLength + numCharsAdded == builder.Length);
            return numCharsAdded;
        }

        /// <summary>
        /// Escapes the given string such that the Microsoft C or C++ runtimes would parse it as a single command-line word
        /// (element in argv after parsing).
        /// A command line can be formed for a conformant parser by joining words with spaces:
        /// <c>string joinedArgs = string.Join(" ", args.Select(EscapeAsCommandLineWord);</c>
        /// A full command line including program name (suitable for CreateProcess) can be formed by using
        /// <see cref="EscapeAsCreateProcessApplicationName" />:
        /// <c>string fullCommandLine = EscapeAsCreateProcessApplicationName(appName) + " " + joinedArgs;</c>
        /// </summary>
        /// <param name="word">String to escape. The string may safely contain spaces, double quotes, and backslashes.</param>
        /// <returns>An escaped string</returns>
        /// <remarks>
        /// See http://msdn.microsoft.com/en-us/library/a1y7w461.aspx and http://msdn.microsoft.com/en-us/library/17w5ykft.aspx
        /// Rules (from the C runtime link):
        /// - Arguments are delimited by white space, which is either a space or a tab.
        /// - A string surrounded by double quotation marks is interpreted as a single argument, regardless of white space
        /// contained within. A quoted string can be embedded in
        /// an argument. Note that the caret (^) is not recognized as an escape character or delimiter.
        /// - A double quotation mark preceded by a backslash, \", is interpreted as a literal double quotation mark (").
        /// - Backslashes are interpreted literally, unless they immediately precede a double quotation mark.
        /// - If an even number of backslashes is followed by a double quotation mark, then one backslash (\) is placed in the argv
        /// array for every pair of backslashes (\\),
        /// and the double quotation mark (") is interpreted as a string delimiter.
        /// - If an odd number of backslashes is followed by a double quotation mark, then one backslash (\) is placed in the argv
        /// array for every pair of backslashes (\\)
        /// and the double quotation mark is interpreted as an escape sequence by the remaining backslash, causing a literal double
        /// quotation mark (") to be placed in argv.
        /// </remarks>
        public static string EscapeAsCommandLineWord(string word)
        {
            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                StringBuilder builder = wrap.Instance;
                var length = builder.AppendEscapedCommandLineWord(word);
                var result = builder.ToString();
                Contract.Assert(length == result.Length);
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Append(StringBuilder? builder, string value)
        {
            builder?.Append(value);

            return value.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Append(StringBuilder? builder, char value)
        {
            builder?.Append(value);

            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Repeat(StringBuilder? builder, string s, int count)
        {
            if (builder != null)
            {
                for (int i = 0; i < count; i++)
                {
                    builder.Append(s);
                }
            }

            return count * s.Length;
        }

        private static readonly HashSet<char> s_unixDelimiters = new HashSet<char>
        {
            ' ', '\t', ';', '(', ')', '`', '\''
        };

        private static bool ContainsDelimiter(string s)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                foreach (char c in s)
                {
                    if (s_unixDelimiters.Contains(c))
                    {
                        return true;
                    }
                }
            }
            else
            {
                foreach (char c in s)
                {
                    if (c <= ' ')
                    {
                        if (c == ' ' || c == '\t')
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
