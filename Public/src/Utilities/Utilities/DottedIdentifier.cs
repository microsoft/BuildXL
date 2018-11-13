// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Dotted identifier
    /// </summary>
    public sealed class DottedIdentifier : IEquatable<DottedIdentifier>, ISymbol
    {
        private DottedIdentifier m_tail;

        /// <summary>
        /// Constructs a new DottedIdentifier
        /// </summary>
        /// <param name="table">Identifier table that will store this identifier.</param>
        /// <param name="head">The head of the identifier</param>
        public DottedIdentifier(SymbolTable table, string head)
        {
            Contract.Requires(table != null);
            Contract.Requires(!string.IsNullOrEmpty(head));

            StringSegment seg = head;

            Contract.Assert(seg.Length > 0);

            Head = SymbolAtom.Create(table.StringTable, seg);
        }

        /// <summary>
        /// Constructs a new DottedIdentifier
        /// </summary>
        /// <param name="head">The head of the identifier</param>
        /// <param name="tail">The tail of the identifier</param>
        public DottedIdentifier(SymbolAtom head, DottedIdentifier tail = null)
        {
            Contract.Requires(head.IsValid);

            Head = head;
            m_tail = tail;
        }

        /// <summary>
        /// Creates a dotted identifier for the given character span
        /// </summary>
        /// <param name="table">Identifier table that will store this identifier.</param>
        /// <param name="head">The head of the identifier</param>
        /// <param name="tail">The tail of the identifier</param>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static DottedIdentifier Create<TChars>(SymbolTable table, TChars head, DottedIdentifier tail = null)
            where TChars : struct, ICharSpan<TChars>
        {
            Contract.Requires(table != null);
            Contract.Requires(head.Length != 0);

            return new DottedIdentifier(SymbolAtom.Create(table.StringTable, head), tail);
        }

        /// <summary>
        /// Constructs a new DottedIdentifier
        /// </summary>
        /// <param name="table">Identifier table that will store this identifier.</param>
        /// <param name="prefix">The prefix of the identifier</param>
        /// <param name="tail">The tail of the identifier</param>
        public DottedIdentifier(SymbolTable table, IEnumerable<string> prefix, DottedIdentifier tail)
        {
            Contract.Requires(table != null);
            Contract.Requires(prefix != null);

            // we'll need this argument in the future so prevent warnings until then.
            Analysis.IgnoreArgument(table);

            DottedIdentifier current = null;
            foreach (var s in prefix)
            {
                if (current == null)
                {
                    // head node
                    Head = SymbolAtom.Create(table.StringTable, (StringSegment)s);
                    current = this;
                }
                else
                {
                    // extend the tail
                    current.m_tail = new DottedIdentifier(table, s);
                    current = current.m_tail;
                }
            }

            Contract.Assume(current != null, "Must have at least one element in the prefix enumeration");
            current.m_tail = tail;
        }

        /// <summary>
        /// Constructs a new DottedIdentifier
        /// </summary>
        /// <param name="table">Identifier table that will store this identifier.</param>
        /// <param name="prefix">The prefix of the identifier</param>
        /// <param name="tail">The tail of the identifier</param>
        public DottedIdentifier(SymbolTable table, IEnumerable<SymbolAtom> prefix, DottedIdentifier tail)
        {
            Contract.Requires(table != null);
            Contract.Requires(prefix != null);

            // we'll need this argument in the future so prevent warnings until then.
            Analysis.IgnoreArgument(table);

            DottedIdentifier current = null;
            foreach (var s in prefix)
            {
                if (current == null)
                {
                    // head node
                    Head = s;
                    current = this;
                }
                else
                {
                    // extend the tail
                    current.m_tail = new DottedIdentifier(s);
                    current = current.m_tail;
                }
            }

            Contract.Assume(current != null, "Must have at least one element in the prefix enumeration");
            current.m_tail = tail;
        }

        /// <summary>
        /// Constructs a new DottedIdentifier
        /// </summary>
        /// <param name="table">Identifier table that will store this identifier.</param>
        /// <param name="components">The components of the identifier</param>
        public DottedIdentifier(SymbolTable table, IEnumerable<string> components)
        {
            Contract.Requires(table != null);
            Contract.Requires(components != null);

            DottedIdentifier current = null;
            foreach (var s in components)
            {
                if (current == null)
                {
                    // head node
                    Head = SymbolAtom.Create(table.StringTable, (StringSegment)s);
                    current = this;
                }
                else
                {
                    // extend the tail
                    current.m_tail = new DottedIdentifier(table, s);
                    current = current.m_tail;
                }
            }

            Contract.Assume(current != null, "Must have at least one element in the components enumeration");
        }

        /// <summary>
        /// Constructs a new DottedIdentifier
        /// </summary>
        /// <param name="table">Identifier table that will store this identifier.</param>
        /// <param name="components">The components of the identifier</param>
        public DottedIdentifier(SymbolTable table, IEnumerable<SymbolAtom> components)
        {
            Contract.Requires(table != null);
            Contract.Requires(components != null);

            // we'll need this argument in the future so prevent warnings until then.
            Analysis.IgnoreArgument(table);

            DottedIdentifier current = null;
            foreach (var s in components)
            {
                if (current == null)
                {
                    // head node
                    Head = s;
                    current = this;
                }
                else
                {
                    // extend the tail
                    current.m_tail = new DottedIdentifier(s);
                    current = current.m_tail;
                }
            }

            Contract.Assume(current != null, "Must have at least one element in the components enumeration");
        }

        /// <summary>
        /// The result from trying to parse a <see cref="DottedIdentifier"/> or subcomponent
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815")]
        public struct ParseResult
        {
            /// <summary>
            /// Line for logging
            /// </summary>
            public int Line;

            /// <summary>
            /// Position for logging
            /// </summary>
            public int Position;

            /// <summary>
            /// The status
            /// </summary>
            public ParseStatus Status;

            /// <summary>
            /// Path to originating file, used for logging
            /// </summary>
            public string Path;

            /// <summary>
            /// Character or text relating to the error
            /// </summary>
            public string Text;
        }

        /// <summary>
        /// Status for parsing
        /// </summary>
        public enum ParseStatus : byte
        {
            /// <nodoc/>
            Success,

            /// <nodoc/>
            UnexpectedEmptyIdentifier,

            /// <nodoc/>
            UnexpectedCharacterAtStartOfIdentifier,

            /// <nodoc/>
            InvalidDottedIdentifierCannotBeEmpty,

            /// <nodoc/>
            InvalidDottedIdentifierUnexpectedDot,

            /// <nodoc/>
            InvalidDottedIdentifierUnexpectedCharacter,
        }

        /// <summary>
        /// Parses a dotted identifier. All error logging is the responsibility of the caller
        /// </summary>
        /// <param name="token">The token to parse</param>
        /// <param name="context">Context with tables.</param>
        /// <param name="identifier">The parsed identifier if successful, null if not.</param>
        public static ParseResult TryParse(
            TokenData token,
            BuildXLContext context,
            out DottedIdentifier identifier)
        {
            return TryParse(
                token.Expand(context.TokenTextTable),
                context,
                out identifier);
        }

        /// <summary>
        /// Parses a dotted identifier. All error logging is the responsibility of the caller
        /// </summary>
        /// <param name="token">The token to parse</param>
        /// <param name="context">Context with tables.</param>
        /// <param name="identifier">The parsed identifier if successful, null if not.</param>
        public static ParseResult TryParse<TChars>(
           ExpandedTokenData<TChars> token,
           PipExecutionContext context,
           out DottedIdentifier identifier)
            where TChars : struct, ICharSpan<TChars>
        {
            Contract.Requires(token.IsValid);
            Contract.Requires(context != null);

            PathTable pathTableForError = context.PathTable;
            SymbolTable symbolTable = context.SymbolTable;

            int position = 0;

            var text = token.Text;
            int textLength = text.Length;
            identifier = null;

            if (text.Length == 0)
            {
                return new ParseResult()
                {
                    Status = ParseStatus.InvalidDottedIdentifierCannotBeEmpty,
                    Path = token.Path.ToString(pathTableForError),
                    Line = token.Line,
                    Position = token.Position,
                };
            }

            TChars id;
            var parseIdenfierResult = TryParseIdentifier(token, context, ref position, out id);
            if (parseIdenfierResult.Status != ParseStatus.Success)
            {
                return parseIdenfierResult;
            }

            identifier = DottedIdentifier.Create(symbolTable, id);
            DottedIdentifier currentIdentifier = identifier;

            while (position < textLength)
            {
                if (text[position] == '.')
                {
                    if (position == textLength - 1)
                    {
                        // Last char is a dot.
                        var updateToken = token.UpdateLineInformationForPosition(position);
                        identifier = null;
                        return new ParseResult()
                        {
                            Status = ParseStatus.InvalidDottedIdentifierUnexpectedDot,
                            Path = updateToken.Path.ToString(pathTableForError),
                            Line = updateToken.Line,
                            Position = updateToken.Position,
                            Text = currentIdentifier != null ? currentIdentifier.Head.ToString(symbolTable.StringTable) : string.Empty,
                        };
                    }

                    position++;

                    TChars currentId;
                    parseIdenfierResult = TryParseIdentifier(token, context, ref position, out currentId);
                    if (parseIdenfierResult.Status != ParseStatus.Success)
                    {
                        identifier = null;
                        return parseIdenfierResult;
                    }

                    var tail = DottedIdentifier.Create(symbolTable, currentId);
                    currentIdentifier.m_tail = tail;
                    currentIdentifier = tail;
                }
                else
                {
                    break;
                }
            }

            if (position < textLength)
            {
                var updateToken = token.UpdateLineInformationForPosition(position);
                identifier = null;

                return new ParseResult()
                {
                    Status = ParseStatus.InvalidDottedIdentifierUnexpectedCharacter,
                    Path = updateToken.Path.ToString(pathTableForError),
                    Line = updateToken.Line,
                    Position = updateToken.Position,
                    Text = text[position].ToString(),
                };
            }

            return new ParseResult() { Status = ParseStatus.Success };
        }

        /// <summary>
        /// Helper to parse an identifier. All error logging is the responsibility of the caller
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public static ParseResult TryParseIdentifier(
            TokenData token,
            BuildXLContext context,
            ref int position,
            out string identifier)
        {
            StringSegment identifierSegment;
            var parseResult = TryParseIdentifier(
                token.Expand(context.TokenTextTable),
                context,
                ref position,
                out identifierSegment);
            identifier = parseResult.Status == ParseStatus.Success ? identifierSegment.ToString() : null;

            return parseResult;
        }

        /// <summary>
        /// Helper to parse an identifier
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private static ParseResult TryParseIdentifier<TChars>(
            ExpandedTokenData<TChars> token,
            PipExecutionContext context,
            ref int position,
            out TChars identifier,
            bool logErrors = true)
            where TChars : struct, ICharSpan<TChars>
        {
            Contract.Requires(token.IsValid);
            Contract.Requires(context != null);

            var pathTable = context.PathTable;

            var text = token.Text;

            if (text.Length == 0 || position == text.Length)
            {
                var updateToken = token.UpdateLineInformationForPosition(position);
                identifier = default(TChars);
                return new ParseResult()
                {
                    Status = ParseStatus.UnexpectedEmptyIdentifier,
                    Path = updateToken.Path.ToString(pathTable),
                    Line = updateToken.Line,
                    Position = updateToken.Position,
                };
            }

            int firstPosition = position;

            char firstChar = text[position];
            if (!SymbolCharacters.IsValidStartChar(firstChar))
            {
                var updateToken = token.UpdateLineInformationForPosition(position);
                identifier = default(TChars);
                return new ParseResult()
                {
                    Status = ParseStatus.UnexpectedCharacterAtStartOfIdentifier,
                    Path = updateToken.Path.ToString(pathTable),
                    Line = updateToken.Line,
                    Position = updateToken.Position,
                    Text = firstChar.ToString(),
                };
            }

            position++;

            for (; position < text.Length; position++)
            {
                char ch = text[position];
                if (!SymbolCharacters.IsValidChar(ch))
                {
                    break;
                }
            }

            identifier = text.Subsegment(firstPosition, position - firstPosition);
            return new ParseResult { Status = ParseStatus.Success };
        }

        /// <summary>
        /// Displays a string representation of the DottedIdentifier
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public string ToString(HierarchicalNameTable symbolTable, char separator = '.')
        {
            Contract.Requires(symbolTable != null);

            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                symbolTable.StringTable.CopyString(Head.StringId, wrap.Instance);
                DottedIdentifier tail = m_tail;
                while (tail != null)
                {
                    wrap.Instance.Append(separator);
                    symbolTable.StringTable.CopyString(tail.Head.StringId, wrap.Instance);
                    tail = tail.m_tail;
                }

                return wrap.Instance.ToString();
            }
        }

#pragma warning disable 809

        /// <summary>
        /// Not available for DottedIdentifier, throws an exception
        /// </summary>
        [Obsolete("Not suitable for DottedIdentifier. Please use ToString overload with SymbolTable")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

#pragma warning restore 809

        /// <summary>
        /// Indicates if this dotted identifier and the one given represent the same underlying value.
        /// </summary>
        public bool Equals(DottedIdentifier other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Head == other.Head)
            {
                if (m_tail != null)
                {
                    return m_tail.Equals(other.m_tail);
                }

                return other.m_tail == null;
            }

            return false;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Head.GetHashCode(), m_tail?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            var other = obj as DottedIdentifier;
            return other != null && Equals(other);
        }

        /// <summary>
        /// The current identifier of the Dotted Identifier.
        /// </summary>
        /// <remarks>
        /// This string is case sensitive.
        /// This is 'A' if the identifier is 'A.B.C' or 'A'
        /// </remarks>
        public SymbolAtom Head { get; }

        /// <summary>
        /// The tail of the dotted identifier.
        /// </summary>
        /// <remarks>
        /// This points to 'B.C' if the identifier is 'A.B.C'
        /// This points to null if the identifier is just 'C'
        /// </remarks>
        public DottedIdentifier GetTail(SymbolTable table)
        {
            Contract.Requires(table != null);

            Analysis.IgnoreArgument(table);

            return m_tail;
        }

        internal static DottedIdentifier Deserialize(BuildXLReader reader, SymbolTable table)
        {
            Contract.Requires(reader != null);
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<DottedIdentifier>() != null);

            // we'll need this argument in the future so prevent warnings until then.
            Analysis.IgnoreArgument(table);

            DottedIdentifier current = null;
            DottedIdentifier head = null;

            while (true)
            {
                var s = reader.ReadSymbolAtom();
                if (!s.IsValid)
                {
                    break;
                }

                if (current == null)
                {
                    // head node
                    head = new DottedIdentifier(s);
                    current = head;
                }
                else
                {
                    // extend the tail...
                    current.m_tail = new DottedIdentifier(s);
                    current = current.m_tail;
                }
            }

            Contract.Assume(head != null);
            return head;
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            for (var i = this; i != null; i = i.m_tail)
            {
                writer.Write(i.Head);
            }

            writer.Write(SymbolAtom.Invalid);
        }

        #region ISymbol Members

        /// <summary>
        /// Attempts to get the full symbol of the combined symbols. If this value represents a full symbol
        /// it is returned unmodified.
        /// </summary>
        public bool TryGetFullSymbol(SymbolTable symbolTable, FullSymbol root, out FullSymbol fullSymbol)
        {
            var identifier = this;
            fullSymbol = root;

            while (identifier != null)
            {
                if (!fullSymbol.TryGet(symbolTable, identifier.Head, out fullSymbol))
                {
                    return false;
                }

                identifier = identifier.m_tail;
            }

            return true;
        }

        /// <summary>
        /// Converts the symbol to its string representation
        /// </summary>
        public string ToString(SymbolTable symbolTable)
        {
            return ToString((HierarchicalNameTable)symbolTable);
        }

        #endregion
    }
}
