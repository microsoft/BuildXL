// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Utilities
{
#if NET5_0_OR_GREATER
    // The tests adopted from the following PR with ReadOnlySpan<T>.Split support:
    // https://github.com/dotnet/runtime/commit/78ed8e8ab18ae6a944e04aaa1c66928acef7da18#diff-27da3246e86c068a251dfa98f5c889b71a6d58d6022040c36c99442fa881d529
    public class MemoryExtensionsTests
    {
        [Fact]
        public static void SplitNoMatchSingleResult()
        {
            ReadOnlySpan<char> value = "a b";

            string expected = value.ToString();
            SpanSplitEnumerator<char> enumerator = value.Split(',');
            Assert.True(enumerator.MoveNext());
            Assert.Equal(expected, enumerator.GetCurrent().ToString());

            enumerator = value.Split(',');
            Assert.True(enumerator.TryGetNextItem(out var current));
            Assert.Equal(expected, current.ToString());

            Assert.False(enumerator.MoveNext());
            Assert.False(enumerator.TryGetNextItem(out current));
            Assert.Equal(1, enumerator.MatchCount);
        }

        [Fact]
        public static void DefaultSpanSplitEnumeratorBehavior()
        {
            SpanSplitEnumerator<string> charSpanEnumerator = new SpanSplitEnumerator<string>();
            Assert.Equal((0, 0), charSpanEnumerator.Current);
            Assert.False(charSpanEnumerator.MoveNext());

            // Implicit DoesNotThrow assertion
            charSpanEnumerator.GetEnumerator();

            SpanSplitEnumerator<string> stringSpanEnumerator = new SpanSplitEnumerator<string>();
            Assert.Equal((0, 0), stringSpanEnumerator.Current);
            Assert.False(stringSpanEnumerator.MoveNext());
            stringSpanEnumerator.GetEnumerator();
        }

        [Fact]
        public static void ValidateArguments_OverloadWithoutSeparator()
        {
            ReadOnlySpan<char> buffer = default;

            SpanSplitEnumerator<char> enumerator = buffer.Split();
            Assert.True(enumerator.MoveNext());
            Assert.Equal((0, 0), enumerator.Current);
            Assert.False(enumerator.MoveNext());

            buffer = "";
            enumerator = buffer.Split();
            Assert.True(enumerator.MoveNext());
            Assert.Equal((0, 0), enumerator.Current);
            Assert.False(enumerator.MoveNext());

            buffer = " ";
            enumerator = buffer.Split();
            Assert.True(enumerator.MoveNext());
            Assert.Equal((0, 0), enumerator.Current);
            Assert.True(enumerator.MoveNext());
            Assert.Equal((1, 1), enumerator.Current);
            Assert.False(enumerator.MoveNext());
        }

        [Fact]
        public static void ValidateArguments_OverloadWithROSSeparator()
        {
            // Default buffer
            ReadOnlySpan<char> buffer = default;

            SpanSplitEnumerator<char> enumerator = buffer.Split(default(char));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split(' ');
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            // Empty buffer
            buffer = "";

            enumerator = buffer.Split(default(char));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split(' ');
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            // Single whitespace buffer
            buffer = " ";

            enumerator = buffer.Split(default(char));
            Assert.True(enumerator.MoveNext());
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split(' ');
            Assert.Equal((0, 0), enumerator.Current);
            Assert.True(enumerator.MoveNext());
            Assert.Equal((0, 0), enumerator.Current);
            Assert.True(enumerator.MoveNext());
            Assert.Equal((1, 1), enumerator.Current);
            Assert.False(enumerator.MoveNext());
        }

        [Fact]
        public static void ValidateArguments_OverloadWithStringSeparator()
        {
            // Default buffer
            ReadOnlySpan<char> buffer = default;

            SpanSplitEnumerator<char> enumerator = buffer.Split(null); // null is treated as empty string
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split("");
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split(" ");
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            // Empty buffer
            buffer = "";

            enumerator = buffer.Split(null);
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split("");
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split(" ");
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.False(enumerator.MoveNext());

            // Single whitespace buffer
            buffer = " ";

            enumerator = buffer.Split(null); // null is treated as empty string
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (1, 1));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split("");
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (1, 1));
            Assert.False(enumerator.MoveNext());

            enumerator = buffer.Split(" ");
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (0, 0));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(enumerator.Current, (1, 1));
            Assert.False(enumerator.MoveNext());
        }

        [Theory]
        [InlineData("", ',', new[] { "" })]
        [InlineData(" ", ' ', new[] { "", "" })]
        [InlineData(",", ',', new[] { "", "" })]
        [InlineData("     ", ' ', new[] { "", "", "", "", "", "" })]
        [InlineData(",,", ',', new[] { "", "", "" })]
        [InlineData("ab", ',', new[] { "ab" })]
        [InlineData("a,b", ',', new[] { "a", "b" })]
        [InlineData("a,", ',', new[] { "a", "" })]
        [InlineData(",b", ',', new[] { "", "b" })]
        [InlineData(",a,b", ',', new[] { "", "a", "b" })]
        [InlineData("a,b,", ',', new[] { "a", "b", "" })]
        [InlineData("a,b,c", ',', new[] { "a", "b", "c" })]
        [InlineData("a,,c", ',', new[] { "a", "", "c" })]
        [InlineData(",a,b,c", ',', new[] { "", "a", "b", "c" })]
        [InlineData("a,b,c,", ',', new[] { "a", "b", "c", "" })]
        [InlineData(",a,b,c,", ',', new[] { "", "a", "b", "c", "" })]
        [InlineData("first,second", ',', new[] { "first", "second" })]
        [InlineData("first,", ',', new[] { "first", "" })]
        [InlineData(",second", ',', new[] { "", "second" })]
        [InlineData(",first,second", ',', new[] { "", "first", "second" })]
        [InlineData("first,second,", ',', new[] { "first", "second", "" })]
        [InlineData("first,second,third", ',', new[] { "first", "second", "third" })]
        [InlineData("first,,third", ',', new[] { "first", "", "third" })]
        [InlineData(",first,second,third", ',', new[] { "", "first", "second", "third" })]
        [InlineData("first,second,third,", ',', new[] { "first", "second", "third", "" })]
        [InlineData(",first,second,third,", ',', new[] { "", "first", "second", "third", "" })]
        [InlineData("Foo Bar Baz", ' ', new[] { "Foo", "Bar", "Baz" })]
        [InlineData("Foo Bar Baz ", ' ', new[] { "Foo", "Bar", "Baz", "" })]
        [InlineData(" Foo Bar Baz ", ' ', new[] { "", "Foo", "Bar", "Baz", "" })]
        [InlineData(" Foo  Bar Baz ", ' ', new[] { "", "Foo", "", "Bar", "Baz", "" })]
        [InlineData("Foo Baz Bar", default(char), new[] { "Foo Baz Bar" })]
        [InlineData("Foo Baz \x0000 Bar", default(char), new[] { "Foo Baz ", " Bar" })]
        [InlineData("Foo Baz \x0000 Bar\x0000", default(char), new[] { "Foo Baz ", " Bar", "" })]
        public static void SpanSplitCharSeparator(string valueParam, char separator, string[] expectedParam)
        {
            char[][] expected = expectedParam.Select(x => x.ToCharArray()).ToArray();
            AssertEqual(expected, valueParam, valueParam.AsSpan().Split(separator));
        }

        [Theory]
        [InlineData("", new[] { "" })]
        [InlineData(" ", new[] { "", "" })]
        [InlineData("     ", new[] { "", "", "", "", "", "" })]
        [InlineData("  ", new[] { "", "", "" })]
        [InlineData("ab", new[] { "ab" })]
        [InlineData("a b", new[] { "a", "b" })]
        [InlineData("a ", new[] { "a", "" })]
        [InlineData(" b", new[] { "", "b" })]
        [InlineData("Foo Bar Baz", new[] { "Foo", "Bar", "Baz" })]
        [InlineData("Foo Bar Baz ", new[] { "Foo", "Bar", "Baz", "" })]
        [InlineData(" Foo Bar Baz ", new[] { "", "Foo", "Bar", "Baz", "" })]
        [InlineData(" Foo  Bar Baz ", new[] { "", "Foo", "", "Bar", "Baz", "" })]
        public static void SpanSplitDefaultCharSeparator(string valueParam, string[] expectedParam)
        {
            char[][] expected = expectedParam.Select(x => x.ToCharArray()).ToArray();
            AssertEqual(expected, valueParam, valueParam.AsSpan().Split());
        }

        [Theory]
        [InlineData(" Foo Bar Baz,", ", ", new[] { " Foo Bar Baz," })]
        [InlineData(" Foo Bar Baz, ", ", ", new[] { " Foo Bar Baz", "" })]
        [InlineData(", Foo Bar Baz, ", ", ", new[] { "", "Foo Bar Baz", "" })]
        [InlineData(", Foo, Bar, Baz, ", ", ", new[] { "", "Foo", "Bar", "Baz", "" })]
        [InlineData(", , Foo Bar, Baz", ", ", new[] { "", "", "Foo Bar", "Baz" })]
        [InlineData(", , Foo Bar, Baz, , ", ", ", new[] { "", "", "Foo Bar", "Baz", "", "" })]
        [InlineData(", , , , , ", ", ", new[] { "", "", "", "", "", "" })]
        [InlineData("     ", " ", new[] { "", "", "", "", "", "" })]
        [InlineData("  Foo, Bar  Baz  ", "  ", new[] { "", "Foo, Bar", "Baz", "" })]
        public static void SpanSplitStringSeparator(string valueParam, string separator, string[] expectedParam)
        {
            char[][] expected = expectedParam.Select(x => x.ToCharArray()).ToArray();
            AssertEqual(expected, valueParam, valueParam.AsSpan().Split(separator));
        }

        private static void AssertEqual<T>(T[][] items, ReadOnlySpan<T> orig, SpanSplitEnumerator<T> source) where T : IEquatable<T>
        {
            foreach (T[] item in items)
            {
                Assert.True(source.MoveNext());
                ReadOnlySpan<T> slice = orig.Slice(source.Current.start, source.Current.end - source.Current.start);
                Assert.Equal(item, slice.ToArray());
            }

            Assert.False(source.MoveNext());
        }
    }
#endif
}