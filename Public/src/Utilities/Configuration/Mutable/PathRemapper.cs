// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Class that deals with mapping paths from one pathtable to the other
    /// </summary>
    /// <remarks>
    /// This is needed for updating the configuration object.
    /// In certain cases we load a pathtable from disc and not build a fresh one.
    /// This is not the ideal way to handle this, but it is a quick workaround that keeps us going for now.
    /// </remarks>
    public sealed class PathRemapper
    {
        private readonly PathTable m_oldPathTable;
        private readonly PathTable m_newPathTable;
        private readonly Func<string, string> m_pathStringRemapper;
        private readonly Func<string, string> m_pathAtomStringRemapper;

        /// <summary>
        /// Default constructor which will not remap any paths and just return the ones passed in
        /// </summary>
        public PathRemapper()
        {
        }

        /// <summary>
        /// Constructor in which each path gets mapped from the old path table to the new pathtable.
        /// </summary>
        public PathRemapper(
            PathTable oldPathTable,
            PathTable newPathTable,
            Func<string, string> pathStringRemapper = null,
            Func<string, string> pathAtomStringRemapper = null)
        {
            Contract.Requires(oldPathTable != null);
            Contract.Requires(newPathTable != null);

            m_oldPathTable = oldPathTable;
            m_newPathTable = newPathTable;

            m_pathStringRemapper = pathStringRemapper;
            m_pathAtomStringRemapper = pathAtomStringRemapper;
        }

        /// <nodoc />
        public PathAtom Remap(PathAtom pathAtom)
        {
            if (m_oldPathTable == null || !pathAtom.IsValid)
            {
                return pathAtom;
            }

            var pathAtomString = pathAtom.ToString(m_oldPathTable.StringTable);

            if (m_pathAtomStringRemapper != null)
            {
                pathAtomString = m_pathAtomStringRemapper(pathAtomString);
            }

            return PathAtom.Create(m_newPathTable.StringTable, pathAtomString);
        }

        /// <nodoc />
        public RelativePath Remap(RelativePath relativePath)
        {
            if (m_oldPathTable == null || !relativePath.IsValid)
            {
                return relativePath;
            }

            return RelativePath.Create(relativePath.GetAtoms().Select(Remap).ToArray());
        }

        /// <nodoc />
        public AbsolutePath Remap(AbsolutePath path)
        {
            if (m_oldPathTable == null || !path.IsValid)
            {
                return path;
            }

            var pathString = path.ToString(m_oldPathTable);

            if (m_pathStringRemapper != null)
            {
                pathString = m_pathStringRemapper(pathString);
            }

            return AbsolutePath.Create(m_newPathTable, pathString);
        }

        /// <nodoc />
        public List<AbsolutePath> Remap(IReadOnlyList<AbsolutePath> paths)
        {
            return m_oldPathTable == null ? new List<AbsolutePath>(paths) : new List<AbsolutePath>(paths.Select(Remap));
        }

        /// <nodoc />
        public FileArtifact Remap(FileArtifact file)
        {
            return m_oldPathTable == null || !file.IsValid ? file : FileArtifact.CreateSourceFile(Remap(file.Path));
        }
    }
}
