// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
