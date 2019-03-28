// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <summary>
    /// Path expander
    /// </summary>
    public sealed class TestPathExpander : HierarchicalNameTable.NameExpander
    {
        private readonly Dictionary<HierarchicalNameId, string> m_replacements;

        private readonly HierarchicalNameTable m_table;

        /// <nodoc />
        public TestPathExpander(HierarchicalNameTable table)
            : base()
        {
            m_replacements = new Dictionary<HierarchicalNameId, string>();
            m_table = table;
        }

        /// <summary>
        /// Adds a replacement of a root path
        /// </summary>
        public void AddReplacement(AbsolutePath root, string name)
        {
            m_table.SetFlags(root.Value, HierarchicalNameTable.NameFlags.Root);

            if (!m_replacements.ContainsKey(root.Value))
            {
                m_replacements.Add(root.Value, string.Format(CultureInfo.InvariantCulture, "{0}", name));
            }
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

        private bool TryGetRootToken(HierarchicalNameId name, HierarchicalNameTable.NameFlags nameFlags, out string rootToken)
        {
            if (((nameFlags & HierarchicalNameTable.NameFlags.Root) == HierarchicalNameTable.NameFlags.Root) &&
                m_replacements.TryGetValue(name, out rootToken))
            {
                return true;
            }

            rootToken = null;
            return false;
        }
    }
}
