// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Code helpers.
    /// </summary>
    internal static class Extensions
    {
        public static T ThrowIfNull<T>(this T val, string valName)
            where T : class
        {
            if (val == null)
            {
                throw new ArgumentNullException(valName);
            }

            return val;
        }

        public static string ThrowIfNullOrEmpty(this string val, string valName)
        {
            if (string.IsNullOrEmpty(val))
            {
                throw new ArgumentNullException(valName);
            }

            return val;
        }
    }
}
