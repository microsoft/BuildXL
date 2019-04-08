// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Allows expansion of paths with a specified string used to expand roots
    /// </summary>
    public sealed class RootExpander : HierarchicalNameTable.NameExpander
    {
        private readonly Dictionary<HierarchicalNameId, string> m_roots;
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Class constructor
        /// </summary>
        public RootExpander(PathTable pathTable)
            : base()
        {
            m_pathTable = pathTable;
            m_roots = new Dictionary<HierarchicalNameId, string>();
        }

        /// <summary>
        /// Adds the expansion for a root
        /// </summary>
        public void Add(AbsolutePath root, string name)
        {
            m_pathTable.SetFlags(root.Value, HierarchicalNameTable.NameFlags.Root);

            if (!m_roots.ContainsKey(root.Value))
            {
                m_roots.Add(root.Value, string.Format(CultureInfo.InvariantCulture, "{0}", name));
            }
        }

        private bool TryGetRootToken(HierarchicalNameId name, HierarchicalNameTable.NameFlags nameFlags, out string rootToken)
        {
            if (((nameFlags & HierarchicalNameTable.NameFlags.Root) == HierarchicalNameTable.NameFlags.Root) &&
                m_roots.TryGetValue(name, out rootToken))
            {
                return true;
            }

            rootToken = null;
            return false;
        }

        /// <inheritdoc />
        public override int GetLength(
            HierarchicalNameId name,
            StringTable stringTable,
            StringId stringId,
            HierarchicalNameTable.NameFlags nameFlags,
            out bool expandContainer)
        {
            string rootToken;
            if (TryGetRootToken(name, nameFlags, out rootToken))
            {
                expandContainer = false;
                return rootToken.Length;
            }

            return base.GetLength(name, stringTable, stringId, nameFlags, out expandContainer);
        }

        /// <inheritdoc />
        public override int CopyString(
            HierarchicalNameId name,
            StringTable stringTable,
            StringId stringId,
            HierarchicalNameTable.NameFlags nameFlags,
            char[] buffer,
            int endIndex)
        {
            string rootToken;
            if (TryGetRootToken(name, nameFlags, out rootToken))
            {
                rootToken.CopyTo(0, buffer, endIndex - rootToken.Length, rootToken.Length);
                return rootToken.Length;
            }

            return base.CopyString(name, stringTable, stringId, nameFlags, buffer, endIndex);
        }
    }
}
