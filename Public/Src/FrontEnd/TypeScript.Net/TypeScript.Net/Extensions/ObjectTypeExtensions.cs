// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    internal static class ObjectTypeExtensions
    {
        /// <nodoc />
        public static IResolvedType GetOrSetRegularType<T>(this IFreshObjectLiteralType @this, T data, Func<IFreshObjectLiteralType, T, IResolvedType> factory)
        {
            return @this.RegularType ?? (@this.RegularType = factory(@this, data));
        }
    }
}
