// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace VSCode.DebugAdapter
{
    public sealed class Handles<T>
    {
        private const int StartHandle = 1000;

        private readonly IDictionary<int, T> m_handleMap;
        private int m_nextHandle;

        public Handles()
        {
            m_nextHandle = StartHandle;
            m_handleMap = new Dictionary<int, T>();
        }

        public void Reset()
        {
            m_nextHandle = StartHandle;
            m_handleMap.Clear();
        }

        public int Create(T value)
        {
            var handle = m_nextHandle++;
            m_handleMap[handle] = value;
            return handle;
        }

        public bool TryGet(int handle, out T value)
        {
            return m_handleMap.TryGetValue(handle, out value);
        }

        public T Get(int handle, T @default)
        {
            T value;
            return m_handleMap.TryGetValue(handle, out value) ? value : @default;
        }
    }
}
