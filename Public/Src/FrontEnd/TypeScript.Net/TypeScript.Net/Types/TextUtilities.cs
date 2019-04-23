// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of utility methods for <see cref="ITextSpan"/> and <see cref="ITextRange"/> types and some other string-related operations.
    /// </summary>
    public static class TextUtilities
    {
        /// <nodoc />
        public static ITextSpan CreateTextSpan(int start, int length)
        {
            Contract.Requires(start >= 0);

            // Contract.Requires(length >= 0);
            return new TextSpan() { Length = length <= 0 ? 1 : length, Start = start };
        }

        /// <nodoc />
        public static ITextSpan CreateTextSpanFromBounds(int start, int end)
        {
            return CreateTextSpan(start, end - start);
        }

        /// <nodoc />
        public static int TextSpanEnd(ITextSpan span)
        {
            return span.Start + span.Length;
        }

        /// <nodoc />
        public static bool TextSpanIsEmpty(ITextSpan span)
        {
            return span.Length == 0;
        }

        /// <nodoc />
        public static bool TextSpanContainsPosition(ITextSpan span, int position)
        {
            return position >= span.Start && position < TextSpanEnd(span);
        }

        /// <summary>
        /// Returns true if 'span' contains 'other'.
        /// </summary>
        public static bool TextSpanContainsTextSpan(ITextSpan span, ITextSpan other)
        {
            return other.Start >= span.Start && TextSpanEnd(other) <= TextSpanEnd(span);
        }

        /// <nodoc />
        public static bool TextSpanOverlapsWith(ITextSpan span, ITextSpan other)
        {
            var overlapStart = Math.Max(span.Start, other.Start);
            var overlapEnd = Math.Min(TextSpanEnd(span), TextSpanEnd(other));
            return overlapStart < overlapEnd;
        }

        /// <nodoc />
        [CanBeNull]
        public static ITextSpan TextSpanOverlap(ITextSpan span1, ITextSpan span2)
        {
            var overlapStart = Math.Max(span1.Start, span2.Start);
            var overlapEnd = Math.Min(TextSpanEnd(span1), TextSpanEnd(span2));
            if (overlapStart < overlapEnd)
            {
                return CreateTextSpanFromBounds(overlapStart, overlapEnd);
            }

            return null;
        }

        /// <nodoc />
        public static bool TextSpanIntersectsWithTextSpan(ITextSpan span, ITextSpan other)
        {
            return other.Start <= TextSpanEnd(span) && TextSpanEnd(other) >= span.Start;
        }

        /// <nodoc/>
        public static bool TextSpanIntersectsWith(ITextSpan span, int start, int length)
        {
            var end = start + length;
            return start <= TextSpanEnd(span) && end >= span.Start;
        }

        /// <nodoc />
        public static bool DecodedTextSpanIntersectsWith(int start1, int length1, int start2, int length2)
        {
            var end1 = start1 + length1;
            var end2 = start2 + length2;
            return start2 <= end1 && end2 >= start1;
        }

        /// <nodoc />
        public static bool TextSpanIntersectsWithPosition(ITextSpan span, int position)
        {
            return position <= TextSpanEnd(span) && position >= span.Start;
        }

        /// <nodoc />
        [CanBeNull]
        public static ITextSpan TextSpanIntersection(ITextSpan span1, ITextSpan span2)
        {
            var intersectStart = Math.Max(span1.Start, span2.Start);
            var intersectEnd = Math.Min(TextSpanEnd(span1), TextSpanEnd(span2));
            if (intersectStart <= intersectEnd)
            {
                return CreateTextSpanFromBounds(intersectStart, intersectEnd);
            }

            return null;
        }

        /// <nodoc />
        public static ITextSpan TextChangeRangeNewSpan(ITextChangeRange range)
        {
            return CreateTextSpan(range.Span.Start, range.NewLength);
        }

        /// <nodoc />
        public static bool TextChangeRangeIsUnchanged(ITextChangeRange range)
        {
            return TextSpanIsEmpty(range.Span) && range.NewLength == 0;
        }

        /// <nodoc />
        public static ITextChangeRange CreateTextChangeRange(ITextSpan span, int newLength)
        {
            Contract.Requires(newLength >= 0);

            return new TextChangeRange() { NewLength = newLength, Span = span };
        }

        /// <nodoc />
        public static readonly ITextChangeRange UnchangedTextChangeRange = CreateTextChangeRange(TextUtilities.CreateTextSpan(0, 0), 0);

        /// <summary>
        /// Called to merge all the changes that occurred across several versions of a script snapshot
        /// into a single change.  i.e., if a user keeps making successive edits to a script we will
        /// have a text change from V1 to V2, V2 to V3, ..., Vn.
        ///
        /// This  will then merge those changes into a single change range valid between V1 and
        /// Vn.
        /// </summary>
        public static ITextChangeRange CollapseTextChangeRangesAcrossMultipleVersions(ITextChangeRange[] changes)
        {
            if (changes.Length == 0)
            {
                return UnchangedTextChangeRange;
            }

            if (changes.Length == 1)
            {
                return changes[0];
            }

            // We change from talking about { { oldStart, oldLength }, newLength } to { oldStart, oldEnd, newEnd }
            // as it makes things much easier to reason about.
            var change0 = changes[0];

            var oldStartN = change0.Span.Start;
            var oldEndN = TextSpanEnd(change0.Span);
            var newEndN = oldStartN + change0.NewLength;

            for (var i = 1; i < changes.Length; i++)
            {
                var nextChange = changes[i];

                // Consider the following case:
                // i.e., two edits.  The first represents the text change range { { 10, 50 }, 30 }.  i.e., The span starting
                // at 10, with length 50 is reduced to length 30.  The second represents the text change range { { 30, 30 }, 40 }.
                // i.e., the span starting at 30 with length 30 is increased to length 40.
                //
                //      0         10        20        30        40        50        60        70        80        90        100
                //      -------------------------------------------------------------------------------------------------------
                //                |                                                 /
                //                |                                            /----
                //  T1            |                                       /----
                //                |                                  /----
                //                |                             /----
                //      -------------------------------------------------------------------------------------------------------
                //                                     |                            \
                //                                     |                               \
                //   T2                                |                                 \
                //                                     |                                   \
                //                                     |                                      \
                //      -------------------------------------------------------------------------------------------------------
                //
                // Merging these turns out to not be too difficult.  First, determining the new start of the change is trivial
                // it's just the min of the old and new starts.  i.e.,:
                //
                //      0         10        20        30        40        50        60        70        80        90        100
                //      ------------------------------------------------------------*------------------------------------------
                //                |                                                 /
                //                |                                            /----
                //  T1            |                                       /----
                //                |                                  /----
                //                |                             /----
                //      ----------------------------------------$-------------------$------------------------------------------
                //                .                    |                            \
                //                .                    |                               \
                //   T2           .                    |                                 \
                //                .                    |                                   \
                //                .                    |                                      \
                //      ----------------------------------------------------------------------*--------------------------------
                //
                // (Note the dots represent the newly inferrred start.
                // Determining the new and old end is also pretty simple.  Basically it boils down to paying attention to the
                // absolute positions at the asterixes, and the relative change between the dollar signs. Basically, we see
                // which if the two $'s precedes the other, and we move that one forward until they line up.  in this case that
                // means:
                //
                //      0         10        20        30        40        50        60        70        80        90        100
                //      --------------------------------------------------------------------------------*----------------------
                //                |                                                                     /
                //                |                                                                /----
                //  T1            |                                                           /----
                //                |                                                      /----
                //                |                                                 /----
                //      ------------------------------------------------------------$------------------------------------------
                //                .                    |                            \
                //                .                    |                               \
                //   T2           .                    |                                 \
                //                .                    |                                   \
                //                .                    |                                      \
                //      ----------------------------------------------------------------------*--------------------------------
                //
                // In other words (in this case), we're recognizing that the second edit happened after where the first edit
                // ended with a delta of 20 characters (60 - 40).  Thus, if we go back in time to where the first edit started
                // that's the same as if we started at char 80 instead of 60.
                //
                // As it so happens, the same logic applies if the second edit precedes the first edit.  In that case rahter
                // than pusing the first edit forward to match the second, we'll push the second edit forward to match the
                // first.
                //
                // In this case that means we have { oldStart: 10, oldEnd: 80, newEnd: 70 } or, in TextChangeRange
                // semantics: { { start: 10, length: 70 }, newLength: 60 }
                //
                // The math then works out as follows.
                // If we have { oldStart1, oldEnd1, newEnd1 } and { oldStart2, oldEnd2, newEnd2 } then we can compute the
                // final result like so:
                //
                // {
                //      oldStart3: Min(oldStart1, oldStart2),
                //      oldEnd3  : Max(oldEnd1, oldEnd1 + (oldEnd2 - newEnd1)),
                //      newEnd3  : Max(newEnd2, newEnd2 + (newEnd1 - oldEnd2))
                // }
                var oldStart1 = oldStartN;
                var oldEnd1 = oldEndN;
                var newEnd1 = newEndN;

                var oldStart2 = nextChange.Span.Start;
                var oldEnd2 = TextSpanEnd(nextChange.Span);
                var newEnd2 = oldStart2 + nextChange.NewLength;

                oldStartN = Math.Min(oldStart1, oldStart2);
                oldEndN = Math.Max(oldEnd1, oldEnd1 + (oldEnd2 - newEnd1));
                newEndN = Math.Max(newEnd2, newEnd2 + (newEnd1 - oldEnd2));
            }

            return CreateTextChangeRange(CreateTextSpanFromBounds(oldStartN, oldEndN), /*newLength:*/ newEndN - oldStartN);
        }

        /// <summary>
        /// Based heavily on the abstract 'Quote'/'QuoteJSONString' operation from ECMA-262 (24.3.2.2),
        /// but augmented for a few select characters (e.G. lineSeparator, paragraphSeparator, nextLine)
        /// Note that this doesn't actually wrap the input in double quotes.
        /// </summary>
        public static string EscapeString(string s)
        {
            var result = s_escapedCharsRegExp.Match(s).Success 
                ? s_escapedCharsRegExp.Replace(s, m => GetReplacement(m.Value)) 
                : s;
            return result;
        }

        /// <summary>
        /// This consists of the first 19 unprintable ASCII characters, canonical escapes, lineSeparator,
        /// paragraphSeparator, and nextLine. The latter three are just desirable to suppress new lines in
        /// the language service. These characters should be escaped when printing, and if any characters are added,
        /// the map below must be updated. Note that this regexp *does not* include the 'delete' character.
        /// There is no reason for this other than that JSON.Stringify does not handle it either.
        /// </summary>
        private static readonly Regex s_escapedCharsRegExp = new Regex("[\"\u0000-\u001f\t\v\f\b\r\n\u2028\u2029\u0085]");

        // TODO: check that this map is correct!
        private static readonly Map<string> s_escapedCharsMap = new Map<string>
        {
            ["\0"] = "\\0",
            ["\t"] = "\\t",
            ["\v"] = "\\v",
            ["\f"] = "\\f",
            ["\b"] = "\\b",
            ["\r"] = "\\r",
            ["\n"] = "\\n",
            ["\\"] = "\\\\",
            ["\""] = "\\\"",
            ["\u2028"] = "\\u2028", // lineSeparator
            ["\u2029"] = "\\u2029", // paragraphSeparator
            ["\u0085"] = "\\u0085", // nextLine
        };

        private static string GetReplacement(string c)
        {
            if (!s_escapedCharsMap.TryGetValue(c, out var result))
            {
                result = Get16BitUnicodeEscapeSequence((int)c[0]);
            }

            return result;
        }

        private static string Get16BitUnicodeEscapeSequence(int charCode)
        {
            var hexCharCode = charCode.ToString("X4").ToUpperInvariant();
            return "\\u" + hexCharCode;
        }
    }
}
