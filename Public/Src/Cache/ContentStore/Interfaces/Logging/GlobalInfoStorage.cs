// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

            // Triggering the event outside of the lock to avoid potential deadlock
            // if the handler will cause another change of a global state.

            // null sender is a common case for static events.
            GlobalInfoChanged?.Invoke(sender: null, new GlobalInfoChangedEventArgs(key, value));
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

        /// <summary>
        /// Notifies when a global key-value pair changes.
        /// </summary>
        /// <remarks>
        /// This is a static event, so in order to prevent memory leaks
        /// do not subscribe forever to this even from a non-global types.
        /// </remarks>
        public static event EventHandler<GlobalInfoChangedEventArgs>? GlobalInfoChanged; 

        /// <summary>
        /// Represents a class that contains event data that occurs when the global entry key-value pair changes.
        /// </summary>
        public sealed class GlobalInfoChangedEventArgs : EventArgs
        {
            /// <nodoc />
            public GlobalInfoChangedEventArgs(GlobalInfoKey key, string? value)
            {
                Key = key;
                Value = value;
            }

            /// <summary>
            /// Represents a key that was changed.
            /// </summary>
            public GlobalInfoKey Key { get; }

            /// <summary>
            /// Represents a new value. <code>null</code> if the value was removed.
            /// </summary>
            public string? Value { get; }
        }
    }
}
