// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Collections.Generic;

namespace BuildXL.Ide.LanguageServer.UnitTests.Helpers
{
    internal class RangeHelpers
    {
        public static Range CreateRange(int StartLine, int StartCharacter, int EndLine, int EndCharacter)
        {
            return new Range()
            {
                Start = new Position()
                {
                    Line = StartLine,
                    Character = StartCharacter
                },
                End = new Position()
                {
                    Line = EndLine,
                    Character = EndCharacter
                }
            };
        }

        public static readonly IEqualityComparer<Range> s_RangeComparer = new RangeComparer();

        private class RangeComparer : IEqualityComparer<Range>
        {
            public bool Equals(Range x, Range y)
            {
                return x.Start.Line == y.Start.Line && x.Start.Character == y.Start.Character
                    && x.End.Line == y.End.Line && x.End.Character == y.End.Character;
            }

            public int GetHashCode(Range obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
