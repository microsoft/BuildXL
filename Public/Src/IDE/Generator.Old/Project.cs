// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Ide.Generator.Old
{
    /// <summary>
    /// A project
    /// </summary>
    internal sealed class Project
    {
        /// <summary>
        /// The name of the qualifier used by the project
        /// </summary>
        public string FriendlyQualifier { get; private set; }

        private readonly ConcurrentDictionary<string, object> m_properties;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<object, Item>> m_items;

        // temporary
        public List<AbsolutePath> RawReferences { get; private set; }

        /// <summary>
        /// The output directory type
        /// </summary>
        private OutputDirectoryType m_outputDirectoryType;

        /// <summary>
        /// Constructs a new project
        /// </summary>
        public Project(string friendlyQualifier)
        {
            FriendlyQualifier = friendlyQualifier;
            RawReferences = new List<AbsolutePath>();
            m_properties = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            m_items = new ConcurrentDictionary<string, ConcurrentDictionary<object, Item>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the project output directory
        /// </summary>
        public void SetOutputDirectory(AbsolutePath outputDirectory, OutputDirectoryType outputDirectoryType, string buildFilter)
        {
            if (outputDirectoryType > m_outputDirectoryType)
            {
                m_outputDirectoryType = outputDirectoryType;
                SetProperty("OutputPath", outputDirectory);
                SetProperty("OutDir", outputDirectory);
                SetProperty("DominoBuildFilter", buildFilter);
            }
        }

        /// <summary>
        /// Sets a property on the project
        /// </summary>
        public void SetProperty(string name, string value)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(value != null);

            m_properties[name] = value;
        }

        /// <summary>
        /// Sets a property on the project
        /// </summary>
        public void SetProperty(string name, AbsolutePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path != AbsolutePath.Invalid);

            m_properties[name] = path;
        }

        /// <summary>
        /// Sets a property on the project
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        public void SetProperty(string name, RelativePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path != RelativePath.Invalid);

            m_properties[name] = path;
        }

        /// <summary>
        /// Sets a property on the project
        /// </summary>
        public void SetProperty(string name, PathAtom path)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path != PathAtom.Invalid);

            m_properties[name] = path;
        }

        /// <summary>
        /// Gets a string property on the project. Returns null if the string is not found or the type does not match
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        public object TryGetProperty(string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));

            object value;
            m_properties.TryGetValue(name, out value);

            return value;
        }

        /// <summary>
        /// Copies the property
        /// </summary>
        public void CopyPropertyFrom(string name, Project source)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(source != null);

            m_properties[name] = source.m_properties[name];
        }

        /// <summary>
        /// Adds new item.
        /// </summary>
        public void AddItem(string name, Item item)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(item != null);

            var items = m_items.GetOrAdd(name, key => new ConcurrentDictionary<object, Item>());

            items.TryAdd(item.Include, item);
        }

        /// <summary>
        /// Creates a new item.
        /// </summary>
        public Item AddItem(string name, AbsolutePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path != AbsolutePath.Invalid);

            var items = m_items.GetOrAdd(name, key => new ConcurrentDictionary<object, Item>());

            var newItem = new Item(path);
            return items.GetOrAdd(newItem.Include, newItem);
        }

        /// <summary>
        /// Creates a new item.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        public Item AddItem(string name, RelativePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path != RelativePath.Invalid);

            var items = m_items.GetOrAdd(name, key => new ConcurrentDictionary<object, Item>());

            var newItem = new Item(path);
            return items.GetOrAdd(newItem.Include, newItem);
        }

        /// <summary>
        /// Creates a new item.
        /// </summary>
        public Item AddItem(string name, PathAtom path)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path != PathAtom.Invalid);

            var items = m_items.GetOrAdd(name, key => new ConcurrentDictionary<object, Item>());

            var newItem = new Item(path);
            return items.GetOrAdd(newItem.Include, newItem);
        }

        /// <summary>
        /// Creates a new item.
        /// </summary>
        public Item AddItem(string name, string str)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(!string.IsNullOrEmpty(str));

            var items = m_items.GetOrAdd(name, key => new ConcurrentDictionary<object, Item>());

            var newItem = new Item(str);
            return items.GetOrAdd(newItem.Include, newItem);
        }

        /// <summary>
        /// All properties
        /// </summary>
        public IEnumerable<KeyValuePair<string, object>> Properties => m_properties;

        /// <summary>
        /// All items
        /// </summary>
        public ILookup<string, Item> Items
        {
            get { return m_items.SelectMany(kv => kv.Value.Values.Select(value => new { kv.Key, Value = value })).ToLookup(kv => kv.Key, kv => kv.Value); }
        }
    }
}
