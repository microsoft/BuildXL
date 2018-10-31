// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Statistics about data that went through the <code>BuildXLWriter</code>.
    /// </summary>
    public static class BuildXLWriterStats
    {
        private static readonly ConcurrentDictionary<int, long> s_bytes = new ConcurrentDictionary<int, long>();
        private static readonly ConcurrentDictionary<int, long> s_counts = new ConcurrentDictionary<int, long>();
        private static readonly ConcurrentDictionary<Type, int> s_types = new ConcurrentDictionary<Type, int>();

        /// <summary>
        /// Gets a unique identifier for a type
        /// </summary>
        public static int GetTypeId<T>()
        {
            return GetTypeId(typeof(T));
        }

        /// <summary>
        /// Gets a unique identifier for a type
        /// </summary>
        public static int GetTypeId(Type type)
        {
            Contract.Requires(type != null);
            return s_types.GetOrAdd(type, _ => HashCodeHelper.GetOrdinalHashCode(type.FullName));
        }

        /// <summary>
        /// Gets the name of the type referenced by the ID
        /// </summary>
        /// <remarks>
        /// This has a linear lookup. Should only be used for error reporting or optimized if used in actual execution
        /// </remarks>
        public static string GetTypeName(int typeId)
        {
            foreach (Type type in Types)
            {
                if (HashCodeHelper.GetOrdinalHashCode(type.FullName) == typeId)
                {
                    return type.FullName;
                }
            }

            return "Unknown";
        }

        /// <summary>
        /// Get the list of categories
        /// </summary>
        public static IEnumerable<Type> Types => s_types.Keys.ToArray();

        /// <summary>
        /// Gets the name of a category
        /// </summary>
        public static string GetName(Type type)
        {
            Contract.Requires(type != null);
            return type.GetTypeInfo().IsGenericType
                ? type.Name.Substring(0, type.Name.IndexOf('`')) + "<" + string.Join(", ", type.GetGenericArguments().Select(u => GetName(u))) + ">"
                : type.Name;
        }

        /// <summary>
        /// Get category bytes
        /// </summary>
        public static long GetBytes(Type type)
        {
            long bytes;
            if (!s_bytes.TryGetValue(s_types[type], out bytes))
            {
                bytes = 0;
            }

            return bytes;
        }

        /// <summary>
        /// Get category count
        /// </summary>
        public static long GetCount(Type type)
        {
            long count;
            if (!s_counts.TryGetValue(s_types[type], out count))
            {
                count = 0;
            }

            return count;
        }

        internal static void Add(int name, long diff)
        {
            s_bytes.AddOrUpdate(name, diff, (_, old) => old + diff);
            s_counts.AddOrUpdate(name, 1, (_, old) => old + 1);
        }
    }
}
