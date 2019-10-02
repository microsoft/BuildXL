// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// File system which is used to construct a pip's file system.
    /// </summary>
    public sealed class PipFileSystemView
    {
        private BitSet m_bitset;

        internal void Clear()
        {
            m_bitset?.Clear();
        }

        /// <summary>
        /// Initialized the filesystem to allow collecting paths. This must be called before paths are added or enumerations
        /// are performed from this object.
        /// </summary>
        public void Initialize(PathTable pathTable)
        {
            if (m_bitset == null)
            {
                var bitSetSize = BitSet.RoundToValidBitCount(Convert.ToInt32(pathTable.Count * 1.25));
                m_bitset = new BitSet(bitSetSize);
                m_bitset.SetLength(bitSetSize);
            }
        }

        /// <summary>
        /// Enumerate the directory and apply a given action to its members
        /// </summary>
        public PathExistence EnumerateDirectory(PathTable pathTable, AbsolutePath path, Action<AbsolutePath, string> handleChildPath)
        {
            // We can bail out early if the path is known as either a file or as being nonexistent
            var id = GetIndexByPath(path);

            bool atLeastOneChild = false;

            if (id >= m_bitset.Length)
            {
                // It means that the path is not in the table when we construct the pip file system.
                // We can safely ignore it and return Nonexistent.
                return PathExistence.Nonexistent;
            }

            foreach (var childPathValue in pathTable.EnumerateImmediateChildren(path.Value))
            {
                var index = HierarchicalNameTable.GetIndexFromValue(childPathValue.Value);

                if (index >= m_bitset.Length)
                {
                    continue;
                }

                if (m_bitset.Contains(index))
                {
                    atLeastOneChild = true;
                    var filePath = GetPathByIndex(pathTable, index);
                    handleChildPath(filePath, filePath.GetName(pathTable).ToString(pathTable.StringTable));
                }
            }

            return atLeastOneChild ? PathExistence.ExistsAsDirectory : PathExistence.Nonexistent;
        }

        /// <summary>
        /// Updates the file system with a given seal directory
        /// </summary>
        /// <remarks>
        /// Not thread-safe
        /// </remarks>
        public void AddSealDirectoryContents(IPipExecutionEnvironment env, DirectoryArtifact directoryDependency)
        {
            foreach (var fileArtifact in env.State.FileContentManager.ListSealedDirectoryContents(directoryDependency))
            {
                AddPath(env.Context.PathTable, fileArtifact);
            }
        }

        /// <summary>
        /// Updates the pip file system after adding a path
        /// </summary>
        /// <remarks>
        /// Not thread-safe
        /// </remarks>
        public void AddPath(PathTable pathTable, AbsolutePath path)
        {
            var index = GetIndexByPath(path);
            while (true)
            {
                if (index >= m_bitset.Length)
                {
                    // under unknown conditions, index might be larger than pathTable.Count
                    // let's just select the largest one, so there is enough space in the bitset
                    var newSize = BitSet.RoundToValidBitCount(Math.Max(pathTable.Count, index));
                    m_bitset.SetLength(newSize);

                    if (index < 0 || index >= m_bitset.Length)
                    {
                        Contract.Assert(false, $"The size of BitSet was increased, yet index={index} is still outside [0, {m_bitset.Length}) range. NewSize = {newSize}. PathTable.Count={pathTable.Count}");
                    }
                }

                if (m_bitset.Contains(index))
                {
                    break;
                }

                m_bitset.Add(index);
                index = GetParentIndex(pathTable, index);
            }
        }

        /// <summary>
        /// Get the index of path
        /// </summary>
        private static AbsolutePath GetPathByIndex(PathTable pathTable, int index)
        {
            return new AbsolutePath(pathTable.GetIdFromIndex(index));
        }

        /// <summary>
        /// Get the index of path
        /// </summary>
        private static int GetIndexByPath(AbsolutePath path)
        {
            return HierarchicalNameTable.GetIndexFromValue(path.Value.Value);
        }

        private static int GetParentIndex(PathTable pathTable, int index)
        {
            return pathTable.GetContainerIndex(index);
        }
    }
}
