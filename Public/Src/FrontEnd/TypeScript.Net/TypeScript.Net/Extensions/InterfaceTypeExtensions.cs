// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    internal static class InterfaceTypeExtensions
    {
        public static IReadOnlyList<IType> GetOrSetResolvedBaseTypes<T>(this IInterfaceType @this, T data, Func<IInterfaceType, T, IReadOnlyList<IType>> factory)
        {
            return @this.ResolvedBaseTypes ?? (@this.ResolvedBaseTypes = factory(@this, data));
        }

        public static IType GetOrSetResolvedBaseConstructorType<T>(this IInterfaceType @this, T data, Func<IInterfaceType, T, IType> factory)
        {
            return @this.ResolvedBaseConstructorType ?? (@this.ResolvedBaseConstructorType = factory(@this, data));
        }
    }
}
