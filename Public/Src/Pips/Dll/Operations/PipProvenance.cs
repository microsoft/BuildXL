// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Class containing pip provenance.
    /// </summary>
    /// <remarks>
    /// The provenance data are important for tracing and error logging.
    /// </remarks>
    public sealed class PipProvenance
    {
        /// <summary>
        /// A string supplied by the tool definition that describes the particular details of this pip.
        /// </summary>
        public readonly PipData Usage;

        /// <summary>
        /// Parse token.
        /// </summary>
        /// <remarks>
        /// 226507: This is now double-encoded in the graph and can be removed.
        /// </remarks>
        public readonly LocationData Token;

        /// <summary>
        /// Name of the output value produced by the transformer that generated this pip.
        /// </summary>
        /// <remarks>
        /// 226507: This is now double-encoded in the graph and can be removed.
        /// </remarks>
        public readonly FullSymbol OutputValueSymbol;

        /// <summary>
        /// The qualifier id
        /// </summary>
        /// <remarks>
        /// 226507: This is now double-encoded in the graph and can be removed.
        /// </remarks>
        public readonly QualifierId QualifierId;

        /// <summary>
        /// The module id
        /// </summary>
        public readonly ModuleId ModuleId;

        /// <summary>
        /// The module name
        /// </summary>
        public readonly StringId ModuleName;

        /// <summary>
        /// Identifier of this pip that is stable across BuildXL runs with an identical schedule
        /// </summary>
        /// <remarks>
        /// This identifier is not necessarily unique, but should be quite unique in practice.
        /// </remarks>
        public readonly long SemiStableHash;

        /// <summary>
        /// Class constructor.
        /// </summary>
        public PipProvenance(
            long semiStableHash,
            ModuleId moduleId,
            StringId moduleName,
            FullSymbol outputValueSymbol,
            LocationData token,
            QualifierId qualifierId,
            PipData usage)
        {
            SemiStableHash = semiStableHash;
            ModuleId = moduleId;
            ModuleName = moduleName;
            OutputValueSymbol = outputValueSymbol;
            Token = token;
            QualifierId = qualifierId;
            Usage = usage;
        }

        /// <summary>
        /// Clones the <see cref="PipProvenance"/> with only the <see cref="SemiStableHash"/> changed by salting with the given value
        /// </summary>
        public PipProvenance CloneWithSaltedSemiStableHash(long semistableHashSalt)
        {
            return new PipProvenance(
                HashCodeHelper.Combine(SemiStableHash, semistableHashSalt),
                ModuleId,
                ModuleName,
                OutputValueSymbol,
                Token,
                QualifierId,
                Usage);
        }

        /// <summary>
        /// Dummy provenance.
        /// </summary>
        /// <remarks>
        /// TODO: Remove when it becomes obsolete
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public static PipProvenance CreateDummy(PipExecutionContext context)
        {
            Contract.Requires(context != null);
            Contract.Requires(context.StringTable != null);
            Contract.Ensures(Contract.Result<PipProvenance>() != null);

            return new PipProvenance(
                new System.Random().Next(),
                ModuleId.Invalid,
                StringId.Invalid,
                FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "<Unknown Pip>")),
                LocationData.Invalid,
                QualifierId.Unqualified,
                PipData.Invalid);
        }

        #region Serialization

        internal void Serialize(PipWriter pipWriter)
        {
            Contract.Requires(pipWriter != null);

            pipWriter.Write(SemiStableHash);
            pipWriter.Write(ModuleId);
            pipWriter.Write(ModuleName);
            pipWriter.Write(OutputValueSymbol);
            pipWriter.Write(Token);
            pipWriter.WriteCompact(QualifierId.Id);
            pipWriter.Write(Usage);
        }

        internal static PipProvenance Deserialize(PipReader reader)
        {
            Contract.Requires(reader != null);
            Contract.Ensures(Contract.Result<PipProvenance>() != null);

            long semiStableHash = reader.ReadInt64();
            ModuleId moduleId = reader.ReadModuleId();
            StringId moduleName = reader.ReadStringId();
            FullSymbol outputValueName = reader.ReadFullSymbol();
            LocationData token = reader.ReadLocationData();
            QualifierId qualifierId = new QualifierId(reader.ReadInt32Compact());
            PipData usage = reader.ReadPipData();

            return new PipProvenance(
                semiStableHash,
                moduleId,
                moduleName,
                outputValueName,
                token,
                qualifierId,
                usage);
        }

        #endregion
    }
}
