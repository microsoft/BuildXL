// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Represents a module name and whether its project references are implicit
    /// </summary>
    /// <remarks>
    /// This struct is used by the checker host to communicate modules to the checker without having to expose
    /// workspace-specific structures.
    /// </remarks>
    public readonly struct ModuleName : IEquatable<ModuleName>
    {
        /// <summary>
        /// An invalid ScriptModuleName
        /// </summary>
        public static readonly ModuleName Invalid = default(ModuleName);

        /// <nodoc />
        public ModuleName(string name, bool projectReferencesAreImplicit)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));

            Name = name;
            ProjectReferencesAreImplicit = projectReferencesAreImplicit;
        }

        /// <nodoc />
        public string Name { get; }

        /// <nodoc />
        public bool ProjectReferencesAreImplicit { get; }

        /// <inheritdoc />
        public bool Equals(ModuleName other)
        {
            return string.Equals(Name, other.Name) && ProjectReferencesAreImplicit == other.ProjectReferencesAreImplicit;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is ModuleName && Equals((ModuleName)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Name.GetHashCode() * 397) ^ ProjectReferencesAreImplicit.GetHashCode();
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }

        /// <nodoc />
        public static bool operator ==(ModuleName left, ModuleName right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ModuleName left, ModuleName right)
        {
            return !left.Equals(right);
        }
    }
}
