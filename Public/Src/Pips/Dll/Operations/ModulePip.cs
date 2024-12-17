// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using System.Collections.Generic;

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
        /// Directories to scrub for this module
        /// </summary>
        public IReadOnlyList<AbsolutePath> ScrubDirectories { get; }

        /// <summary>
        /// Constructs a new module pip
        /// </summary>
        public ModulePip(ModuleId module, StringId identity, StringId version, LocationData location, StringId resolverKind, StringId resolverName, IReadOnlyList<AbsolutePath> scrubDirectories)
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
            ScrubDirectories = scrubDirectories;
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
        public static ModulePip CreateForTesting(StringTable stringTable, AbsolutePath specPath, ModuleId? moduleId = null, StringId? moduleName = null, IReadOnlyList<AbsolutePath> scrubDirectories = null)
        {
            moduleName = moduleName ?? StringId.Create(stringTable, "TestModule");
            return new ModulePip(
                module: moduleId ?? ModuleId.Create(moduleName.Value),
                identity: moduleName.Value,
                version: StringId.Invalid,
                location: new LocationData(specPath, 0, 0),
                resolverKind: StringId.Create(stringTable, "TestResolver"),
                resolverName: StringId.Create(stringTable, "TestResolver"),
                scrubDirectories: scrubDirectories
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
            writer.WriteReadOnlyList(ScrubDirectories ?? new List<AbsolutePath>(), (writer, path) => writer.Write(path));
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
                resolverName: reader.ReadStringId(),
                scrubDirectories: reader.ReadReadOnlyList((reader) => reader.ReadAbsolutePath())
            );
        }
        #endregion
    }
}
