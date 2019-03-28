// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
