// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A module name reference with the information of where it was referenced from
    /// </summary>
    public readonly struct ModuleReferenceWithProvenance : IEquatable<ModuleReferenceWithProvenance>
    {
        private readonly LineInfo m_referencedFromLineInfo;
        private readonly string m_referencedFromPath;

        /// <nodoc/>
        public string Name { get; }

        /// <nodoc/>
        public Location ReferencedFrom
            => new Location { Line = m_referencedFromLineInfo.Line, Position = m_referencedFromLineInfo.Position, File = m_referencedFromPath };

        /// <nodoc/>
        public ModuleReferenceWithProvenance(string moduleName, LineInfo referencedFromLineInfo, string referencedFromPath)
        {
            Contract.Requires(!string.IsNullOrEmpty(moduleName));

            Name = moduleName;
            m_referencedFromLineInfo = referencedFromLineInfo;
            m_referencedFromPath = referencedFromPath;
        }

        /// <nodoc />
        public static ModuleReferenceWithProvenance FromName(string moduleName)
        {
            return new ModuleReferenceWithProvenance(moduleName, default(LineInfo), string.Empty);
        }

        /// <nodoc />
        public static ModuleReferenceWithProvenance FromNameAndPath(string moduleName, string path)
        {
            return new ModuleReferenceWithProvenance(moduleName, default(LineInfo), path);
        }

        /// <inheritdoc/>
        public bool Equals(ModuleReferenceWithProvenance other)
        {
            return string.Equals(Name, other.Name)
                && m_referencedFromLineInfo.Equals(other.m_referencedFromLineInfo)
                && m_referencedFromPath.Equals(other.m_referencedFromPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is ModuleReferenceWithProvenance && Equals((ModuleReferenceWithProvenance)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Name.GetHashCode(), m_referencedFromLineInfo.GetHashCode(), m_referencedFromPath.GetHashCode());
        }

        /// <nodoc/>
        public static bool operator ==(ModuleReferenceWithProvenance left, ModuleReferenceWithProvenance right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(ModuleReferenceWithProvenance left, ModuleReferenceWithProvenance right)
        {
            return !left.Equals(right);
        }
    }
}
