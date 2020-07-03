// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
