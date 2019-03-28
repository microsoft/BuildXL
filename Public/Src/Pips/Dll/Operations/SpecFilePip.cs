// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A Pip that represents a spec file
    /// </summary>
    public class SpecFilePip : Pip
    {
        /// <summary>
        /// Returns the spec file
        /// </summary>
        public FileArtifact SpecFile { get; }

        /// <summary>
        /// Returns the location where the spec file is defined
        /// </summary>
        public LocationData DefinitionLocation { get; }

        /// <summary>
        /// Returns the moduleId that this spec file is a part of.
        /// </summary>
        public ModuleId OwningModule { get; }

        /// <summary>
        /// Constructs a new spec file pip
        /// </summary>
        public SpecFilePip(
            FileArtifact specFile,
            LocationData definitionLocation,
            ModuleId owningModule)
        {
            Contract.Requires(specFile.IsValid);

            SpecFile = specFile;
            DefinitionLocation = definitionLocation;
            OwningModule = owningModule;
        }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags => default(ReadOnlyArray<StringId>);

        /// <inheritdoc />
        public override PipProvenance Provenance => null;

        /// <inheritdoc />
        public override PipType PipType => PipType.SpecFile;

        #region Serialziation

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(SpecFile);
            writer.Write(DefinitionLocation);
            writer.Write(OwningModule);
        }

        /// <summary>
        /// Deserialize
        /// </summary>
        internal static SpecFilePip InternalDeserialize(PipReader reader)
        {
            return new SpecFilePip(
                specFile: reader.ReadFileArtifact(),
                definitionLocation: reader.ReadLocationData(),
                owningModule: reader.ReadModuleId());
        }
        #endregion
    }
}
