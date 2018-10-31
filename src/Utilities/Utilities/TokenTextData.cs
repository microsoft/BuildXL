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
    public readonly struct TokenTextData : IEquatable<TokenTextData>
    {
        /// <summary>
        /// An invalid token data.
        /// </summary>
        public static readonly TokenTextData Invalid = default(TokenTextData);

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
        /// <param name="line">Line number of the token</param>
        /// <param name="column">Column number of the token</param>
        /// <param name="text">Text in the token.</param>
        public TokenTextData(int line, int column, TokenText text)
        {
            Contract.Requires(text.IsValid);

            Line = line;
            Position = column;
            Text = text;
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(Line);
            writer.Write(Text);
            writer.WriteCompact(Position);
        }

        internal static TokenTextData Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            Contract.Ensures(Contract.Result<TokenTextData>().IsValid);
            var line = reader.ReadInt32Compact();
            var text = reader.ReadTokenText();
            var position = reader.ReadInt32Compact();
            return new TokenTextData(line, position, text);
        }

        /// <summary>
        /// Creates a token from the information with the given path
        /// </summary>
        public TokenData ToToken(AbsolutePath path)
        {
            return IsValid ? new TokenData(path, Line, Position, Text) : TokenData.Invalid;
        }

        /// <summary>
        /// Implicit conversion of Token to TokenTextData.
        /// </summary>
        public static implicit operator TokenTextData(Token token)
        {
            return token == null ? Invalid : new TokenTextData(token.Line, token.Position, token.Text);
        }

        /// <summary>
        /// Implicit conversion of TokenData to TokenTextData.
        /// </summary>
        public static implicit operator TokenTextData(TokenData token)
        {
            return !token.IsValid ? Invalid : new TokenTextData(token.Line, token.Position, token.Text);
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
        public TokenTextData UpdateLineInformationForPosition(TokenTextTable table, int positionInToken)
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

            return new TokenTextData(line, pos, Text);
        }

        /// <summary>
        /// Returns a string representation of the token.
        /// </summary>
        /// <param name="pathTable">The path table used when creating the AbsolutePath in the Path field.</param>
        public string ToString(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            return I($"({Line}, {Position})");
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Line, Position);
        }

        /// <inheritdoc />
        public bool Equals(TokenTextData other)
        {
            return other.Position == Position && other.Line == Line && Text.Equals(other.Text);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(TokenTextData left, TokenTextData right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(TokenTextData left, TokenTextData right)
        {
            return !left.Equals(right);
        }
    }
}
