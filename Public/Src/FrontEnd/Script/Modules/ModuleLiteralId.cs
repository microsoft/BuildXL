// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Sdk;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Module id.
    /// </summary>
    public readonly struct ModuleLiteralId : IEquatable<ModuleLiteralId>
    {
        /// <summary>
        /// Path to a file that represents a module.
        /// </summary>
        /// <remarks>
        /// The path value is valid if the module is specified by a path.
        /// </remarks>
        public AbsolutePath Path { get; }

        /// <summary>
        /// Module name.
        /// </summary>
        /// <remarks>
        /// The name is valid iff this module is a namespace or internal module.
        /// </remarks>
        public FullSymbol Name { get; }

        /// <summary>
        /// Invalid module id.
        /// </summary>
        public static ModuleLiteralId Invalid { get; } = default(ModuleLiteralId);

        /// <nodoc />
        private ModuleLiteralId(AbsolutePath path, FullSymbol name)
        {
            Contract.Requires(path.IsValid || name.IsValid);

            Path = path;
            Name = name;
        }

        /// <summary>
        /// Creates a module id, given a path.
        /// </summary>
        public static ModuleLiteralId Create(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return new ModuleLiteralId(path, FullSymbol.Invalid);
        }

        /// <summary>
        /// Creates a module id, given an atomic name.
        /// </summary>
        public static ModuleLiteralId Create(FullSymbol name)
        {
            Contract.Requires(name.IsValid);
            return new ModuleLiteralId(AbsolutePath.Invalid, name);
        }

        /// <summary>
        /// Creates a module id by deriving from another module id.
        /// </summary>
        /// <remarks>
        /// If the other module id has the form (Path, Package, Name), and
        /// the given name is M.N.O, then the resulting module id is (Path, Package, M.N.O).
        /// </remarks>
        [Pure]
        public ModuleLiteralId WithName(FullSymbol name)
        {
            Contract.Requires(name != null);
            return new ModuleLiteralId(Path, name);
        }

        /// <summary>
        /// Checks if this package id is valid.
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <inheritdoc />
        public bool Equals(ModuleLiteralId other)
        {
            return Path == other.Path && Name == other.Name && Name == other.Name;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Path.GetHashCode(), Name.GetHashCode());
        }

        /// <inheritdoc />
        public override string ToString()
        {
#if DEBUG
            string path = Path.IsValid ? (FrontEndContext.DebugContext != null ? Path.ToString(FrontEndContext.DebugContext.PathTable) : Path.ToString()) : string.Empty;
            string name = Name.IsValid ? (FrontEndContext.DebugContext != null ? Name.ToString(FrontEndContext.DebugContext.SymbolTable) : Name.ToString()) : string.Empty;
#else
            string path = Path.IsValid ? Path.ToString() : string.Empty;
            string name = Name.IsValid ? Name.ToString() : string.Empty;
#endif
            return I($"({path}, {name})");
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ModuleLiteralId left, ModuleLiteralId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ModuleLiteralId left, ModuleLiteralId right)
        {
            return !left.Equals(right);
        }
    }
}
