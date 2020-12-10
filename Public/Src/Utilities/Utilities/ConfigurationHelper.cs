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

        /// <nodoc />
        public static TEnum ParseEnumOrDefault<TEnum>(string value, string propertyName, TEnum defaultValue) where TEnum : struct
        {
            if (value != null)
            {
                if (!Enum.TryParse<TEnum>(value, out var parsed))
                {
                    throw new ArgumentException($"Failed to parse `{propertyName}` setting with value `{value}` into type `{typeof(TEnum)}`");
                }

                return parsed;
            }
            else
            {
                return defaultValue;
            }
        }

        /// <nodoc />
        public static void ApplyEnumIfNotNull<TEnum>(string value, string propertyName, Action<TEnum> apply) where TEnum : struct
        {
            if (value != null)
            {
                if (!Enum.TryParse<TEnum>(value, out var parsed))
                {
                    throw new ArgumentException($"Failed to parse `{propertyName}` setting with value `{value}` into type `{typeof(TEnum)}`");
                }

                apply(parsed);
            }
        }

        /// <nodoc />
        public static void ApplyEnumIfNotNull<TEnum>(string value, Action<TEnum> apply) where TEnum : struct
        {
            if (value != null)
            {
                if (Enum.TryParse<TEnum>(value, out var parsed))
                {
                    apply(parsed);
                }

                apply(parsed);
            }
        }

        /// <nodoc />
        public static TOutput IfNotNull<TInput, TOutput>(TInput? value, Func<TInput, TOutput> apply) where TInput : struct
        {
            if (value != null)
            {
                return apply(value.Value);
            }

            return default(TOutput);
        }
    }
}
