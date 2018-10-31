// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// A composite format string along with the arguments to be formatted. An instance of this
    /// type may result from the use of the C# or VB language primitive "interpolated string".
    /// </summary>
    /// <remarks>
    /// TSE Change: to avoid additional allocations, this type is defined as a struct.
    /// </remarks>
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct FormattableString : IFormattable
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        private readonly object[] m_arguments;

        /// <nodoc/>
        public FormattableString(string format, object[] arguments)
            : this()
        {
            m_arguments = arguments;
            Format = format;
        }

        /// <summary>
        /// The composite format string.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Returns an object array that contains zero or more objects to format. Clients should not
        /// mutate the contents of the array.
        /// </summary>
        public object[] GetArguments() => m_arguments;

        /// <summary>
        /// The number of arguments to be formatted.
        /// </summary>
        public int ArgumentCount => m_arguments.Length;

        /// <summary>
        /// Returns one argument to be formatted from argument position <paramref name="index"/>.
        /// </summary>
        public object GetArgument(int index) => m_arguments[index];

        /// <summary>
        /// Format to a string using the given culture.
        /// </summary>
        public string ToString(IFormatProvider formatProvider)
        {
            return string.Format(formatProvider, Format, m_arguments);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        string IFormattable.ToString(string ignored, IFormatProvider formatProvider)
        {
            return ToString(formatProvider);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return ToString(Globalization.CultureInfo.CurrentCulture);
        }
    }
}
