// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Threading;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Pips
{
    /// <summary>
    /// Class with helper functions for creating pip
    /// </summary>
    public sealed class PipConstructionHelper
    {
        /// <summary>
        /// The current semistable hash to hand out for pips
        /// </summary>
        private long m_semiStableHash;

        private ReserveFoldersResolver m_folderIdResolver;

        private readonly AbsolutePath m_objectRoot;

        private readonly AbsolutePath m_redirectedRoot;

        private readonly AbsolutePath m_tempRoot;

        /// <nodoc />
        private PipExecutionContext Context { get; }

        /// <summary>
        /// The graph
        /// </summary>
        public IPipGraph PipGraph { get; }

        /// <summary>
        /// A unique relative path for this value pip
        /// </summary>
        public RelativePath PipRelativePath { get; }

        /// <summary>
        /// The value pip for the current Thunk
        /// </summary>
        private readonly ValuePip m_valuePip;

        /// <summary>
        /// Id of the module for the current Thunk
        /// </summary>
        private readonly ModuleId m_moduleId;

        /// <summary>
        /// Name of the module for the current Thunk
        /// </summary>
        private readonly string m_moduleName;

        /// <summary>
        /// Singleton to get empty list of files for sealing directories
        /// </summary>
        public static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> EmptySealContents =
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(new FileArtifact[0], OrdinalFileArtifactComparer.Instance);

        /// <summary>
        /// Singleton to get empty list of files for static directories
        /// </summary>
        public static readonly SortedReadOnlyArray<FileArtifact, OrdinalPathOnlyFileArtifactComparer> EmptyStaticDirContents =
            SortedReadOnlyArray<FileArtifact, OrdinalPathOnlyFileArtifactComparer>.CloneAndSort(new FileArtifact[0], OrdinalPathOnlyFileArtifactComparer.Instance);

        /// <nodoc />
        private PipConstructionHelper(
            PipExecutionContext context,
            AbsolutePath objectRoot,
            AbsolutePath redirectedRoot,
            AbsolutePath tempRoot,
            IPipGraph pipGraph,
            ModuleId moduleId,
            string moduleName,
            ValuePip valuePip,
            RelativePath pipRelativePath,
            long semiStableHashSeed)
        {
            Context = context;
            m_objectRoot = objectRoot;
            m_redirectedRoot = redirectedRoot;
            m_tempRoot = tempRoot.IsValid ? tempRoot : objectRoot;
            PipGraph = pipGraph;
            m_moduleId = moduleId;
            m_moduleName = moduleName;
            m_valuePip = valuePip;
            PipRelativePath = pipRelativePath;
            
            m_semiStableHash = semiStableHashSeed;
            m_folderIdResolver = new ReserveFoldersResolver(this);
        }

        /// <summary>
        /// Creates a new PipConstructionHelper
        /// </summary>
        /// <remarks>
        /// Ideally this function would take ModuleId, FullSymbol QualifierId and compute uniqueOutputLocation itself. Unfortunately today the data is not yet
        /// exposed via IPipGraph, therefore the responsibility is on the call site for now.
        /// </remarks>
        public static PipConstructionHelper Create(
            PipExecutionContext context,
            AbsolutePath objectRoot,
            AbsolutePath redirectedRoot,
            AbsolutePath tempRoot,
            IPipGraph pipGraph,
            ModuleId moduleId,
            string moduleName,
            RelativePath specRelativePath,
            FullSymbol symbol,
            LocationData thunkLocation,
            QualifierId qualifierId)
        {
            var stringTable = context.StringTable;
            var pathTable = context.PathTable;

            // We have to manually compute the pipPipUniqueString here, Ideally we pass PackageId, SpecFile, FullSymbol and qualiferId and have it computed inside, but the IPipGraph does not allow querying it for now.
            string hashString;
            long semiStableHashSeed = 0;
            using (var builderWrapper = Pools.GetStringBuilder())
            {
                var builder = builderWrapper.Instance;

                builder.Append(moduleName);
                builder.Append('/');
                semiStableHashSeed = HashCodeHelper.GetOrdinalHashCode64(moduleName);

                if (specRelativePath.IsValid)
                {
                    string specPath = specRelativePath.ToString(stringTable);
                    builder.Append(specPath);
                    builder.Append('/');
                    semiStableHashSeed = HashCodeHelper.Combine(semiStableHashSeed, HashCodeHelper.GetOrdinalHashCode64(specPath));
                }

                var symbolName = symbol.ToStringAsCharArray(context.SymbolTable);
                builder.Append(symbolName);
                builder.Append('/');
                semiStableHashSeed = HashCodeHelper.Combine(semiStableHashSeed, HashCodeHelper.GetOrdinalHashCode64(symbolName));

                var qualifierDisplayValue = context.QualifierTable.GetCanonicalDisplayString(qualifierId);
                builder.Append(qualifierDisplayValue);
                semiStableHashSeed = HashCodeHelper.Combine(semiStableHashSeed, HashCodeHelper.GetOrdinalHashCode64(qualifierDisplayValue));

                var pipPipUniqueString = builder.ToString();
                hashString = Hash(pipPipUniqueString);
            }

            var pipRelativePath = RelativePath.Create(
                PathAtom.Create(stringTable, hashString.Substring(0, 1)),
                PathAtom.Create(stringTable, hashString.Substring(1, 1)),
                PathAtom.Create(stringTable, hashString.Substring(2)));

            var valuePip = new ValuePip(symbol, qualifierId, thunkLocation);

            return new PipConstructionHelper(
                context,
                objectRoot,
                redirectedRoot,
                tempRoot,
                pipGraph,
                moduleId,
                moduleName,
                valuePip,
                pipRelativePath,
                semiStableHashSeed);
        }

        /// <summary>
        /// Helper method with defaults for convenient creation from unit tests
        /// </summary>
        public static PipConstructionHelper CreateForTesting(
            PipExecutionContext context,
            AbsolutePath? objectRoot = null,
            AbsolutePath? redirectedRoot = null,
            AbsolutePath? tempRoot = null,
            IPipGraph pipGraph = null,
            string moduleName = null,
            string specRelativePath = null,
            string symbol = null,
            AbsolutePath? specPath = null,
            QualifierId? qualifierId = null)
        {
            moduleName = moduleName ?? "TestModule";

            return Create(
                context,
                objectRoot ?? AbsolutePath.Create(context.PathTable, "d:\\test\\obj"),
                redirectedRoot ?? AbsolutePath.Create(context.PathTable, "d:\\test\\redirected"),
                tempRoot ?? objectRoot ?? AbsolutePath.Create(context.PathTable, "d:\\test\\tmp"),
                pipGraph,
                ModuleId.Create(context.StringTable, moduleName),
                moduleName,
                RelativePath.Create(context.StringTable, specRelativePath ?? "spec"),
                FullSymbol.Create(context.SymbolTable, symbol ?? "testValue"),
                new LocationData(specPath ?? AbsolutePath.Create(context.PathTable, "d:\\src\\spec.dsc"), 0, 0),
                qualifierId ?? QualifierId.Unqualified);
        }

        /// <nodoc />
        public bool TryCopyFile(
            FileArtifact source, 
            AbsolutePath destination,
            CopyFile.Options options,
            string[] tags, 
            string description, 
            out FileArtifact fileArtifact)
        {
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);

            fileArtifact = FileArtifact.CreateSourceFile(destination).CreateNextWrittenVersion();
            var pip = new CopyFile(
                source,
                fileArtifact,
                ToStringIds(tags),
                CreatePipProvenance(description),
                options);

            if (PipGraph != null)
            {
                return PipGraph.AddCopyFile(pip, GetValuePipId());
            }

            return true;
        }

        /// <nodoc />
        public bool TryWriteFile(
            AbsolutePath destination,
            PipData content,
            WriteFileEncoding encoding,
            string[] tags,
            string description,
            out FileArtifact fileArtifact)
        {
            Contract.Requires(destination.IsValid);
            Contract.Requires(content.IsValid);

            fileArtifact = FileArtifact.CreateSourceFile(destination).CreateNextWrittenVersion();
            var pip = new WriteFile(
                fileArtifact,
                content,
                encoding,
                ToStringIds(tags),
                CreatePipProvenance(description));

            if (PipGraph != null)
            {
                return PipGraph.AddWriteFile(pip, GetValuePipId());
            }

            return true;
        }

        /// <nodoc />
        public bool TrySealDirectory(
            AbsolutePath directoryRoot,
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents,
            SealDirectoryKind kind,
            string[] tags,
            string description,
            string[] patterns,
            out DirectoryArtifact sealedDirectory,
            bool scrub = false)
        {
            Contract.Requires(directoryRoot.IsValid);
            Contract.Requires(contents.IsValid);

            PipData usage = PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, description != null
                ? new PipDataAtom[] { description }
                : new PipDataAtom[] { "'", directoryRoot, "' [", contents.Length.ToString(CultureInfo.InvariantCulture), " files]" });

            var pip = new SealDirectory(
                directoryRoot,
                contents,
                kind,
                CreatePipProvenance(usage),
                ToStringIds(tags),
                ToStringIds(patterns),
                scrub);

            if (PipGraph != null)
            {
                sealedDirectory = PipGraph.AddSealDirectory(pip, GetValuePipId());
                if (!sealedDirectory.IsValid)
                {
                    return false;
                }
            }
            else
            {
                sealedDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(directoryRoot);
            }

            return true;
        }

        /// <nodoc/>
        public bool TryComposeSharedOpaqueDirectory(
            AbsolutePath directoryRoot,
            IReadOnlyList<DirectoryArtifact> contents,
            [CanBeNull] string description,
            string[] tags,
            out DirectoryArtifact sharedOpaqueDirectory)
        {
            Contract.Requires(directoryRoot.IsValid);
            Contract.Requires(contents != null);

            if (PipGraph == null)
            {
                sharedOpaqueDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(directoryRoot);
                return true;
            }

            PipData usage = PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, description != null
                ? new PipDataAtom[] { description }
                : new PipDataAtom[] { "'", directoryRoot, " [", contents.Count.ToString(CultureInfo.InvariantCulture), " shared opaque directories]" });

            sharedOpaqueDirectory = PipGraph.ReserveSharedOpaqueDirectory(directoryRoot);

            var pip = new CompositeSharedOpaqueSealDirectory(
                    directoryRoot,
                    contents,
                    CreatePipProvenance(usage),
                    ToStringIds(tags));

            // The seal directory is ready to be initialized, since the directory artifact has been reserved already
            pip.SetDirectoryArtifact(sharedOpaqueDirectory);

            sharedOpaqueDirectory = PipGraph.AddSealDirectory(pip, GetValuePipId());
            if (!sharedOpaqueDirectory.IsValid)
            {
                return false;
            }

            return true;
        }

        /// <nodoc />
        public bool TryAddProcess(ProcessBuilder processBuilder, out ProcessOutputs processOutputs, out Process pip)
        {
            // Applying defaults can fail if, for example, a source sealed directory cannot be 
            // created because it is not under a mount.  That error must be propagated, because
            // otherwise an error will be logged but the evaluation will succeed.
            if (PipGraph?.ApplyCurrentOsDefaults(processBuilder) == false)
            {
                pip = null;
                processOutputs = null;
                return false;
            }

            if (!processBuilder.TryFinish(this, out pip, out processOutputs))
            {
                return false;
            }

            if (PipGraph != null)
            {
                var success = PipGraph.AddProcess(pip, GetValuePipId());
                processOutputs.ProcessPipId = pip.PipId;
                return success;
            }

            return true;
        }

        /// <nodoc />
        public bool TryAddIpc(
            IpcClientInfo ipcClientInfo,
            PipData arguments,
            FileArtifact outputFile,
            ReadOnlyArray<PipId> servicePipDependencies,
            ReadOnlyArray<FileArtifact> fileDependencies,
            ReadOnlyArray<DirectoryArtifact> directoryDependencies,
            ReadOnlyArray<FileOrDirectoryArtifact> skipMaterializationFor,
            bool isServiceFinalization,
            bool mustRunOnMaster,
            string[] tags,
            out PipId ipcPipId)
        {

            var pip = new IpcPip(
                ipcClientInfo,
                arguments,
                outputFile: outputFile,
                servicePipDependencies: servicePipDependencies,
                fileDependencies: fileDependencies,
                directoryDependencies: directoryDependencies,
                skipMaterializationFor: skipMaterializationFor,
                isServiceFinalization: isServiceFinalization,
                mustRunOnMaster: mustRunOnMaster,
                tags: ToStringIds(tags),
                provenance: CreatePipProvenance(string.Empty)
            );

            if (PipGraph != null)
            {
                var success = PipGraph.AddIpcPip(pip, GetValuePipId());
                ipcPipId = pip.PipId;
                return success;
            }

            ipcPipId = PipId.Invalid;
            return true;
        }

        /// <nodoc />
        public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot)
        {
            if (PipGraph != null)
            {
                return PipGraph.ReserveSharedOpaqueDirectory(directoryArtifactRoot);
            }

            // If the pip graph is not available (e.g. /phase:evaluate was passed)
            // then a directory artifact with seal id zero should suffice
            return DirectoryArtifact.CreateWithZeroPartialSealId(directoryArtifactRoot);
        }

        private PipProvenance CreatePipProvenance(string description)
        {
            var usage = string.IsNullOrEmpty(description) 
                ? PipData.Invalid 
                : PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, description);
            return CreatePipProvenance(usage);
        }

        internal PipProvenance CreatePipProvenance(PipData usage)
        {
            var result = new PipProvenance(
                GetNextSemiStableHash(),
                moduleId: m_moduleId,
                moduleName: StringId.Create(Context.StringTable, m_moduleName),
                outputValueSymbol: m_valuePip.Symbol,
                token: m_valuePip.LocationData,
                qualifierId: m_valuePip.Qualifier,
                usage: usage);
            return result;
        }

        /// <nodoc />
        public long GetNextSemiStableHash()
        {
            return Interlocked.Increment(ref m_semiStableHash);
        }

        private ReadOnlyArray<StringId> ToStringIds(string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return ReadOnlyArray<StringId>.Empty;
            }
            else
            {
                var tagArray = new StringId[tags.Length];
                for (int i = 0; i < tags.Length; i++)
                {
                    tagArray[i] = StringId.Create(Context.StringTable, tags[i]);
                }

                return ReadOnlyArray<StringId>.FromWithoutCopy(tagArray);
            }
        }

        private PipId GetValuePipId()
        {
            if (PipGraph != null)
            {
                if (!m_valuePip.PipId.IsValid)
                {
                    PipGraph.AddOutputValue(m_valuePip);
                }

                Contract.Assert(m_valuePip.PipId.IsValid);
            }

            return m_valuePip.PipId;
        }

        private static string Hash(string content)
        {
            var hashedBlob = FingerprintUtilities.Hash(content);
            return FingerprintUtilities.FingerprintToFileName(hashedBlob);
        }

        /// <summary>
        /// Gets a directory unique for the current call under the object directory.
        /// </summary>
        public DirectoryArtifact GetUniqueObjectDirectory(PathAtom name)
        {
            var relativePath = GetUniqueRelativePath(name);
            return DirectoryArtifact.CreateWithZeroPartialSealId(m_objectRoot.Combine(Context.PathTable, relativePath));

        }

        /// <summary>
        /// Gets a directory unique for the current call under the redirected directory root.
        /// </summary>
        public DirectoryArtifact GetUniqueRedirectedDirectory(PathAtom name)
        {
            var relativePath = GetUniqueRelativePath(name);
            return DirectoryArtifact.CreateWithZeroPartialSealId(m_redirectedRoot.Combine(Context.PathTable, relativePath));
        }

        /// <summary>
        /// Gets a relative path for temp files unique for the current call.
        /// </summary>
        public DirectoryArtifact GetUniqueTempDirectory()
        {
            var relativePath = GetUniqueRelativePath(PathAtom.Create(Context.StringTable, "t"));
            return DirectoryArtifact.CreateWithZeroPartialSealId(m_tempRoot.Combine(Context.PathTable, relativePath));
        }

        /// <summary>
        /// Gets a relative path unique for the current call.
        /// </summary>
        private RelativePath GetUniqueRelativePath(PathAtom name)
        {
            var count = m_folderIdResolver.GetNextId(name);
            var stringTable = Context.StringTable;

            var pathAtom = count == 0
                ? name
                : PathAtom.Create(stringTable, string.Concat(name.ToString(stringTable), "_", count.ToString(CultureInfo.InvariantCulture)));

            return PipRelativePath.Combine(pathAtom);
        }
    }
}
