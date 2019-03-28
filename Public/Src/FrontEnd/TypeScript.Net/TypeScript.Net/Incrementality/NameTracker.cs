// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TypeScript.Net.Incrementality
{
    /// <summary>
    /// Helper class that tracks and computes the full name during spec binding alaysis.
    /// </summary>
    internal readonly struct NameTracker
    {
        private readonly Stack<string> m_stack;
        public const int DefaultCapacity = 50;

        public NameTracker(int capacity)
            : this()
        {
            m_stack = new Stack<string>(capacity);
        }

        /// <nodoc />
        public readonly struct PreserveLengthTracker : IDisposable
        {
            private readonly int m_oldCount;
            private readonly NameTracker m_tracker;
            private readonly bool m_restoreOriginalSize;

            /// <nodoc />
            public PreserveLengthTracker(NameTracker tracker, bool restoreOriginalSize)
            {
                m_oldCount = tracker.m_stack.Count;
                m_tracker = tracker;
                m_restoreOriginalSize = restoreOriginalSize;
            }

            /// <nodoc />
            public void Dispose()
            {
                if (m_restoreOriginalSize)
                {
                    var currentCount = m_tracker.m_stack.Count;

                    for (int i = 0; i < currentCount - m_oldCount; i++)
                    {
                        m_tracker.m_stack.Pop();
                    }
                }
            }
        }

        /// <nodoc />
        public PreserveLengthTracker PreserveLength(bool restoreOriginalSize = true)
        {
            return new PreserveLengthTracker(this, restoreOriginalSize);
        }

        /// <nodoc />
        public PreserveLengthTracker AutoPush(string name)
        {
            var tracker = new PreserveLengthTracker(this, restoreOriginalSize: true);

            Push(name);

            return tracker;
        }

        /// <nodoc />
        public void Push(string name)
        {
            m_stack.Push(name);
        }

        /// <nodoc />
        public Stack<string> Names => m_stack;

        /// <nodoc />
        public string CurrentName => string.Join(".", m_stack.Reverse());
    }
}
