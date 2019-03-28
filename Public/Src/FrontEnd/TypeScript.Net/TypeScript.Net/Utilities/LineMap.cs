// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using TypeScript.Net.Scanning;

namespace TypeScript.Net.Utilities
{
    /// <summary>
    /// Information about line endings for a file.
    /// </summary>
    /// <remarks>
    /// Line breaks are computed lazily for performance reasons.
    /// </remarks>
    public sealed class LineMap
    {
        /// <nodoc/>
        public int[] Map { get; }

        /// <nodoc/>
        public bool BackslashesAllowedInPathInterpolation { get; }

        /// <nodoc/>
        public LineMap(int[] map, bool backslashesAllowedInPathInterpolation)
        {
            Map = map;
            BackslashesAllowedInPathInterpolation = backslashesAllowedInPathInterpolation;
        }

        /// <nodoc/>
        public static LineMap Read(BuildXLReader reader)
        {
            var mapSize = reader.ReadInt32Compact();
            var map = new int[mapSize];
            for (int i = 0; i < mapSize; i++)
            {
                map[i] = reader.ReadInt32Compact();
            }

            var backslashesAllowedInPathInterpolation = reader.ReadBoolean();

            return new LineMap(map, backslashesAllowedInPathInterpolation);
        }

        /// <nodoc/>
        public void Write(BuildXLWriter writer)
        {
            writer.WriteCompact(Map.Length);
            foreach (var n in Map)
            {
                writer.WriteCompact(n);
            }

            writer.Write(BackslashesAllowedInPathInterpolation);
        }

        /// <summary>
        /// Computes a map with line breaks.
        /// </summary>
        /// <remarks>
        /// This is heavyweight computation and the result of it definitely should be cached to avoid perf impact.
        /// </remarks>
        public static int[] ComputeLineStarts(TextSource text)
        {
            List<int> result = new List<int>();
            var pos = 0;
            var lineStart = 0;
            while (pos < text.Length)
            {
                var ch = text.CharCodeAt(pos++);
                switch (ch)
                {
                    case CharacterCodes.CarriageReturn:
                        if (text.CharCodeAt(pos) == CharacterCodes.LineFeed)
                        {
                            pos++;
                        }

                        goto case CharacterCodes.LineFeed;
                    case CharacterCodes.LineFeed:
                        result.Add(lineStart);
                        lineStart = pos;
                        break;
                    default:
                        if (ch > CharacterCodes.MaxAsciiCharacter && Scanner.IsLineBreak(ch))
                        {
                            result.Add(lineStart);
                            lineStart = pos;
                        }

                        break;
                }
            }

            result.Add(lineStart);
            return result.ToArray();
        }
    }
}
