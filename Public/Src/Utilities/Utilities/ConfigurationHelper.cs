// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities
{
    /// <nodoc />
    public static class ConfigurationHelper
    {
        /// <nodoc />
        public static void ApplyIfNotNull<T>(T value, Action<T> apply) where T : class
        {
            if (value != null)
            {
                apply(value);
            }
        }

        /// <nodoc />
        public static void ApplyIfNotNull<T>(T? value, Action<T> apply) where T : struct
        {
            if (value != null)
            {
                apply(value.Value);
            }
        }
    }
}
