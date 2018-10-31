// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A composite format string along with the arguments to be formatted. An instance of this
    /// type may result from the use of the C# or VB language primitive "interpolated string".
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public static class FormattableStringEx
    {
        /// <summary>
        /// Format the given object in the invariant culture. This static method may be
        /// imported in C# with using static feature.
        /// <code>
        /// using static System.FormattableString;
        /// </code>.
        /// Within the scope
        /// of that import directive an interpolated string may be formatted in the
        /// invariant culture by writing, for example,
        /// <code>
        /// Invariant($"{{ lat = {latitude}; lon = {longitude} }}")
        /// </code>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Invariant(FormattableString formattable)
        {
            return formattable.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format the given object in the invariant culture. This static method may be
        /// imported in C# with using static feature.
        /// <code>
        /// using static System.FormattableString;
        /// </code>.
        /// Within the scope
        /// of that import directive an interpolated string may be formatted in the
        /// invariant culture by writing, for example,
        /// <code>
        /// Invariant($"{{ lat = {latitude}; lon = {longitude} }}")
        /// </code>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string I(FormattableString formattable)
        {
            return formattable.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format the given object in the current culture. This static method may be
        /// imported in C# with using static feature.
        /// <code>
        /// using static System.FormattableString;
        /// </code>.
        /// Within the scope
        /// of that import directive an interpolated string may be formatted in the
        /// invariant culture by writing, for example,
        /// <code>
        /// Current($"{{ lat = {latitude}; lon = {longitude} }}")
        /// </code>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Current(FormattableString formattable)
        {
            return formattable.ToString(CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Format the given object in the current culture. This static method may be
        /// imported in C# with using static feature.
        /// <code>
        /// using static System.FormattableString;
        /// </code>.
        /// Within the scope
        /// of that import directive an interpolated string may be formatted in the
        /// invariant culture by writing, for example,
        /// <code>
        /// C($"{{ lat = {latitude}; lon = {longitude} }}")
        /// </code>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string C(FormattableString formattable)
        {
            return formattable.ToString(CultureInfo.CurrentCulture);
        }
   }
}
