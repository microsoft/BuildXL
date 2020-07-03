// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    internal static class TypeParameterExtensions
    {
        public static IType GetOrSetResolvedApparentType<T>(this ITypeParameter @this, T data, Func<ITypeParameter, T, IType> factory)
        {
            if (@this.ResolvedApparentType != null)
            {
                return @this.ResolvedApparentType;
            }

            lock (@this)
            {
                return @this.ResolvedApparentType ?? (@this.ResolvedApparentType = factory(@this, data));
            }
        }

        public static IType GetOrSetConstraint<T>(this ITypeParameter @this, T data, Func<ITypeParameter, T, IType> factory)
        {
            return @this.Constraint ?? (@this.Constraint = factory(@this, data));
        }
    }
}
