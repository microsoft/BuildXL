// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace BuildXL.Cache.ContentStore.Extensions
{
    /// <summary>
    ///     Extensions for <see cref="StringBuilder" />.
    /// </summary>
    public static class StringBuilderExtensions
    {
        /// <summary>
        ///     Concatenate a text into a string builder.
        /// </summary>
        /// <param name="builder">
        ///     The string builder.
        /// </param>
        /// <param name="text">
        ///     Text to be concatenated.
        /// </param>
        /// <param name="separator">
        ///     The concatenator.
        /// </param>
        public static void Concat(this StringBuilder builder, string text, string separator = ", ")
        {
            builder.Append(builder.Length > 0 ? separator : string.Empty);
            builder.Append(text);
        }

        /// <nodoc />
        public static void AppendSequence<T>(this StringBuilder builder, IEnumerable<T> data, Action<StringBuilder, T> appendToBuilder, string separator = ", ")
        {
            bool first = true;
            foreach (var e in data)
            {
                if (!first)
                {
                    builder.Append(separator);
                }

                if (first)
                {
                    first = false;
                }

                appendToBuilder(builder, e);
            }
        }
    }
}
