// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <nodoc />
    public enum GlobalInfoKey
    {
        /// <nodoc />
        BuildId,

        /// <nodoc />
        LocalLocationStoreRole,
    }

    /// <summary>
    /// A global (static) class that allows registering and retrieving global information, like current build id etc.
    /// </summary>
    public static class GlobalInfoStorage
    {
        private static readonly object _globalInfoTableLock = new object();
        private static readonly Dictionary<GlobalInfoKey, string> _globalInfoTable = new Dictionary<GlobalInfoKey, string>();

        /// <summary>
        /// Sets the current role of a current service.
        /// </summary>
        public static void SetServiceRole(string role) => SetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole, role);

        /// <summary>
        /// Registers a global key-value information, like build id, or the role of local location store
        /// that will be traced by the remote logger and embedded in every message.
        /// </summary>
        /// <remarks>
        /// if <paramref name="value"/> is <code>null</code>, the global value must be unset.
        /// </remarks>
        public static void SetGlobalInfo(GlobalInfoKey key, string? value)
        {
            lock (_globalInfoTableLock)
            {
                if (string.IsNullOrEmpty(value))
                {
                    _globalInfoTable.Remove(key);
                }
                else
                {
                    _globalInfoTable[key] = value!;
                }
            }
        }

        /// <summary>
        /// Gets a global value associated with a key.
        /// </summary>
        public static string? GetGlobalInfo(GlobalInfoKey key)
        {
            lock (_globalInfoTableLock)
            {
                _globalInfoTable.TryGetValue(key, out var result);
                return result;
            }
        }
    }
}
