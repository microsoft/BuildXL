// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A Pip that represents a Module
    /// </summary>
    public class ModulePip : Pip
    {
        /// <summary>
        /// Returns the module id.
        /// </summary>
        public ModuleId Module { get; }

        /// <summary>
        /// Returns the module identity.
        /// </summary>
        public StringId Identity { get; }

        /// <summary>
        /// Returns the kind of resolver that instantiated this module.
        /// </summary>
        public StringId ResolverKind { get; }

        /// <summary>
        /// Returns the name of the resolver that instantiated this module.
        /// </summary>
        public StringId ResolverName { get; }

        /// <summary>
        /// Returns the version of the module.
        /// </summary>
        /// <remarks>
        /// This field is optional and StringId.Invalid is a legal value.
        /// </remarks>
        public StringId Version { get; }

        /// <summary>
        /// Returns the module location.
        /// </summary>
        public LocationData Location { get; }

        /// <summary>
        /// Constructs a new module pip
        /// </summary>
        public ModulePip(ModuleId module, StringId identity, StringId version, LocationData location, StringId resolverKind, StringId resolverName)
        {
            Contract.Requires(module.IsValid);
            Contract.Requires(identity.IsValid);
            Contract.Requires(resolverKind.IsValid);
            Contract.Requires(resolverName.IsValid);

            Module = module;
            Identity = identity;
            Version = version;
            Location = location;
            ResolverKind = resolverKind;
            ResolverName = resolverName;
        }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags => default(ReadOnlyArray<StringId>);

        /// <inheritdoc />
        public override PipProvenance Provenance => null;

        /// <inheritdoc />
        public override PipType PipType => PipType.Module;

        /// <summary>
        /// Helper method to crate a dummy test version of the modulepip.
        /// </summary>
        public static ModulePip CreateForTesting(StringTable stringTable, AbsolutePath specPath, ModuleId? moduleId = null)
        {
            var moduleName = StringId.Create(stringTable, "TestModule");
            return new ModulePip(
                module: moduleId ?? ModuleId.Create(moduleName),
                identity: moduleName,
                version: StringId.Invalid,
                location: new LocationData(specPath, 0, 0),
                resolverKind: StringId.Create(stringTable, "TestResolver"),
                resolverName: StringId.Create(stringTable, "TestResolver")
            );
        }

        #region Serialization

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Module);
            writer.Write(Identity);
            writer.Write(Version);
            Location.Serialize(writer);
            writer.Write(ResolverKind);
            writer.Write(ResolverName);
        }

        /// <summary>
        /// Deserialize
        /// </summary>
        internal static ModulePip InternalDeserialize(PipReader reader)
        {
            return new ModulePip(
                module: reader.ReadModuleId(),
                identity: reader.ReadStringId(),
                version: reader.ReadStringId(),
                location: LocationData.Deserialize(reader),
                resolverKind: reader.ReadStringId(),
                resolverName: reader.ReadStringId()
            );
        }
        #endregion
    }
}
