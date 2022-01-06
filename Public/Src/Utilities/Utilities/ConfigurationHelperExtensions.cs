// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.ConfigurationHelpers
{
    /// <summary>
    /// A special class that defines some methods from <see cref="ConfigurationHelper"/> as extension methods.
    /// </summary>
    /// <remarks>
    /// <code>using static</code> does not allow using methods defined as extension methods.
    /// It means that the code should decide if to use such helpers as extension methods or via 'using static',
    /// defining a separate class solves this ambiguity.
    /// This type is moved intentionally into a sub-namespace of 'BuildXL.Utilities' to name conflicts that may occur
    /// if the client code have 'using BuildXL.Utilities'.
    /// </remarks>
    public static class ConfigurationHelperExtensions
    {
        /// <nodoc />
        public static void ApplyIfNotNull<T>(this T value, Action<T> apply)
            where T : class => ConfigurationHelper.ApplyIfNotNull(value, apply);

        /// <nodoc />
        public static void ApplyIfNotNull<T>(this T? value, Action<T> apply)
            where T : struct
            => ConfigurationHelper.ApplyIfNotNull(value, apply);
    }
}