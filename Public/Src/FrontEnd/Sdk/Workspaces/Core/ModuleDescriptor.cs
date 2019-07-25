// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// The identifier of a module.
    /// </summary>
    [DebuggerDisplay("ModuleDescriptor = {ToString(), nq}")]
    public readonly struct ModuleDescriptor : IEquatable<ModuleDescriptor>
    {
        /// <nodoc/>
        public ModuleId Id { get; }

        /// <nodoc/>
        [NotNull]
        public string Name { get; }

        /// <nodoc/>
        [NotNull]
        public string DisplayName { get; }

        /// <summary>
        /// Module version field (optionally specified).
        /// </summary>
        /// <remarks>
        /// If the module does not have a version, this will be string.Empty
        /// </remarks>
        [NotNull]
        public string Version { get; }

        /// <summary>
        /// The resolver kind, this is the kind field of a resolver i.e. 'SourceResolver', 'Nuget' or 'MsBuild'
        /// </summary>
        /// <remarks>
        /// This is deliberately a string type and not an enumeration to allow for flexibility and pluggability
        /// </remarks>
        public string ResolverKind { get; }

        /// <summary>
        /// The resolver name, this is a unique name of the resolver that owns this module.
        /// </summary>
        public string ResolverName { get; }

        /// <summary>
        /// Returns true if the current module is a special module created by the default source.
        /// </summary>
        public bool IsSpecialConfigModule() => Name == Names.ConfigAsPackageName || Name == Names.ConfigModuleName;

        /// <nodoc/>
        public ModuleDescriptor(ModuleId id, string name, string displayName, string version, string resolverKind, string resolverName)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(!string.IsNullOrEmpty(resolverKind));

            Name = name;
            DisplayName = displayName ?? name;
            Id = id;
            Version = version ?? string.Empty;
            ResolverKind = resolverKind;
            ResolverName = resolverName;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
            {
                return false;
            }

            return GetType() == obj.GetType() && Equals((ModuleDescriptor)obj);
        }

        /// <nodoc/>
        public bool Equals(ModuleDescriptor other)
        {
            return string.Equals(other.Name, Name) && string.Equals(other.DisplayName, DisplayName) && string.Equals(other.Version, Version) && Id.Equals(other.Id);
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Id.GetHashCode(), DisplayName.GetHashCode(), Name.GetHashCode(), Version.GetHashCode());
        }

        /// <nodoc/>
        public static bool operator ==(ModuleDescriptor left, ModuleDescriptor right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(ModuleDescriptor left, ModuleDescriptor right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            string baseDescriptor = I($"[{Id}]{Name}");

            return string.IsNullOrEmpty(Version) ?
                baseDescriptor :
                I($"{baseDescriptor},{Version}");
        }

        /// <summary>
        /// Creates a module from a module name and optionally a version using an id that it is guaranteed to be unique.
        /// </summary>
        public static ModuleDescriptor CreateWithUniqueId(StringTable table, string moduleName, IWorkspaceModuleResolver resolver, string version = null)
        {
            return new ModuleDescriptor(
                id: ModuleId.Create(table, moduleName, version),
                name: moduleName,
                displayName: moduleName,
                version: version, 
                resolverKind: resolver.Kind,
                resolverName: resolver.Name);
        }

        /// <summary>
        /// Creates a new module descriptor based on their name and optionally a version.
        /// </summary>
        /// <remarks>
        /// The module id is computed based on the module name hash code, so the id may be duplicated if module names are duplicated.
        /// This is used for testing purposes, so the module id can be predicted only by its name.
        /// </remarks>
        public static ModuleDescriptor CreateForTesting(string moduleName, string version = null, string resolverName = null)
        {
            var id = ModuleId.UnsafeCreate(moduleName.GetHashCode());
            return new ModuleDescriptor(
                id: id,
                name: moduleName,
                displayName: moduleName,
                version: version, 
                resolverKind: KnownResolverKind.DScriptResolverKind, 
                resolverName: resolverName ?? "DScriptTestModule");
        }
    }
}
