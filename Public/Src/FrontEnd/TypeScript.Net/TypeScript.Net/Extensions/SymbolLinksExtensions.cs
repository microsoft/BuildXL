// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    /// <summary>
    /// Set of extension methods for <see cref="ISymbolLinks"/> that helps to make the checker thread safe.
    /// </summary>
    /// <remarks>
    /// General consideration regarless using locks in the following methods:
    /// There is no locks there because some functions are recursive.
    /// It means that one thread can start resolution one symbol and reach another one.
    /// Meanwhile another thread can start resolution from 'another one' and reach the first one.
    /// If each symbol resolution will acquire a lock, a deadlock can occur.
    /// Instead of using locks, those methods perform an atomic updates with non-zero likelihood of doing the same job twice.
    /// But this is definitely better than a deadlock or coarse grained locking.
    /// </remarks>
    internal static class SymbolLinksExtensions
    {
        /// <nodoc />
        public static ISymbol GetOrSetTarget<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, ISymbol> factory)
        {
            return links.Target ?? (links.Target = factory(links, data));
        }

        /// <nodoc />
        public static ISymbol GetOrSetDirectTarget<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, ISymbol> factory)
        {
            return links.DirectTarget ?? (links.DirectTarget = factory(links, data));
        }

        /// <nodoc />
        public static IType GetOrSetType<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, IType> factory)
        {
            // TODO: Switch to GetOrSetAtomic if race condition will keep happening.
            // return ConcurrencyUtilities.GetOrSetAtomic(links, data, l => l.Type, (l, t) => l.Type = t, factory);
            return links.Type ?? (links.Type = factory(links, data));
        }

        /// <nodoc />
        public static IType GetOrSetDeclaredType<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, IType> factory)
        {
            // It is very important to use double-checked locking here to avoid subtle race conditions.
            // In all cases the callback is pure, so there is no possibility of deadlocks,
            // but sycnhronization is required because in some cases referencial type equalitity matters.
            // Consider the following method: merge<T>():T.
            // When the checker constructs a signature for this method both, type argument and the result type should be the same
            // (not structurally the same, but referentially the same).
            // To achieve this, DeclaredType computation for a node should be synchronized.
            if (links.DeclaredType != null)
            {
                return links.DeclaredType;
            }

            lock (links)
            {
                return links.DeclaredType ?? (links.DeclaredType = factory(links, data));
            }
        }

        /// <nodoc />
        public static IType GetOrSetInferredClassType<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, IType> factory)
        {
            return links.InferredClassType ?? (links.InferredClassType = factory(links, data));
        }

        /// <nodoc />
        public static IReadOnlyList<ITypeParameter> GetOrSetTypeParameters<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, List<ITypeParameter>> factory)
        {
            return links.TypeParameters ?? (links.TypeParameters = factory(links, data));
        }

        /// <nodoc />
        public static Map<IType> GetOrSetInstantiations<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, Map<IType>> factory)
        {
            return links.Instantiations ?? (links.Instantiations = factory(links, data));
        }

        /// <nodoc />
        public static bool GetOrSetReferenced<T>(this ISymbolLinks links, T state, Func<ISymbolLinks, T, bool> factory)
        {
            if (links.Referenced.GetValueOrDefault() == false)
            {
                links.Referenced = factory(links, state);
            }

            return links.Referenced.GetValueOrDefault();
        }

        /// <nodoc />
        public static ISymbolTable GetOrSetResolvedExports<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, ISymbolTable> factory)
        {
            return links.ResolvedExports ?? (links.ResolvedExports = factory(links, data));
        }

        /// <nodoc />
        public static bool GetOrSetExportsChecked<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, bool> factory)
        {
            if (!links.ExportsChecked)
            {
                links.ExportsChecked = factory(links, data);
            }

            return links.ExportsChecked;
        }

        /// <nodoc />
        public static bool GetOrSetIsNestedRedeclaration<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, bool> factory)
        {
            if (links.IsNestedRedeclaration == null)
            {
                links.IsNestedRedeclaration = factory(links, data);
            }

            return links.IsNestedRedeclaration.Value;
        }

        /// <nodoc />
        public static bool GetOrSetExportsSomeValue<T>(this ISymbolLinks links, T data, Func<ISymbolLinks, T, bool> factory)
        {
            if (!links.ExportsSomeValue.HasValue)
            {
                links.ExportsSomeValue = factory(links, data);
            }

            return links.ExportsSomeValue.Value;
        }
    }
}
