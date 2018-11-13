// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// The token class tracks the location information during parsing.
    /// </summary>
    public readonly struct ExpandedTokenData<TChars> : IEquatable<ExpandedTokenData<TChars>>
        where TChars : struct, ICharSpan<TChars>
    {
        /// <summary>
        /// An invalid token data.
        /// </summary>
        public static readonly ExpandedTokenData<TChars> Invalid = default(ExpandedTokenData<TChars>);

        /// <summary>
        /// Determines whether token data is valid or not.
        /// </summary>
        [Pure]
        public bool IsValid => !Text.Equals(default(TChars));

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
        public readonly TChars Text;

        /// <summary>
        /// Constructs a token.
        /// </summary>
        /// <param name="path">Path of the token</param>
        /// <param name="line">Line number of the token</param>
        /// <param name="column">Column number of the token</param>
        /// <param name="text">Text in the token.</param>
        public ExpandedTokenData(AbsolutePath path, int line, int column, TChars text)
        {
            Contract.Requires(!text.Equals(default(TChars)), "Token text must be valid.");

            Path = path;
            Line = line;
            Position = column;
            Text = text;
        }

        /// <summary>
        /// Helper method that will try to map the position in the token string to the correct original line and position in the
        /// file.
        /// </summary>
        /// <remarks>
        /// It will start with the token as line and position info. It will scan through the actual text to find the lines and
        /// recomputes the location.
        /// </remarks>
        /// <param name="positionInToken">The actual character position int the text of the token.</param>
        /// <returns>The recomputed line information</returns>
        public LineInfo GetLineInformationForPosition(int positionInToken)
        {
            Contract.Requires(positionInToken >= 0);
            Contract.Requires(positionInToken <= Text.Length);

            int line = Line;
            int pos = Position;

            bool seenCr = false;
            int lastPos = Math.Min(positionInToken, Text.Length - 1);
            for (int i = 0; i < lastPos; i++)
            {
                char c = Text[i];
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

            return new LineInfo(line, pos);
        }

        /// <summary>
        /// Helper method that will try to map the position in the token string to the correct original line and position in the
        /// file.
        /// </summary>
        /// <remarks>
        /// It will start with the token as line and position info. It will scan through the actual text to find the lines and
        /// recomputes the location.
        /// </remarks>
        /// <param name="positionInToken">The actual character position int the text of the token.</param>
        /// <returns>An updated token with recomputed line information</returns>
        public ExpandedTokenData<TChars> UpdateLineInformationForPosition(int positionInToken)
        {
            var updatedLineInfo = GetLineInformationForPosition(positionInToken);
            return new ExpandedTokenData<TChars>(Path, updatedLineInfo.Line, updatedLineInfo.Position, Text);
        }

        /// <summary>
        /// Returns a string representation of the token.
        /// </summary>
        /// <param name="pathTable">The path table used when creating the AbsolutePath in the Path field.</param>
        public string ToString(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            return string.Format(CultureInfo.InvariantCulture, "{0}({1}, {2})", Path.ToString(pathTable), Line, Position);
        }

        /// <summary>
        /// Implicit conversion of ExpandedTokenData to LineInfo.
        /// </summary>
        public static implicit operator LineInfo(ExpandedTokenData<TChars> token)
        {
            return token.ToLineInfo();
        }

        /// <summary>
        /// Implicit conversion of TokenData to LocationData.
        /// </summary>
        public static implicit operator LocationData(ExpandedTokenData<TChars> token)
        {
            return token.ToLocationData();
        }

        /// <summary>
        /// Converts this ExpandedTokenData to a LineInfo.
        /// </summary>
        public LineInfo ToLineInfo()
        {
            return new LineInfo(Line, Position);
        }

        /// <summary>
        /// Converts this ExpandedTokenData to a LocationData.
        /// </summary>
        public LocationData ToLocationData()
        {
            return !Path.IsValid ? LocationData.Invalid : new LocationData(Path, Line, Position);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Line, Position);
        }

        /// <inheritdoc />
        public bool Equals(ExpandedTokenData<TChars> other)
        {
            return other.Position == Position && other.Line == Line && Text.Equals(other.Text) && Path.Equals(other.Path);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(ExpandedTokenData<TChars> left, ExpandedTokenData<TChars> right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ExpandedTokenData<TChars> left, ExpandedTokenData<TChars> right)
        {
            return !left.Equals(right);
        }
    }
}
