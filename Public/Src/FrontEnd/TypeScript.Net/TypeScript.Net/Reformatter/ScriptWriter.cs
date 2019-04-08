// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL.Utilities;
using TypeScript.Net.Scanning;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// A class that writes pieces of the AST that later can be used to get string representation of it.
    /// </summary>
    /// <remarks>
    /// This class is indented for testing purposes and debugging only.
    /// </remarks>
    public class ScriptWriter : IDisposable
    {
        /// <nodoc/>
        public const int IndentSize = 4;

        private const char IndentChar = ' ';

        /// <nodoc/>
        public const int DefaultMaxCharactersPerLine = 180;

        private const string Space = " ";

        /// <nodoc/>
        public const string SeparateBlockToken = ";";

        /// <nodoc/>
        public const string StartBlockToken = "{";

        /// <nodoc/>
        public const string EndBlockToken = "}";

        /// <nodoc/>
        public const string SeparateTypeLiteralToken = ",";

        /// <nodoc/>
        public const string StartTypeLiteralToken = "{";

        /// <nodoc/>
        public const string EndTypeLiteralToken = "}";

        /// <nodoc/>
        public const string SeparateArgumentsToken = ",";

        /// <nodoc/>
        public const string StartArgumentsToken = "(";

        /// <nodoc/>
        public const string EndArgumentsToken = ")";

        /// <nodoc/>
        public const string SeparateArrayToken = ",";

        /// <nodoc/>
        public const string StartArrayToken = "[";

        /// <nodoc/>
        public const string EndArrayToken = "]";

        private int m_indent;

        private bool m_needToAddSpaceForNextToken;
        private bool m_needToAddIndentedNewLineForNextToken;

        /// <nodoc/>
        public int CurrentLinePosition { get; private set; }

        private PooledObjectWrapper<StringBuilder> m_pooledBuilder;

        /// <nodoc/>
        private StringBuilder Builder { get; }

        /// <summary>
        /// The character position the write currently is on.
        /// </summary>
        public int CharactersRemainingOnCurrentLine => MaxCharactersPerLine - CurrentLinePosition;

        /// <nodoc/>
        public int MaxCharactersPerLine { get; }

        private int IndentLength => m_indent * IndentSize;

        /// <nodoc/>
        public ScriptWriter(int maxCharactersPerLine = DefaultMaxCharactersPerLine)
        {
            m_pooledBuilder = ObjectPools.StringBuilderPool.GetInstance();
            Builder = m_pooledBuilder.Instance;
            MaxCharactersPerLine = maxCharactersPerLine;
        }

        /// <nodoc/>
        public ScriptWriter(StringBuilder builder, int maxCharactersPerLine = DefaultMaxCharactersPerLine)
        {
            Builder = builder;
            MaxCharactersPerLine = maxCharactersPerLine;
        }

        /// <nodoc/>
        public void Dispose()
        {
            if (m_pooledBuilder.Instance != null)
            {
                m_pooledBuilder.Dispose();
            }
        }

        /// <nodoc/>
        public Indenter Indent()
        {
            return new Indenter(this, endBlockToken: null, useNewLine: false);
        }

        /// <nodoc/>
        public Indenter Block(bool useNewLine = true, string startBlockToken = StartBlockToken, string endBlockToken = EndBlockToken)
        {
            if (useNewLine)
            {
                AppendLine(startBlockToken);
            }
            else
            {
                AppendToken(startBlockToken);
            }

            return new Indenter(this, endBlockToken, useNewLine);
        }

        /// <nodoc/>
        public ScriptWriter NoWhitespace()
        {
            m_needToAddSpaceForNextToken = false;
            return this;
        }

        /// <nodoc/>
        public ScriptWriter Whitespace()
        {
            m_needToAddSpaceForNextToken = true;
            return this;
        }

        /// <nodoc/>
        public ScriptWriter NoNewLine()
        {
            m_needToAddIndentedNewLineForNextToken = false;
            return this;
        }

        /// <nodoc/>
        public ScriptWriter NewLine()
        {
            m_needToAddIndentedNewLineForNextToken = true;
            return this;
        }

        /// <nodoc/>
        public ScriptWriter AdditionalNewLine()
        {
            AddNewLine();
            m_needToAddIndentedNewLineForNextToken = true;
            return this;
        }

        /// <nodoc/>
        public ScriptWriter AppendWithNewLineIfNeeded(string token, bool newLineRequired)
        {
            return newLineRequired ? AppendLine(token) : AppendToken(token);
        }

        /// <nodoc/>
        public ScriptWriter AppendToken(SyntaxKind token)
        {
            return AppendToken(Scanner.TokenStrings[token]);
        }

        /// <nodoc/>
        public ScriptWriter ExplicitlyAddNewLine()
        {
            AddNewLine();
            return this;
        }

        /// <nodoc/>
        public ScriptWriter AppendToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return this;
            }

            AddNewLineIfNeeded();
            AddIndentIfNeeded();
            DoAppendToken(token);

            return this;
        }

        /// <nodoc/>
        public ScriptWriter AppendQuotedString(string token, bool isPathString, char quote = '\"')
        {
            AddNewLineIfNeeded();
            AddIndentIfNeeded();

            DoAppendToken(quote);
            AppendEscapedString(token, isPathString, quote);
            DoAppendToken(quote);

            return this;
        }

        /// <nodoc/>
        public ScriptWriter AppendEscapedString(string token, bool isPathString, char quote = '\"')
        {
            foreach (char c in token)
            {
                string encoded = null;

                if (isPathString)
                {
                    if (c == '`')
                    {
                        encoded = "``";
                    }
                }
                else
                {
                    switch (c)
                    {
                        case '\'':
                            encoded = quote == c ? @"\'" : null;
                            break;
                        case '\"':
                            encoded = quote == c ? @"\""" : null;
                            break;
                        case '`':
                            encoded = quote == c ? @"\`" : null;
                            break;
                        case '$':
                            encoded = quote == '`' ? @"\$" : null;
                            break;
                        case '\\':
                            encoded = @"\\";
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
                }

                if (encoded != null)
                {
                    DoAppendToken(encoded);
                }
                else
                {
                    DoAppendToken(c);
                }
            }

            return this;
        }

        /// <nodoc/>
        public ScriptWriter AppendLine(string token)
        {
            AppendToken(token);
            m_needToAddIndentedNewLineForNextToken = true;
            return this;
        }

        /// <nodoc />
        public void AppendItems<T>(IEnumerable<T> items, string open, string close, string separator, Action<T> writeItem)
        {
            AppendToken(open);
            switch (items.Count())
            {
                case 0:
                    {
                        break;
                    }

                case 1:
                    {
                        writeItem(items.First());
                        break;
                    }

                default:
                    {
                        AppendLine(string.Empty);
                        using (Indent())
                        {
                            bool needsSeparator = false;
                            foreach (var item in items)
                            {
                                if (needsSeparator)
                                {
                                    AppendLine(separator);
                                }

                                writeItem(item);

                                needsSeparator = true;
                            }
                        }

                        AppendLine(string.Empty);
                        break;
                    }
            }

            AppendToken(close);
        }

        /// <inheritdoc/>
        public override string ToString() => Builder.ToString();

        private void AddNewLineIfNeeded()
        {
            if (m_needToAddIndentedNewLineForNextToken)
            {
                AddNewLine();
                m_needToAddIndentedNewLineForNextToken = false;
                m_needToAddSpaceForNextToken = false; // Make no sense to add space if new line and indentation were added!
            }
        }

        /// <summary>
        /// Unconditionally adds a new line.
        /// </summary>
        private void AddNewLine()
        {
            Builder.Append("\r\n");

            Builder.Append(IndentChar, IndentLength);
            CurrentLinePosition = IndentLength;
        }

        private void AddIndentIfNeeded()
        {
            if (m_needToAddSpaceForNextToken)
            {
                Builder.Append(Space);
                CurrentLinePosition += Space.Length;
                m_needToAddSpaceForNextToken = false;
            }
        }

        private void DoAppendToken(string token)
        {
            Builder.Append(token);
            CurrentLinePosition += token.Length;
        }

        private void DoAppendToken(char token)
        {
            Builder.Append(token);
            CurrentLinePosition += 1;
        }

        /// <nodoc/>
        public readonly struct Indenter : IDisposable
        {
            private readonly ScriptWriter m_writer;
            private readonly string m_endBlockToken;
            private readonly bool m_useNewLine;

            /// <nodoc/>
            public Indenter(ScriptWriter writer, string endBlockToken, bool useNewLine)
            {
                m_writer = writer;
                m_endBlockToken = endBlockToken;
                m_useNewLine = useNewLine;
                m_writer.m_indent++;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_writer.m_indent--;
                if (m_endBlockToken != null)
                {
                    if (m_useNewLine)
                    {
                        m_writer.AppendLine(m_endBlockToken);
                    }
                    else
                    {
                        m_writer.AppendToken(m_endBlockToken);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to respect new lines between statements by assuming the parser ran with preserveTrivia:true and then looking up the table on the sourcefile.
        /// </summary>
        public ScriptWriter TryToPreserveNewLines(INode node)
        {
            Trivia trivia;
            if (TryGetTrivia(node, out trivia))
            {
                if (trivia.LeadingNewLineCount >= 2)
                {
                    AddNewLine();
                }
            }

            return this;
        }

        /// <summary>
        /// Looks up the leading comments and prints them.
        /// </summary>
        public ScriptWriter TryWriteLeadingComments(INode node)
        {
            Trivia trivia;
            if (TryGetTrivia(node, out trivia))
            {
                if (trivia.LeadingComments != null)
                {
                    if (trivia.LeadingNewLineCount <= 2)
                    {
                        NoNewLine();
                    }

                    foreach (var comment in trivia.LeadingComments)
                    {
                        AppendToken(comment.Content);
                        NewLine();
                    }
                }
            }

            return this;
        }

        /// <nodoc />
        public ScriptWriter TryWriteTrailingComments(INode node)
        {
            Trivia trivia;
            if (TryGetTrivia(node, out trivia))
            {
                if (trivia.TrailingComments != null)
                {
                    foreach (var comment in trivia.TrailingComments)
                    {
                        if (comment.IsSingleLine)
                        {
                            AppendToken(" ");
                        }

                        AppendToken(comment.Content);
                        NewLine();
                    }
                }
            }

            return this;
        }

        /// <nodoc />
        private static bool TryGetTrivia(INode node, out Trivia trivia)
        {
            trivia = null;
            var sourceFile = node.GetSourceFile();

            // There are cases (e.g. unit tests) where the parent relationship is not set, so the source file is null
            // In that case we bail out from trying to preserve new lines
            if (sourceFile == null || sourceFile.PerNodeTrivia.Count == 0)
            {
                return false;
            }

            var actualNode = node.ResolveUnionType();

            return sourceFile.PerNodeTrivia.TryGetValue(actualNode, out trivia);
        }
    }
}
