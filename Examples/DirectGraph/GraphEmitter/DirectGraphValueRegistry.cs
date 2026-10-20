// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Engine.DirectGraph;
using Google.Protobuf.Collections;

namespace GraphEmitter
{
    /// <summary>
    /// Emitter-side interning for the DirectGraph protocol: gives each distinct path and string its id, and registers the
    /// values not sent yet, with their ids, on the operation that is about to be written.
    /// </summary>
    /// <remarks>
    /// Ids follow the order operations are written in, so use one instance per stream, in the one place operations are
    /// written, and look values up only for the operation being written next. Operations may be generated concurrently
    /// elsewhere as long as they carry plain values until then. Not thread-safe, deliberately.
    /// </remarks>
    internal sealed class DirectGraphValueRegistry
    {
        private readonly Dictionary<string, uint> m_pathIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> m_stringIds = new(StringComparer.Ordinal);
        private readonly List<string> m_newPaths = new();
        private readonly List<string> m_newStrings = new();

        /// <summary>
        /// The id of <paramref name="path"/>, registering it with the next operation written if it has not been sent yet.
        /// </summary>
        public uint Path(string path) => GetOrAdd(m_pathIds, m_newPaths, path);

        /// <summary>
        /// The id of <paramref name="value"/>, registering it with the next operation written if it has not been sent yet.
        /// </summary>
        public uint String(string value) => GetOrAdd(m_stringIds, m_newStrings, value);

        /// <summary>
        /// Moves the values first used since the previous call onto <paramref name="operation"/>, each with its id. Call
        /// it for every operation, immediately before writing it.
        /// </summary>
        public void AttachNewValues(GraphOperation operation)
        {
            Attach(operation.NewPaths, m_newPaths, m_pathIds.Count);
            Attach(operation.NewStrings, m_newStrings, m_stringIds.Count);
        }

        private static void Attach(RepeatedField<Registration> registrations, List<string> newValues, int registered)
        {
            uint id = (uint)(registered - newValues.Count);
            foreach (string value in newValues)
            {
                registrations.Add(new Registration { Id = id++, Value = value });
            }

            newValues.Clear();
        }

        private static uint GetOrAdd(Dictionary<string, uint> ids, List<string> newValues, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!ids.TryGetValue(value, out uint id))
            {
                id = (uint)ids.Count;
                ids.Add(value, id);
                newValues.Add(value);
            }

            return id;
        }
    }
}
