// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Set of extension methods for <see cref="Span{T}"/> and <see cref="Memory{T}"/>.
    /// </summary>
    /// <remarks>
    /// The code is adopted from the following PR that was reverted later:
    /// https://github.com/dotnet/runtime/commit/78ed8e8ab18ae6a944e04aaa1c66928acef7da18#diff-27da3246e86c068a251dfa98f5c889b71a6d58d6022040c36c99442fa881d529
    /// </remarks>
    public static class MemoryExtensions
    {
        /// <summary>
        /// Returns a type that allows for enumeration of each element within a split span
        /// using a single space as a separator character.
        /// </summary>
        /// <param name="span">The source span to be enumerated.</param>
        /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/>.</returns>
        public static SpanSplitEnumerator<char> Split(this ReadOnlySpan<char> span)
            => new SpanSplitEnumerator<char>(span, ' ');

        /// <summary>
        /// Returns a type that allows for enumeration of each element within a split span
        /// using the provided separator character.
        /// </summary>
        /// <param name="span">The source span to be enumerated.</param>
        /// <param name="separator">The separator character to be used to split the provided span.</param>
        /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/>.</returns>
        public static SpanSplitEnumerator<char> Split(this ReadOnlySpan<char> span, char separator)
            => new SpanSplitEnumerator<char>(span, separator);

        /// <inheritdoc cref="Split(System.ReadOnlySpan{char})"/>
        public static SpanSplitEnumerator<char> SplitAny(this ReadOnlySpan<char> span, ReadOnlySpan<char> separators)
            => new SpanSplitEnumerator<char>(span, separators, splitAny: true);

        /// <summary>
        /// Returns a type that allows for enumeration of each element within a split span
        /// using the provided separator string.
        /// </summary>
        /// <param name="span">The source span to be enumerated.</param>
        /// <param name="separator">The separator string to be used to split the provided span.</param>
        /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/>.</returns>
        public static SpanSplitEnumerator<char> Split(this ReadOnlySpan<char> span, string separator)
            => new SpanSplitEnumerator<char>(span, (separator ?? string.Empty).AsSpan());
    }

    /// <summary>
    /// <see cref="SpanSplitEnumerator{T}"/> allows for enumeration of each element within a <see cref="System.ReadOnlySpan{T}"/>
    /// that has been split using a provided separator.
    /// </summary>
    public ref struct SpanSplitEnumerator<T> where T : IEquatable<T>
    {
        private readonly ReadOnlySpan<T> _buffer;

        private readonly ReadOnlySpan<T> _separators;
        private readonly T _separator;

        private readonly int _separatorLength;
        private readonly bool _splitOnSingleToken;

        private readonly bool _isInitialized;

        private readonly bool _splitAny;

        private int _startCurrent;
        private int _endCurrent;
        private int _startNext;

        private int _matchCount;

        /// <summary>
        /// Returns an enumerator that allows for iteration over the split span.
        /// </summary>
        /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/> that can be used to iterate over the split span.</returns>
        public SpanSplitEnumerator<T> GetEnumerator() => this;

        /// <summary>
        /// Returns the current element of the enumeration.
        /// </summary>
        /// <returns>Returns a tuple that indicates the bounds of the current element withing the source span.</returns>
        public (int start, int end) Current => (_startCurrent, _endCurrent);

        /// <summary>
        /// Gets the number of matches found in the input source.
        /// </summary>
        public int MatchCount => _matchCount;

        /// <summary>
        /// Gets the current slice.
        /// </summary>
        public ReadOnlySpan<T> GetCurrent() => _buffer.Slice(_startCurrent, _endCurrent - _startCurrent);

        /// <summary>
        /// Tries moving the iterator forward and returns the current match into <paramref name="result"/> if <see cref="MoveNext"/> returns true.
        /// </summary>
        public bool TryGetNextItem(out ReadOnlySpan<T> result)
        {
            if (MoveNext())
            {
                result = GetCurrent();
                return true;
            }

            result = default;
            return false;
        }

        internal SpanSplitEnumerator(ReadOnlySpan<T> span, ReadOnlySpan<T> separators, bool splitAny = false)
        {
            _isInitialized = true;
            _buffer = span;
            _separators = separators;
            _separator = default!;
            _splitOnSingleToken = false;
            _separatorLength = !splitAny && _separators.Length != 0 ? _separators.Length : 1;
            _splitAny = splitAny;
            _startCurrent = 0;
            _endCurrent = 0;
            _startNext = 0;
            _matchCount = 0;
        }

        internal SpanSplitEnumerator(ReadOnlySpan<T> span, T separator)
        {
            _isInitialized = true;
            _buffer = span;
            _separator = separator;
            _separators = default;
            _splitOnSingleToken = true;
            _separatorLength = 1;
            _splitAny = false;
            _startCurrent = 0;
            _endCurrent = 0;
            _startNext = 0;
            _matchCount = 0;
        }

        /// <summary>
        /// Advances the enumerator to the next element of the enumeration.
        /// </summary>
        /// <returns><see langword="true"/> if the enumerator was successfully advanced to the next element; <see langword="false"/> if the enumerator has passed the end of the enumeration.</returns>
        public bool MoveNext()
        {
            if (!_isInitialized || _startNext > _buffer.Length)
            {
                return false;
            }

            ReadOnlySpan<T> slice = _buffer.Slice(_startNext);
            _startCurrent = _startNext;

            int separatorIndex = _splitOnSingleToken
                ? slice.IndexOf(_separator)
                : (_splitAny ? slice.IndexOfAny(_separators) : slice.IndexOf(_separators));
            int elementLength = (separatorIndex != -1 ? separatorIndex : slice.Length);

            _endCurrent = _startCurrent + elementLength;
            _startNext = _endCurrent + _separatorLength;
            _matchCount++;
            return true;
        }
    }
}