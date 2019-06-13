// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Extends CoreHashingHelper by adding handling for pip data.
    /// </summary>
    public sealed class HashingHelper : CoreHashingHelperBase, ICollectionFingerprinter
    {
        /// <summary>
        /// Path table used to expand <see cref="AbsolutePath" /> values into path strings.
        /// </summary>
        private readonly PathTable m_pathTable;

        /// <summary>
        /// The tokenizer used to handle path roots
        /// </summary>
        private readonly PathExpander m_pathExpander;

        private readonly bool m_recordFingerprintString;
        /// <summary>
        /// Class constructor.
        /// </summary>
        public HashingHelper(PathTable pathTable, bool recordFingerprintString, PathExpander pathExpander = null, HashAlgorithmType hashAlgorithmType = HashAlgorithmType.SHA1Managed)
            : base(recordFingerprintString, hashAlgorithmType)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_recordFingerprintString = recordFingerprintString;
            m_pathExpander = pathExpander ?? PathExpander.Default;
        }

        /// <summary>
        /// Adds a path to the fingerprint stream.
        /// The path is expanded to a string (using the <see cref="PathTable" /> provided to the constructor).
        /// </summary>
        public void Add(AbsolutePath path)
        {
            BeginItem(HashValueType.Path);
            AddInnerPath(path);
            EndItem();
        }

        /// <summary>
        /// Adds a file reference to the fingerprint stream.
        /// The file is expanded to a string (using the <see cref="PathTable" /> provided to the constructor).
        /// </summary>
        public void Add(FileArtifact file)
        {
            Add(file.Path);
            Add(file.RewriteCount);
        }

        /// <summary>
        /// Adds a path to the fingerprint stream.
        /// The path is expanded to a string (using the <see cref="PathTable" /> provided to the constructor).
        /// </summary>
        public void Add(string name, AbsolutePath path)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.Path, name);
            AddInnerPath(path);
            EndItem();
        }

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// The value is expanded to a string (using the <see cref="StringTable" /> provided to the constructor).
        /// </summary>
        public void Add(string name, StringId text)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.String, name);
            AddInnerString(text);
            EndItem();
        }

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// The value is expanded to a string (using the <see cref="StringTable" /> provided to the constructor).
        /// </summary>
        public void Add(StringId text)
        {
            BeginItem(HashValueType.String);
            AddInnerString(text);
            EndItem();
        }

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// The value is expanded to a string (using the <see cref="StringTable" /> provided to the constructor).
        /// </summary>
        public void Add(PathAtom text)
        {
            Add(text.StringId);
        }

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// </summary>
        public void Add(string text)
        {
            BeginItem(HashValueType.String);
            AddInnerString(text, forceUppercase: false);
            EndItem();
        }

        /// <summary>
        /// Adds a path and content hash pair to the fingerprint stream.
        /// See <see cref="Add(string ,AbsolutePath)"/>
        /// </summary>
        public void Add(AbsolutePath path, ContentHash hash)
        {
            Contract.Requires(path.IsValid, "Invalid paths should not have a content hash");

            BeginItem(HashValueType.HashedPath);
            AddInnerPath(path);
            AddInnerCharacter('|');
            AddInnerHash(hash);
            EndItem();
        }

        /// <summary>
        /// Adds a path and content hash pair to the fingerprint stream.
        /// See <see cref="Add(string ,AbsolutePath)"/>
        /// </summary>
        public void Add(string name, AbsolutePath path, ContentHash hash)
        {
            Contract.Requires(name != null);
            Contract.Requires(path.IsValid, "Invalid paths should not have a content hash");

            BeginItem(HashValueType.HashedPath, name);
            AddInnerPath(path);
            AddInnerCharacter('|');
            AddInnerHash(hash);
            EndItem();
        }

        /// <inheritdoc />
        public void AddFileName(AbsolutePath path) => throw new NotImplementedException();

        /// <inheritdoc />
        public void AddNested(string name, Action<IFingerprinter> addOps)
        {
            Add(name);
            Indent();
            addOps(this);
            Unindent();
        }

        /// <inheritdoc />
        public void AddNested(StringId name, Action<IFingerprinter> addOps)
        {
            string nameString = name.IsValid ? m_pathTable.StringTable.GetString(name) : "??Invalid";
            AddNested(nameString, addOps);
        }

        /// <inheritdoc />
        public void AddNested(AbsolutePath path, Action<IFingerprinter> addOps)
        {
            Add(path);
            Indent();
            addOps(this);
            Unindent();
        }

        /// <summary>
        /// Adds a <see cref="AbsolutePath" /> to the fingerprint stream without adding a type prefix.
        /// The path is added in an expanded string form.
        /// See <see cref="Add(string, AbsolutePath)" />.
        /// </summary>
        private void AddInnerPath(AbsolutePath path)
        {
            string pathString = path.IsValid ? m_pathExpander.ExpandPath(m_pathTable, path) : "??Invalid";

            AddInnerString(pathString, forceUppercase: true);
        }

        /// <summary>
        /// Adds a <see cref="StringId" /> to the fingerprint stream without adding a type prefix.
        /// The value is added in an expanded string form.
        /// See <see cref="Add(string, StringId)" />.
        /// </summary>
        private void AddInnerString(StringId text)
        {
            string textString = text.IsValid ? m_pathTable.StringTable.GetString(text) : "??Invalid";
            AddInnerString(textString, forceUppercase: false);
        }

        /// <inheritdoc />
        public void AddCollection<TValue, TCollection>(string name, TCollection elements, Action<ICollectionFingerprinter, TValue> addElement) 
            where TCollection : IEnumerable<TValue>
        {
            Contract.Requires(name != null);
            Contract.Requires(elements != null);
            Contract.Requires(addElement != null);

            int count = 0;
            Indent();

            foreach (TValue element in elements)
            {
                addElement(this, element);
                count++;
            }

            Unindent();

            // We always include a length for variable-length collections to keep the fingerprint function injective.
            Add(name, count);
        }

        /// <inheritdoc />
        public void AddOrderIndependentCollection<TValue, TCollection>(string name, TCollection elements, Action<ICollectionFingerprinter, TValue> addElement, IComparer<TValue> comparer)
            where TCollection : IEnumerable<TValue>
        {
            Contract.Requires(name != null);
            Contract.Requires(elements != null);
            Contract.Requires(addElement != null);

            using (var helper = new HashingHelper(m_pathTable, recordFingerprintString: m_recordFingerprintString, pathExpander: m_pathExpander, hashAlgorithmType: HashAlgorithmType.MurmurHash3))
            {
                int count = 0;
                Indent();
                byte[] result = new byte[helper.HashSizeBytes];

                foreach (TValue element in elements)
                {
                    addElement(helper, element);
                    var right = helper.GenerateHashBytes();
                    result.CombineOrderIndependent(right);
                    count++;
                }

                if (m_recordFingerprintString)
                {
                    AddInnerStringLiteralDebug(helper.FingerprintInputText);
                }

                Add(result);
                Unindent();

                // We always include a length for variable-length collections to keep the fingerprint function injective.
                Add(name, count);
            }
        }

        /// <summary>
        /// Adds a path and fingerprint to the fingerprint stream.
        /// </summary>
        public void Add(AbsolutePath path, Fingerprint fingerprint)
        {
            BeginItem(HashValueType.HashedPath);
            AddInnerPath(path);
            AddInnerCharacter('|');
            AddInnerFingerprint(fingerprint);
            EndItem();
        }

        /// <summary>
        /// Adds a path and fingerprint to the fingerprint stream.
        /// </summary>
        public void Add(string name, AbsolutePath path, Fingerprint fingerprint)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.HashedPath, name);
            AddInnerPath(path);
            AddInnerCharacter('|');
            AddInnerFingerprint(fingerprint);
            EndItem();
        }
    }
}
