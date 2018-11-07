// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// A factory type used by compilers to create instances of the type <see cref="FormattableString"/>.
    /// </summary>
    public static class FormattableStringFactory
    {
#pragma warning disable CS0436 // Type conflicts with imported type
        /// <summary>
        /// Create a <see cref="FormattableString"/> from a composite format string and object
        /// array containing zero or more objects to format.
        /// </summary>
        public static FormattableString Create(string format, params object[] arguments)
#pragma warning restore CS0436 // Type conflicts with imported type
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

#pragma warning disable CS0436 // Type conflicts with imported type
            return new FormattableString(format, arguments);
#pragma warning restore CS0436 // Type conflicts with imported type
        }
    }
}
