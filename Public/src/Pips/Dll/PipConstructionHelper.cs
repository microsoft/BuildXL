// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips
{
    /// <summary>
    /// Class with helper functions for creating pip
    /// </summary>
    public sealed class PipConstructionHelper
    {
        private static readonly char[] s_safeFileNameChars =
        {
            // numbers
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',

            // letters (only lowercase as windows is case sensitive) Some letters removed to reduce the chance of offensive words occurring.
            'a', 'b', 'c', 'd', 'e', /*'f',*/ 'g', 'h', /*'i',*/ 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', /*'s',*/ 't', /*'u',*/ 'v', 'w', 'x', 'y', 'z',
        };

        /// <summary>
        /// The current semistable hash to hand out for pips
        /// </summary>
        private long m_semiStableHash;

        private ReserveFoldersResolver m_folderIdResolver;

        private readonly AbsolutePath m_objectRoot;

        private readonly AbsolutePath m_tempRoot;

        /// <nodoc />
        private PipExecutionContext Context { get; }

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
            AbsolutePath tempRoot,
            ModuleId moduleId,
            string moduleName,
            ValuePip valuePip,
            RelativePath pipRelativePath,
            long semiStableHashSeed)
        {
            Context = context;
            m_objectRoot = objectRoot;
            m_tempRoot = tempRoot.IsValid ? tempRoot : objectRoot;
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
        /// IDeally this function would take ModuleId, FullSymbol QualifierId and compute uniqueOutputLocation itself. Unfortunately today the data is not yet
        /// exposed via IPipGraph, therefore the responsibility is on the call site for now.
        /// </remarks>
        public static PipConstructionHelper Create(
            PipExecutionContext context,
            AbsolutePath objectRoot,
            AbsolutePath tempRoot,
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
                tempRoot,
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
            AbsolutePath? tempRoot = null,
            string moduleName = null,
            string specRelativePath = null,
            string symbol = null,
            AbsolutePath? specPath = null,
            QualifierId? qualifierId = null)
        {
            return Create(
                context,
                objectRoot ?? AbsolutePath.Create(context.PathTable, "d:\\test\\obj"),
                tempRoot ?? objectRoot ?? AbsolutePath.Create(context.PathTable, "d:\\test\\tmp"),
                new ModuleId(1),
                moduleName ?? "TestModule",
                RelativePath.Create(context.StringTable, specRelativePath ?? "spec"),
                FullSymbol.Create(context.SymbolTable, symbol ?? "testValue"),
                new LocationData(specPath ?? AbsolutePath.Create(context.PathTable, "d:\\src\\spec.dsc"), 0, 0),
                qualifierId ?? QualifierId.Unqualified);
        }

        /// <nodoc />
        public bool TryAddProcess(ProcessBuilder processBuilder, out ProcessOutputs processOutputs, out Process pip)
        {
            if (!processBuilder.TryFinish(this, out pip, out processOutputs))
            {
                return false;
            }

            return true;
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
            return m_valuePip.PipId;
        }

        private static string Hash(string content)
        {
            byte[] fingerprint = new SHA1Managed().ComputeHash(Encoding.UTF8.GetBytes(content));
            return FingerprintToFileName(fingerprint);
        }

        /// <summary>
        /// Helper to return a short filename that is a valid filename
        /// </summary>
        /// <remarks>
        /// This function is a trade-off between easy filename characters and the size of the path
        ///
        /// We currently use Sha1 which has 20 bytes.
        /// A default of using ToHex would expand each byte to 2 characters
        /// n bytes => m char (base m-root(255^n)) => Math.Ceiling(m * 20 / N)
        ///
        /// 1 bytes => 2 char (base16) => (40 char) // easy to code, each byte matches
        /// 5 bytes => 8 char (base32) => (32 char) // nice on bit boundary
        /// 2 bytes => 3 char (base41) => (30 char) // not on bit-boundary so harder to code
        /// 3 bytes => 4 char (base64) => (28 char) // nice on bit-boundary, but need extended characters like '�', '�', '�', '�' etc.
        /// 4 bytes => 5 char (base85) => (25 char) // not in bit-boundary so harder to code.
        ///
        /// For now the character savings of higher bases is not worth it so we'll stick with base32.
        /// We can extend later
        /// </remarks>
        public static string FingerprintToFileName(byte[] fingerprint)
        {
            Contract.Requires(fingerprint != null);
            Contract.Requires(fingerprint.Length > 0);

            var targetBitLength = 5;
            var inputBitLength = 8;
            var bitMask = ((int)Math.Pow(2, inputBitLength) >> (inputBitLength - targetBitLength)) - 1;

            Contract.Assume(s_safeFileNameChars.Length == Math.Pow(2, targetBitLength));

            int nrOfBytes = fingerprint.Length;

            // Multiply by inputBitLength, to get bit count of source.
            int sourceLengthInBits = nrOfBytes * inputBitLength;

            // We are converting base 256 to base 32, since both
            // are power of 2, this makes the task easier.
            // Every 5 bits are converted to a corresponding digit or
            // a character in converted string (8 bits).
            // Calculate how many converted character will be needed.
            int base32LengthInBits = (sourceLengthInBits / targetBitLength) * inputBitLength;
            if (sourceLengthInBits % targetBitLength != 0)
            {
                // Some left over bits, we will pad them with zero.
                base32LengthInBits += inputBitLength;
            }

            // sourceLengthInBits in must have been a multiple of <inputBitLength>.
            int outputLength = base32LengthInBits / inputBitLength;

            // Allocate the string to store converted characters.
            var output = new StringBuilder(outputLength);

            int i = 0;
            int remainingBits = 0;
            int accumulator = 0;

            // For every <targetBitLength> bits insert a character into the string.
            for (i = 0; i < nrOfBytes; i++)
            {
                accumulator = (accumulator << inputBitLength) | fingerprint[i];
                remainingBits += inputBitLength;

                while (remainingBits >= targetBitLength)
                {
                    remainingBits -= targetBitLength;
                    output.Append(s_safeFileNameChars[(accumulator >> remainingBits) & bitMask]);
                }
            }

            // Some left over bits, pad them with zero and insert one character for them also.
            if (remainingBits > 0)
            {
                output.Append(s_safeFileNameChars[(accumulator << (targetBitLength - remainingBits)) & bitMask]);
            }

            // Return the final base32 string.
            return output.ToString();
        }

        /// <summary>
        /// Gets a relative path unique for the current call.
        /// </summary>
        public DirectoryArtifact GetUniqueObjectDirectory(PathAtom name)
        {
            var relativePath = GetUniqueRelativePath(name);
            return DirectoryArtifact.CreateWithZeroPartialSealId(m_objectRoot.Combine(Context.PathTable, relativePath));

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

        /// <summary>
        /// Helper struct for constructing unique identifiers for a given folder names.
        /// </summary>
        private struct ReserveFoldersResolver
        {
            // The dictionary is allocated only when more than one folder name was queried for the unique id.
            // This saves reasonable amount of memory even for medium size builds (like .5Gb for Word).
            private StringId m_firstFolder;
            private int m_firstFolderCount;

            private readonly object m_syncRoot;
            private readonly Lazy<ConcurrentDictionary<StringId, int>> m_reservedFolders;

            /// <nodoc />
            public ReserveFoldersResolver(object syncRoot)
                : this()
            {
                m_syncRoot = syncRoot;

                m_reservedFolders = new Lazy<ConcurrentDictionary<StringId, int>>(() => new ConcurrentDictionary<StringId, int>(
                                                                                    /*DEFAULT_CONCURRENCY_MULTIPLIER * PROCESSOR_COUNT*/
                                                                                    concurrencyLevel: 4 * Environment.ProcessorCount,
                                                                                    capacity: /*Default capacity*/ 4));
            }

            /// <summary>
            /// Returns the next Id for a given <paramref name="name"/>.
            /// </summary>
            public int GetNextId(PathAtom name)
            {
                Contract.Requires(name.IsValid);

                var id = name.StringId;

                // Check if we can use the first slot
                if (!m_firstFolder.IsValid)
                {
                    lock (m_syncRoot)
                    {
                        if (!m_firstFolder.IsValid)
                        {
                            m_firstFolder = id;
                            return 0;
                        }
                    }
                }

                // Check if the name matches the first folder name.
                if (m_firstFolder == id)
                {
                    return Interlocked.Increment(ref m_firstFolderCount);
                }

                // Fallback: using ConcurrentDictionary
                return m_reservedFolders.Value.AddOrUpdate(
                    name.StringId,
                    (_) => 0,
                    (_, currentCount) => currentCount + 1);
            }
        }
    }
}
