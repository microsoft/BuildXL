// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.Host.Service.Internal
{
    internal static class ConfigurationHelper
    {
        public static void ApplyIfNotNull<T>(T value, Action<T> apply) where T : class
        {
            if (value != null)
            {
                apply(value);
            }
        }

        public static void ApplyIfNotNull<T>(T? value, Action<T> apply) where T : struct
        {
            if (value != null)
            {
                apply(value.Value);
            }
        }
    }
}
