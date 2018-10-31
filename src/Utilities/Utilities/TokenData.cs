// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// The token class tracks the location information during parsing.
    /// </summary>
    public readonly struct TokenData : IEquatable<TokenData>
    {
        /// <summary>
        /// An invalid token data.
        /// </summary>
        public static readonly TokenData Invalid = default(TokenData);

        /// <summary>
        /// Determines whether token data is valid or not.
        /// </summary>
        [Pure]
        public bool IsValid => Text.IsValid;

        /// <summary>
        /// Line number of the token
        /// </summary>
        public readonly int Line;

        /// <summary>
        /// Path of the token.
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Column number of the token
        /// </summary>
        public readonly int Position;

        /// <summary>
        /// Text in the token.
        /// </summary>
        public readonly TokenText Text;

        /// <summary>
        /// Constructs a token.
        /// </summary>
        /// <param name="path">Path of the token</param>
        /// <param name="line">Line number of the token</param>
        /// <param name="column">Column number of the token</param>
        /// <param name="text">Text in the token.</param>
        public TokenData(AbsolutePath path, int line, int column, TokenText text)
        {
            Contract.Requires(text.IsValid);

            Path = path;
            Line = line;
            Position = column;
            Text = text;
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(Line);
            writer.Write(Path);
            writer.Write(Text);
            writer.WriteCompact(Position);
        }

        internal static TokenData Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            Contract.Ensures(Contract.Result<TokenData>().IsValid);
            var line = reader.ReadInt32Compact();
            var path = reader.ReadAbsolutePath();
            var text = reader.ReadTokenText();
            var position = reader.ReadInt32Compact();
            return new TokenData(path, line, position, text);
        }

        /// <summary>
        /// Converts this TokenData to a Token.
        /// </summary>
        public Token ToToken()
        {
            return !IsValid ? null : new Token(Path, Line, Position, Text);
        }

        /// <summary>
        /// Implicit conversion of Token to TokenData.
        /// </summary>
        public static implicit operator TokenData(Token token)
        {
            return token == null ? Invalid : new TokenData(token.Path, token.Line, token.Position, token.Text);
        }

        /// <summary>
        /// Expands the token text
        /// </summary>
        /// <param name="table">the table used when creating the token text</param>
        /// <returns>the token with the expanded</returns>
        public ExpandedTokenData<StringSegment> Expand(TokenTextTable table)
        {
            Contract.Requires(table != null);
            return !IsValid
                ? default(ExpandedTokenData<StringSegment>)
                : new ExpandedTokenData<StringSegment>(Path, Line, Position, Text.ToString(table));
        }

        /// <summary>
        /// Helper method that will try to map the position in the token string to the correct original line and position in the
        /// file.
        /// </summary>
        /// <remarks>
        /// It will start with the token as line and position info. It will scan through the actual text to find the lines and
        /// recomputes the location.
        /// </remarks>
        /// <param name="table">The table used when creating the token.</param>
        /// <param name="positionInToken">The actual character position int the text of the token.</param>
        /// <returns>An updated token with recomputed line information</returns>
        public TokenData UpdateLineInformationForPosition(TokenTextTable table, int positionInToken)
        {
            Contract.Requires(table != null);
            Contract.Requires(positionInToken >= 0);
            Contract.Requires(positionInToken <= Text.GetLength(table));

            int line = Line;
            int pos = Position;

            var text = Text.ToString(table);
            bool seenCr = false;
            int lastPos = Math.Min(positionInToken, Text.GetLength(table) - 1);
            for (int i = 0; i < lastPos; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    if (!seenCr)
                    {
                        // in case of \n (not \r\n) start a new line.
                        line++;
                        pos = 0;
                    }
                }
                else if (c == '\r')
                {
                    // in case of \r start a new line.
                    seenCr = true;
                    line++;
                    pos = 0;
                }
                else
                {
                    seenCr = false;
                    pos++;
                }
            }

            return new TokenData(Path, line, pos, Text);
        }

        /// <summary>
        /// Returns a string representation of the token.
        /// </summary>
        /// <param name="pathTable">The path table used when creating the AbsolutePath in the Path field.</param>
        public string ToString(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            return I($"{Path.ToString(pathTable)}({Line}, {Position})");
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Line, Position);
        }

        /// <inheritdoc />
        public bool Equals(TokenData other)
        {
            return other.Position == Position && other.Line == Line && Text.Equals(other.Text) && Path.Equals(other.Path);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(TokenData left, TokenData right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(TokenData left, TokenData right)
        {
            return !left.Equals(right);
        }
    }
}
