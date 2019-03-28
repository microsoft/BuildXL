// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    /// <summary>
    /// Set of extension methods for <see cref="NodeLinks"/> that helps to make the checker thread safe.
    /// </summary>
    internal static class NodeLinksExtensions
    {
        public static IType GetOrSetResolvedType<T>(this NodeLinks links, T data, Func<NodeLinks, T, IType> factory)
        {
            return links.ResolvedType ?? (links.ResolvedType = factory(links, data));
        }

        public static ISignature GetOrSetResolvedSignature<T>(this NodeLinks links, T data, Func<NodeLinks, T, ISignature> factory)
        {
            return links.ResolvedSignature ?? (links.ResolvedSignature = factory(links, data));
        }

        public static bool GetOrSetIsVisible<T>(this NodeLinks links, T data, Func<NodeLinks, T, bool> factory)
        {
            return (links.IsVisible ?? (links.IsVisible = factory(links, data))).Value;
        }
    }
}
