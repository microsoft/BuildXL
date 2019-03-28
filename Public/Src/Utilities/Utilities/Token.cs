// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using System.Xml;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// The token class tracks the location information during parsing.
    /// </summary>
    public sealed class Token
    {
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
        public Token(AbsolutePath path, int line, int column, TokenText text)
        {
            Contract.Requires(text.IsValid);

            Path = path;
            Line = line;
            Position = column;
            Text = text;
        }

        /// <summary>
        /// Helper to create instances of tokens.
        /// </summary>
        public static Token Create(BuildXLContext context, AbsolutePath path, string text = "", int line = 0, int column = 0)
        {
            Contract.Requires(context != null);
            Contract.Requires(path.IsValid);
            Contract.Requires(text != null);

            return new Token(path, line, column, TokenText.Create(context.TokenTextTable, text));
        }

        /// <summary>
        /// Helper to create instances of tokens.
        /// </summary>
        public static Token Create(BuildXLContext context, Type type, string text = "", int line = 0, int column = 0)
        {
            Contract.Requires(context != null);
            Contract.Requires(type != null);
            Contract.Requires(text != null);
            return new Token(
                AbsolutePath.Create(context.PathTable, AssemblyHelper.GetAssemblyLocation(type.GetTypeInfo().Assembly)),
                line,
                column,
                TokenText.Create(context.TokenTextTable, text));
        }

        /// <summary>
        /// Helper to create instances of tokens from xml reader.
        /// </summary>
        public static Token Create(BuildXLContext context, XmlReader reader, AbsolutePath path)
        {
            Contract.Requires(context != null);
            Contract.Requires(reader != null);

            var xmlInfo = (IXmlLineInfo)reader;
            return new Token(path, xmlInfo.LineNumber, xmlInfo.LinePosition, TokenText.Create(context.TokenTextTable, reader.Value));
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(Line);
            writer.Write(Path);
            writer.Write(Text);
            writer.WriteCompact(Position);
        }

        internal static Token Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            Contract.Ensures(Contract.Result<Token>() != null);
            var line = reader.ReadInt32Compact();
            var path = reader.ReadAbsolutePath();
            var text = reader.ReadTokenText();
            var position = reader.ReadInt32Compact();
            return new Token(path, line, position, text);
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
        public Token UpdateLineInformationForPosition(TokenTextTable table, int positionInToken)
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

            return new Token(Path, line, pos, Text);
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
    }
}
