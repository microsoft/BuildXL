// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Package representation.
    /// </summary>
    [DebuggerDisplay("{Descriptor.Name}")]
    public sealed class Package
    {
        /// <summary>
        /// Projects owned by the package that are already parsed.
        /// </summary>
        private readonly HashSet<AbsolutePath> m_parsedProjects;

        /// <summary>
        /// Package id.
        /// TODO: unify with ModuleId
        /// </summary>
        public PackageId Id { get; }

        /// <summary>
        /// A map to the module ID in the ModuleTable. The table is used to create a ModulePip
        /// in the pip graph for this package.
        /// TODO: Remove that and keep a reference to the module Pip instead.
        /// </summary>
        public ModuleId ModuleId { get; }

        /// <summary>
        /// Package location.
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        /// Projects owned by the package.
        /// </summary>
        /// <remarks>
        /// Can be null if there is no explicit projects listed in the package configuration.
        /// </remarks>
        [CanBeNull]
        public HashSet<AbsolutePath> DescriptorProjects { get; }

        /// <summary>
        /// Get all parsed projects
        /// </summary>
        /// <remarks>
        /// This property is called when the object construction is finished. So no synchronization is needed.
        /// </remarks>
        [CanBeNull]
        public IEnumerable<AbsolutePath> ParsedProjects => m_parsedProjects;

        /// <summary>
        /// Package descriptor.
        /// </summary>
        public IPackageDescriptor Descriptor { get; }

        /// <nodoc />
        private Package(PackageId id, AbsolutePath path, IPackageDescriptor descriptor, IEnumerable<AbsolutePath> parsedProjects, ModuleId moduleId)
        {
            Id = id;
            Path = path;
            Descriptor = descriptor;
            DescriptorProjects = descriptor?.Projects != null ? new HashSet<AbsolutePath>(descriptor.Projects) : null;
            m_parsedProjects = parsedProjects != null
                ? new HashSet<AbsolutePath>(parsedProjects)
                : new HashSet<AbsolutePath>();
            ModuleId = moduleId;
        }

        /// <summary>
        /// Creates a package.
        /// </summary>
        public static Package Create(PackageId id, AbsolutePath path, IPackageDescriptor descriptor, IEnumerable<AbsolutePath> parsedProjects = null, ModuleId moduleId = default)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(path.IsValid);
            Contract.Requires(descriptor != null);

            return new Package(id, path, descriptor, parsedProjects, moduleId.IsValid ? moduleId : ModuleId.Create(id.Name));
        }

        /// <summary>
        /// Add a parsed project to the collection
        /// </summary>
        public void AddParsedProject(AbsolutePath project)
        {
            // This is the only place when synchronization is needed.
            lock (m_parsedProjects)
            {
                m_parsedProjects.Add(project);
            }
        }
    }
}
