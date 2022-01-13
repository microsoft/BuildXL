// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

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
        public AbsolutePath? Remap(AbsolutePath? path)
        {
            return path.HasValue ? (AbsolutePath?)Remap(path.Value) : null;
        }

        /// <nodoc />
        public List<AbsolutePath> Remap(IReadOnlyList<AbsolutePath> paths)
        {
            return m_oldPathTable == null ? new List<AbsolutePath>(paths) : new List<AbsolutePath>(paths.Select(Remap));
        }

        /// <nodoc />
        public List<DirectoryArtifact> Remap(IReadOnlyList<DirectoryArtifact> paths)
        {
            return m_oldPathTable == null ? new List<DirectoryArtifact>(paths) : new List<DirectoryArtifact>(paths.Select(Remap));
        }

        /// <nodoc />
        public FileArtifact Remap(FileArtifact file)
        {
            return m_oldPathTable == null || !file.IsValid ? file : FileArtifact.CreateSourceFile(Remap(file.Path));
        }

        /// <nodoc />
        public DirectoryArtifact Remap(DirectoryArtifact directory)
        {
            return m_oldPathTable == null || !directory.IsValid ? directory : new DirectoryArtifact(Remap(directory.Path), directory.PartialSealId, directory.IsSharedOpaque);
        }

        /// <nodoc />
        public FileArtifact? Remap(FileArtifact? file)
        {
            return file.HasValue ? (FileArtifact?)Remap(file.Value) : null;
        }

        /// <nodoc />
        public DiscriminatingUnion<FileArtifact, PathAtom> Remap(DiscriminatingUnion<FileArtifact, PathAtom> fileUnion)
        {
            DiscriminatingUnion<FileArtifact, PathAtom> remappedPath = null;

            if (fileUnion != null)
            {
                var fileValue = fileUnion.GetValue();
                remappedPath = new DiscriminatingUnion<FileArtifact, PathAtom>();

                if (fileValue is FileArtifact file)
                {
                    remappedPath.SetValue(Remap(file));
                }
                else if (fileValue is PathAtom pathAtom)
                {
                    remappedPath.SetValue(Remap(pathAtom));
                }
            }

            return remappedPath;
        }

        /// <nodoc />
        public DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>> Remap(DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>> fileUnion)
        {
            DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>> remappedPath = null;

            if (fileUnion != null)
            {
                var fileValue = fileUnion.GetValue();
                remappedPath = new DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>>();

                if (fileValue is FileArtifact file)
                {
                    remappedPath.SetValue(Remap(file));
                }
                else if (fileValue is IReadOnlyList<DirectoryArtifact> searchDirectories)
                {
                    remappedPath.SetValue(Remap(searchDirectories));
                }
            }

            return remappedPath;
        }
    }
}
