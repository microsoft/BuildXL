// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Class to serialize BuildXL objects to JSON.
    /// </summary>
    public class JsonFingerprinter : IFingerprinter, IDisposable
    {
        /// <summary>
        /// Used to write JSON objects.
        /// </summary>
        private JsonTextWriter Writer { get; set; }

        /// <summary>
        /// Used to expand <see cref="AbsolutePath"/>s.
        /// </summary>
        private PathTable PathTable { get; set; }

        /// <summary>
        /// The tokenizer used to handle path roots.
        /// </summary>
        public PathExpander PathExpander { get; private set; }

        /// <summary>
        /// Uses the same underlying state as the owning <see cref="JsonFingerprinter"/>, but provides access to additional functions.
        /// Control is passed to <see cref="m_collectionFingerprinter"/> when using <see cref="AddCollection{TValue, TCollection}(string, TCollection, Action{ICollectionFingerprinter, TValue})"/>/.
        /// </summary>
        private readonly JsonCollectionFingerprinter m_collectionFingerprinter;

        /// <summary>
        /// What fingerprinter to use when writing JSON collections (arrays).
        /// Property names are disallowed in JSON arrays, but unnamed values and complete JSON objects (i.e. { "name" : "value" }) are allowed.
        /// 
        /// Recursive calls of <see cref="AddCollection{TValue, TCollection}(string, TCollection, Action{ICollectionFingerprinter, TValue})"/>
        /// and <see cref="AddNested(string, Action{IFingerprinter})"/> will alternate writing with <see cref="CollectionFingerprinter"/> and <see cref="Fingerprinter"/>.
        /// </summary>
        protected virtual JsonCollectionFingerprinter CollectionFingerprinter => m_collectionFingerprinter;

        /// <summary>
        /// What fingerprinter to use when writing standard JSON objects.
        /// Property names must precede anything written in a standard JSON object, unnamed values and complete JSON objects (i.e. { "name" : "value" }) are disallowed.
        /// 
        /// Recursive calls of <see cref="AddCollection{TValue, TCollection}(string, TCollection, Action{ICollectionFingerprinter, TValue})"/>
        /// and <see cref="AddNested(string, Action{IFingerprinter})"/> will alternate writing with <see cref="CollectionFingerprinter"/> and <see cref="Fingerprinter"/>.
        /// </summary>
        protected virtual JsonFingerprinter Fingerprinter => this;

        /// <summary>
        /// Convenience function for creating a <see cref="JsonFingerprinter"/> and writing a single JSON object.
        /// </summary>
        /// <remarks>
        /// If no <see cref="PathTable"/> is provided, no APIs that expand <see cref="AbsolutePath"/>s can be used by the fingerprinter.
        /// </remarks>
        public static string CreateJsonString(
            Action<JsonFingerprinter> fingerprintOps,
            Formatting formatting = Formatting.None,
            PathTable pathTable = null,
            PathExpander pathExpander = null)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;
                using (var writer = new JsonFingerprinter(sb, formatting: formatting, pathTable: pathTable, pathExpander: pathExpander))
                {
                    fingerprintOps(writer);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public JsonFingerprinter(
            StringBuilder stringBuilder, 
            Formatting formatting = Formatting.None,
            PathTable pathTable = null,
            PathExpander pathExpander = null)
        {
            Writer = new JsonTextWriter(new StringWriter(stringBuilder));
            Writer.Formatting = formatting;
            Writer.WriteStartObject();

            PathTable = pathTable;
            PathExpander = pathExpander ?? PathExpander.Default;

            m_collectionFingerprinter = new JsonCollectionFingerprinter(this);
        }

        /// <summary>
        /// Constructor for <see cref="JsonCollectionFingerprinter"/>/ to use the same underlying state.
        /// </summary>
        private JsonFingerprinter(JsonFingerprinter jsonFingerprinter)
        {
            Writer = jsonFingerprinter.Writer;
            PathTable = jsonFingerprinter.PathTable;
            PathExpander = jsonFingerprinter.PathExpander;
        }

        /// <summary>
        /// Adds a named value.
        /// </summary>
        private void WriteJson<T>(string name, T value)
        {
            WriteNamedJsonObject(name, () =>
            {
                Writer.WriteValue(value);
            });
        }

        /// <summary>
        /// Adds a value.
        /// </summary>
        private void WriteJson<T>(T value)
        {
            Writer.WriteValue(value);
        }

        /// <inheritdoc />
        public void Add(string name, Fingerprint fingerprint)
        {
            WriteJson(name, fingerprint.ToString());
        }

        /// <inheritdoc />
        public void Add(string name, ContentHash contentHash)
        {
            WriteJson(name, ContentHashToString(contentHash));
        }

        /// <inheritdoc />
        public void Add(string name, string text)
        {
            WriteJson(name, text);
        }

        /// <inheritdoc />
        public void Add(string name, int value)
        {
            WriteJson(name, value.ToString());
        }
        
        /// <inheritdoc />
        public void Add(string name, long value)
        {
            WriteJson(name, value.ToString());
        }

        /// <inheritdoc />
        public void Add(string name, AbsolutePath path)
        {
            WriteJson(name, PathToString(path));
        }

        /// <inheritdoc />
        public void Add(string name, StringId text)
        {
            WriteJson(name, StringIdToString(text));
        }

        /// <inheritdoc />
        public void Add(AbsolutePath path, ContentHash hash)
        {
            WriteJson(PathToString(path), ContentHashToString(hash));
        }

        /// <inheritdoc />
        public void Add(string name, AbsolutePath path, ContentHash hash)
        {
            AddNested(name, f => f.Add(path, hash));
        }

        /// <inheritdoc />
        public void Add(AbsolutePath path, Fingerprint fingerprint)
        {
            WriteJson(PathToString(path), fingerprint.ToString());
        }

        /// <inheritdoc />
        public void Add(string name, AbsolutePath path, Fingerprint fingerprint)
        {
            AddNested(name, f => f.Add(path, fingerprint));
        }

        /// <inheritdoc />
        public void AddNested(string name, Action<IFingerprinter> addOps)
        {
            WriteNamedJsonObject(name, () =>
            {
                Writer.WriteStartObject();
                addOps(Fingerprinter);
                Writer.WriteEndObject();
            });
        }

        /// <inheritdoc />
        public void AddNested(StringId name, Action<IFingerprinter> addOps)
        {
            AddNested(StringIdToString(name), addOps);
        }

        /// <inheritdoc />
        public void AddNested(AbsolutePath path, Action<IFingerprinter> addOps)
        {
            AddNested(PathToString(path), addOps);
        }

        /// <inheritdoc />
        public void AddCollection<TValue, TCollection>(string name, TCollection elements, Action<ICollectionFingerprinter, TValue> addElement) 
            where TCollection : IEnumerable<TValue>
        {
            WriteNamedJsonObject(name, () =>
            {
                Writer.WriteStartArray();
                foreach (var element in elements)
                {
                    addElement(CollectionFingerprinter, element);
                }
                Writer.WriteEndArray();
            });
        }

        /// <inheritdoc />
        public void AddOrderIndependentCollection<TValue, TCollection>(string name, TCollection elements, Action<ICollectionFingerprinter, TValue> addElement, IComparer<TValue> comparer)
            where TCollection : IEnumerable<TValue>
        {
            AddCollection(name, elements.OrderBy(x => x, comparer), addElement);
        }

        /// <summary>
        /// Writes a named JSON object.
        /// </summary>
        protected virtual void WriteNamedJsonObject(string name, Action addOps)
        {
            Writer.WritePropertyName(name);
            addOps();
        }

        /// <summary>
        /// Converts a path to a normalized string.
        /// </summary>
        private string PathToString(AbsolutePath path)
        {
            Contract.Requires(PathTable != null, "Cannot add AbsolutePaths to a JsonFingerprinter that was initialized without a PathTable.");
            string pathString = path.IsValid ? PathExpander.ExpandPath(PathTable, path) : "??Invalid";

            // Normalize string paths to lower case since absolute path equivalency
            // depends on the hash and the path, but not casing
            return pathString.ToLowerInvariant();
        }

        /// <summary>
        /// Converts a <see cref="StringId"/> to a string.
        /// </summary>
        private string StringIdToString(StringId stringId)
        {
            Contract.Requires(PathTable != null, "Cannot add strings by StringId to a JsonFingerprinter that was initialized without a PathTable.");
            return stringId.ToString(PathTable.StringTable);
        }

        /// <summary>
        /// Converts a <see cref="ContentHash"/> to a string appropriate for the JSON fingerprint.
        /// For compactness, the hash is truncated to just the first 10 characters.
        /// This will cause data loss and should only be used for displaying hashes.
        /// </summary>
        public static string ContentHashToString(ContentHash hash)
        {
            Contract.Requires(hash.HashType != HashType.Unknown);
            var fullString = hash.ToHex();
            return hash.ToHex().Substring(0, 10);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Writer.WriteEndObject();
            Writer.Close();
        }

        /// <summary>
        /// Extends a <see cref="JsonFingerprinter"/> to implement <see cref="ICollectionFingerprinter"/>.
        /// This can only be accessed through an existing <see cref="JsonFingerprinter"/> during 
        /// <see cref="AddCollection{TValue, TCollection}(string, TCollection, Action{ICollectionFingerprinter, TValue})"/>
        /// to prevent invalid JSON from being created.
        /// </summary>
        protected class JsonCollectionFingerprinter : JsonFingerprinter, ICollectionFingerprinter
        {
            /// <summary>
            /// Captures the <see cref="JsonFingerprinter"/> that instantiate this <see cref="JsonCollectionFingerprinter"/>
            /// and passes control back when <see cref="IFingerprinter.AddNested(string, Action{IFingerprinter})"/> is called.
            /// </summary>
            private readonly JsonFingerprinter m_jsonFingerprinter;

            /// <inheritdoc />
            protected override JsonCollectionFingerprinter CollectionFingerprinter => this;

            /// <inheritdoc />
            protected override JsonFingerprinter Fingerprinter => m_jsonFingerprinter;

            /// <inheritdoc />
            public JsonCollectionFingerprinter(JsonFingerprinter jsonFingerprinter) : base(jsonFingerprinter)
            {
                m_jsonFingerprinter = jsonFingerprinter;
            }

            /// <inheritdoc />
            protected override void WriteNamedJsonObject(string name, Action addOps)
            {
                Writer.WriteStartObject();

                Writer.WritePropertyName(name);
                addOps();

                Writer.WriteEndObject();
            }

            /// <inheritdoc />
            public void Add(StringId text)
            {
                WriteJson(StringIdToString(text));
            }

            /// <inheritdoc />
            public void Add(string text)
            {
                WriteJson(text);
            }

            /// <inheritdoc />
            public void Add(int value)
            {
                WriteJson(value);
            }

            /// <inheritdoc />
            public void Add(AbsolutePath path)
            {
                WriteJson(PathToString(path));
            }

            /// <inheritdoc />
            public void Add(Fingerprint fingerprint)
            {
                WriteJson(fingerprint.ToString());
            }

            /// <inheritdoc />
            public void AddFileName(AbsolutePath path)
            {
                Contract.Requires(PathTable != null, "Cannot add filename component of an AbsolutePath to a JsonFingerprinter initialized without a PathTable");
                WriteJson(path.GetName(PathTable).ToString(PathTable.StringTable).ToLowerInvariant());
            }
        }
    }
}
