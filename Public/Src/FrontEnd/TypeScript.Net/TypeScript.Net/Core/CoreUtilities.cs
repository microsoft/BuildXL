// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;

namespace TypeScript.Net.Core
{
    /// <summary>
    /// Ternary values are defined such that
    /// x &amp; y is False if either x or y is False.
    /// x &amp; y is Maybe if either x or y is Maybe, but neither x or y is False.
    /// x &amp; y is True if both x and y are True.
    /// x | y is False if both x and y are False.
    /// x | y is Maybe if either x or y is Maybe, but neither x or y is True.
    /// x | y is True if either x or y is True.
    /// </summary>
    public enum Ternary
    {
        /// <nodoc />
        False = 0,

        /// <nodoc />
        Maybe = 1,

        /// <nodoc />
        True = -1,
    }

    /// <nodoc/>
    public static class CoreUtilities
    {
        /// <summary>
        /// Memoize function.
        /// </summary>
        public static Func<T> Memoize<T>(Func<T> func)
        {
            T result = default(T);
            return () =>
            {
                if (func != null)
                {
                    result = func();
                    func = null;
                }

                return result;
            };
        }

        /// <summary>
        /// Returns true if the <paramref name="value"/> is falsy.
        /// </summary>
        public static bool IsFalsy<T>(T value)
        {
            return FalsyChecker<T>.Instance.IsFalsy(value);
        }

        /// <summary>
        /// Special invalid identifier that represents a pending identifier assignment operation.
        /// Used to achieve atomic updates in a lock-free manner.
        /// </summary>
        public const int ReservedInvalidIdentifier = -2;

        /// <summary>
        /// Invalid identifier value.
        /// </summary>
        public const int InvalidIdentifier = -1;

        /// <summary>
        /// Returns true if a given identifier is valid.
        /// </summary>
        public static bool IsValid(this int identifier)
        {
            return identifier > InvalidIdentifier;
        }

        /// <summary>
        /// Returns identifier value or default (i.e. 0) otherwise.
        /// </summary>
        public static int ValueOrDefault(this int number)
        {
            return number.IsValid() ? number : default(int);
        }

        private abstract class FalsyChecker<T>
        {
            public static readonly FalsyChecker<T> Instance = GetInstance();

            private static FalsyChecker<T> GetInstance()
            {
                if (typeof(T) == typeof(bool))
                {
                    object checker = new BoolFalsyChecker();
                    return (FalsyChecker<T>)checker;
                }

                if (typeof(T).GetTypeInfo().IsValueType)
                {
                    return new DefaultFalsyChecker<T>();
                }

                return new ObjectFalsyChecker<T>();
            }

            public abstract bool IsFalsy(T value);
        }

        // TODO: int to falsy
        // TODO: string to falsy (empty string is falsy)
        private sealed class DefaultFalsyChecker<T> : FalsyChecker<T>
        {
            public override bool IsFalsy(T value)
            {
                return false;
            }
        }

        private sealed class BoolFalsyChecker : FalsyChecker<bool>
        {
            public override bool IsFalsy(bool value)
            {
                return !value;
            }
        }

        private sealed class ObjectFalsyChecker<T> : FalsyChecker<T>
        {
            public override bool IsFalsy(T value)
            {
                return value == null;
            }
        }
    }
}
