// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginHandlers<T, U>
    {
        /// <nodoc />
        private readonly ConcurrentDictionary<T, U> m_handlers;

        /// <nodoc />
        public int Count => m_handlers.Count;

        /// <nodoc />
        public PluginHandlers()
        {
            m_handlers = new ConcurrentDictionary<T, U>();
        }

        /// <nodoc />
        public void AddOrUpdateHanlder(T type, Func<U> addHandlerFunc, Func<U, U> updateHandlerFunc)
        {
            m_handlers.AddOrUpdate(type, m => addHandlerFunc(), (m, r) => updateHandlerFunc(r));
        }

        /// <nodoc />
        public bool TryGet(T type, out U handler)
        {
            return m_handlers.TryGetValue(type, out handler);
        }

        /// <nodoc />
        public bool TryAdd(T type, U handler)
        {
            return m_handlers.TryAdd(type, handler);
        }

        /// <nodoc />
        public bool TryRemove(T type)
        {
            return m_handlers.TryRemove(type, out U handler);
        }
    }
    /// <nodoc />
    public class PluginHandlers : PluginHandlers<PluginMessageType, IPlugin>
    {
    }
}
