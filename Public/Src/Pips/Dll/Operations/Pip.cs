// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Text;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Primitive Indivisible Processing, representing the smallest schedulable unit of work.
    /// </summary>
    /// <remarks>
    /// There are only few subtypes: <code>CopyFile</code>, <code>WriteFile</code>, <code>Process</code>, <code>HashSourceFile</code>, <code>SealDirectory</code>.
    /// TODO: Introduce some intermediate abstract classes, reflecting that only some pips have a provenance, and that only some pips have tags, and that only some pips have descriptions.
    /// A <code>Pip</code> is strictly immutable (except for its <code>PipId</code> which is set once). All mutable information is held by <code>MutablePipState</code>.
    /// </remarks>
    public abstract class Pip
    {
        /// <summary>
        /// The prefix to use when reporting the semistable hash.
        /// </summary>
        public const string SemiStableHashPrefix = "Pip";

        private PipId m_pipId;

        /// <summary>
        /// Tags used to enable pip-level filtering of the schedule.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public abstract ReadOnlyArray<StringId> Tags { get; }

        /// <summary>
        /// Pip provenance.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public abstract PipProvenance Provenance { get; }

        /// <summary>
        /// Exposes the type of the pip.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public abstract PipType PipType { get; }

        /// <nodoc />
        internal Pip()
        {
        }

        /// <summary>
        /// Unique Pip Id assigned when Pip is added to table
        /// </summary>
        /// <remarks>
        /// This property can be set only once.
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public PipId PipId
        {
            get
            {
                return m_pipId;
            }

            internal set
            {
                Contract.Requires(value.IsValid);
                Contract.Requires(!PipId.IsValid);
                m_pipId = value;
            }
        }

        /// <summary>
        /// Identifier of this pip that is stable across BuildXL runs with an identical schedule
        /// </summary>
        /// <remarks>
        /// This identifier is not necessarily unique, but should be quite unique in practice.
        /// </remarks>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public long SemiStableHash => Provenance?.SemiStableHash ?? 0;

        /// <summary>
        /// A SemiStableHash formatted as a string for display
        /// </summary>
        public string FormattedSemiStableHash => FormatSemiStableHash(SemiStableHash);

        /// <summary>
        /// Format the semistable hash for display 
        /// </summary>
        public static string FormatSemiStableHash(long hash) => string.Format(CultureInfo.InvariantCulture, "{0}{1:X16}", SemiStableHashPrefix, hash);

        /// <summary>
        /// Whether this is a process pip that <see cref="Process.AllowUndeclaredSourceReads"/>
        /// </summary>
        public bool ProcessAllowsUndeclaredSourceReads => this is Process process && process.AllowUndeclaredSourceReads;
        
        /// <summary>
        /// Resets pip id.
        /// </summary>
        /// <remarks>
        /// This method should only be used for graph patching and in unit tests.
        /// </remarks>
        public void ResetPipIdForTesting()
        {
            m_pipId = PipId.Invalid;
        }

        #region Serialization
        internal static Pip Deserialize(PipReader reader)
        {
            Contract.Requires(reader != null);
            Contract.Ensures(Contract.Result<Pip>() != null);
            var b = reader.ReadByte();
            Contract.Assert(b <= (int)PipType.SealDirectory);
            var pipType = (PipType)b;
            Pip pip;
            switch (pipType)
            {
                case PipType.CopyFile:
                    reader.Start<CopyFile>();
                    pip = CopyFile.InternalDeserialize(reader);
                    break;
                case PipType.HashSourceFile:
                    reader.Start<HashSourceFile>();
                    pip = HashSourceFile.InternalDeserialize(reader);
                    break;
                case PipType.Process:
                    reader.Start<Process>();
                    pip = Process.InternalDeserialize(reader);
                    break;
                case PipType.Ipc:
                    reader.Start<IpcPip>();
                    pip = IpcPip.InternalDeserialize(reader);
                    break;
                case PipType.SealDirectory:
                    reader.Start<SealDirectory>();
                    pip = SealDirectory.InternalDeserialize(reader);
                    break;
                case PipType.Value:
                    reader.Start<ValuePip>();
                    pip = ValuePip.InternalDeserialize(reader);
                    break;
                case PipType.SpecFile:
                    reader.Start<SpecFilePip>();
                    pip = SpecFilePip.InternalDeserialize(reader);
                    break;
                case PipType.Module:
                    reader.Start<ModulePip>();
                    pip = ModulePip.InternalDeserialize(reader);
                    break;
                default:
                    Contract.Assert(pipType == PipType.WriteFile);
                    reader.Start<WriteFile>();
                    pip = WriteFile.InternalDeserialize(reader);
                    break;
            }

            reader.End();
            Contract.Assume(pip != null);
            return pip;
        }

        internal void Serialize(PipWriter writer)
        {
            Contract.Requires(writer != null);
            writer.Write((byte)PipType);
            writer.Start(GetType());
            InternalSerialize(writer);
            writer.End();
        }

        internal abstract void InternalSerialize(PipWriter writer);
        #endregion
       
        /// <summary>
        /// Gets a friendly description of the pip.
        /// </summary>
        /// <remarks>
        /// By no means is this a unique identifier for this instance. It is merely for UI, reporting and light-weight debugging
        /// purposes. This value may be empty, but is non-null.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily", Justification = "The code would be too ugly or way more casts would happen if we always cast to process in each invocation.")]
        public string GetDescription(PipExecutionContext context)
        {
            Contract.Requires(context != null);
            Contract.Ensures(Contract.Result<string>() != null);

            var stringTable = context.StringTable;
            var pathTable = context.PathTable;

            var p = Provenance;

            using (PooledObjectWrapper<StringBuilder> wrapper = Pools.StringBuilderPool.GetInstance())
            {
                StringBuilder sb = wrapper.Instance;

                if (p == null)
                {
                    sb.Append("Pip<unknown>");
                }
                else
                {
                    sb.Append(FormattedSemiStableHash);

                    if (PipType == PipType.Process)
                    {
                        var process = (Process)this;
                        sb.Append(", ");
                        sb.Append(process.GetToolName(pathTable).ToString(stringTable));

                        if (process.ToolDescription.IsValid)
                        {
                            sb.Append(" (");
                            sb.Append(stringTable.GetString(process.ToolDescription));
                            sb.Append(')');
                        }
                    }
                }

                switch (PipType)
                {
                    case PipType.CopyFile:
                        sb.Append(", <COPYFILE>");
                        break;
                    case PipType.WriteFile:
                        sb.Append(", <WRITEFILE>");
                        break;
                    case PipType.Ipc:
                        sb.Append(", <IPC>");
                        break;
                    case PipType.HashSourceFile:
                        sb.Append(", <HASHFILE>");
                        break;
                    case PipType.SealDirectory:
                        sb.Append(", <SEALDIRECTORY>");
                        break;
                    case PipType.Value:
                        sb.Append(", <VALUE>");
                        break;
                    case PipType.SpecFile:
                        sb.Append(", <SPECFILE>");
                        break;
                    case PipType.Module:
                        sb.Append(", <MODULE>");
                        break;
                }

                if (p != null)
                {
                    if (p.ModuleName.IsValid)
                    {
                        sb.Append(", ");
                        sb.Append(p.ModuleName.ToString(stringTable));
                    }

                    if (context.SymbolTable != null)
                    {
                        if (p.OutputValueSymbol.IsValid)
                        {
                            sb.Append(", ");
                            sb.Append(p.OutputValueSymbol, context.SymbolTable);
                        }
                    }

                    if (p.QualifierId.IsValid)
                    {
                        sb.Append(", ");
                        sb.Append(context.QualifierTable.GetCanonicalDisplayString(p.QualifierId));
                    }
                }

                switch (PipType)
                {
                    case PipType.CopyFile:
                        var copyFilePip = this as CopyFile;
                        sb.Append(", ");
                        sb.Append(copyFilePip.Source.Path.ToString(pathTable));
                        sb.Append(", => ");
                        sb.Append(stringTable.GetString(pathTable.GetFinalComponent(copyFilePip.Destination.Path.Value)));
                        break;
                    case PipType.WriteFile:
                        var write = this as WriteFile;
                        sb.Append(", => ");
                        sb.Append(stringTable.GetString(pathTable.GetFinalComponent(write.Destination.Path.Value)));
                        break;
                    case PipType.Ipc:
                        var ipcPip = this as IpcPip;
                        sb.Append(", moniker id '").Append(ipcPip.IpcInfo.IpcMonikerId.ToString(pathTable.StringTable));
                        sb.Append("', config ").Append(ipcPip.IpcInfo.IpcClientConfig.ToJson());
                        sb.Append(", => ");
                        sb.Append(stringTable.GetString(pathTable.GetFinalComponent(ipcPip.OutputFile.Path.Value)));
                        break;
                    case PipType.HashSourceFile:
                        var hash = this as HashSourceFile;
                        sb.Append(", => ");
                        sb.Append(stringTable.GetString(pathTable.GetFinalComponent(hash.Artifact.Path.Value)));
                        break;
                    case PipType.SealDirectory:
                        var seal = this as SealDirectory;
                        sb.Append(", => ");

                        // Note that we cannot access seal.Directory here without assuming that it is fully initialized (IsInitialized)
                        // DirectoryRoot, however, is available beforehand.
                        sb.Append(stringTable.GetString(pathTable.GetFinalComponent(seal.DirectoryRoot.Value)));
                        sb.AppendFormat("({0} entries)", seal.Contents.Length);
                        break;
                    case PipType.Process:
                        var process = this as Process;
                        var semaphores = process.Semaphores;
                        if (semaphores.Length > 0)
                        {
                            sb.Append(", acquires semaphores (");
                            for (int i = 0; i < semaphores.Length; i++)
                            {
                                var s = semaphores[i];
                                sb.Append(i == 0 ? string.Empty : ", ");
                                sb.Append(stringTable.GetString(s.Name));
                                sb.Append(':');
                                sb.Append(s.Value);
                            }

                            sb.Append(")");
                        }

                        break;
                    case PipType.Value:
                        var valuePip = this as ValuePip;
                        sb.Append(", ");
                        sb.Append(valuePip.Symbol, context.SymbolTable);
                        sb.Append(" @ ");
                        sb.Append(valuePip.Qualifier.Id.ToString(CultureInfo.InvariantCulture));
                        break;
                    case PipType.SpecFile:
                        var specFilePip = this as SpecFilePip;
                        sb.Append(", ");
                        sb.Append(specFilePip.SpecFile.Path.ToString(pathTable));
                        break;
                    case PipType.Module:
                        var modulePip = this as ModulePip;
                        sb.Append(", ");
                        sb.Append(modulePip.Identity.ToString(stringTable));
                        break;
                }

                if (p != null)
                {
                    if (p.Usage.IsValid)
                    {
                        // custom pip description supplied by a customer
                        sb.Append(BuildXL.Utilities.Tracing.FormattingEventListener.CustomPipDescriptionMarker);
                        sb.Append(p.Usage.ToString(pathTable));
                    }
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Gets a short description of this pip
        /// </summary>
        public string GetShortDescription(PipExecutionContext context)
        {
            if(Provenance == null)
            {
                return "";
            }

            if (Provenance.Usage.IsValid)
            {
                return Provenance.Usage.ToString(context.PathTable);
            }

            var moduleName = Provenance.ModuleName.ToString(context.StringTable);
            var valueName = Provenance.OutputValueSymbol.ToString(context.SymbolTable);

            var toolName = string.Empty;
            if (this is Process process)
            {
                toolName = " - " + process.GetToolName(context.PathTable).ToString(context.StringTable);
            }

            var qualifierName = context.QualifierTable.GetFriendlyUserString(Provenance.QualifierId);

            return $"{moduleName} - {valueName}{toolName} [{qualifierName}]";
        }
    }
}
