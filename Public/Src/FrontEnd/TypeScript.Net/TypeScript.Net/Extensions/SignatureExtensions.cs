// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    internal static class SignatureExtensions
    {
        /// <nodoc />
        public static ISignature GetOrSetErasedSignature<T>(this ISignature @this, T data, Func<ISignature, T, ISignature> factory)
        {
            return @this.ErasedSignature ?? (@this.ErasedSignature = factory(@this, data));
        }

        /// <nodoc />
        public static IObjectType GetOrSetIsolatedSignatureType<T>(this ISignature @this, T data, Func<ISignature, T, IObjectType> factory)
        {
            return @this.IsolatedSignatureType ?? (@this.IsolatedSignatureType = factory(@this, data));
        }

        /// <nodoc />
        public static IType GetOrSetResolvedReturnType<T>(this ISignature @this, T data, Func<ISignature, T, IType> factory)
        {
            return @this.ResolvedReturnType ?? (@this.ResolvedReturnType = factory(@this, data));
        }
    }
}
