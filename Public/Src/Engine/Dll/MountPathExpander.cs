// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Normalizes paths using the given mounts
    /// </summary>
    public sealed class MountPathExpander : SemanticPathExpander
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "MountPathExpander", version: 1);

        private readonly Expander m_nameExpander;
        private readonly Dictionary<string, string> m_expandedRootsByName;
        private readonly Dictionary<string, SemanticPathInfo> m_mountsByName;
        private readonly FlaggedHierarchicalNameDictionary<SemanticPathInfo> m_semanticPathInfoMap;
        private readonly List<SemanticPathInfo> m_alternativeRoots;

        /// <summary>
        /// The parent whitelist
        /// If this is a module specific mount path expander, parent is the root mount path expander
        /// Otherwise, if this is the root mount path expander, this is null
        /// This field is mutually exclusive with the <see cref="m_moduleExpanders"/> field.
        /// </summary>
        private readonly MountPathExpander m_parent;

        /// <summary>
        /// The module specific mount path expanders (null if this is a module whitelist)
        /// This field is mutually exclusive with the <see cref="m_parent"/> field.
        /// </summary>
        private readonly Dictionary<ModuleId, MountPathExpander> m_moduleExpanders;

        /// <summary>
        /// Path table used by mount path expander.
        /// </summary>
        public PathTable PathTable { get; }

        /// <summary>
        /// Class constructor
        /// </summary>
        public MountPathExpander(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            m_nameExpander = new Expander();
            m_parent = null;
            PathTable = pathTable;
            m_expandedRootsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            m_mountsByName = new Dictionary<string, SemanticPathInfo>(StringComparer.OrdinalIgnoreCase);
            m_semanticPathInfoMap = new FlaggedHierarchicalNameDictionary<SemanticPathInfo>(pathTable, HierarchicalNameTable.NameFlags.Root);
            m_moduleExpanders = new Dictionary<ModuleId, MountPathExpander>();
            m_alternativeRoots = new List<SemanticPathInfo>();
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        public MountPathExpander(MountPathExpander parent)
            : this(parent.PathTable)
        {
            Contract.Requires(parent != null);

            m_parent = parent;
        }

        /// <summary>
        /// Adds the root to the set of tokenized roots
        /// </summary>
        public void Add(PathTable pathTable, BuildXL.Utilities.Configuration.IMount mount)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(mount != null);

            Add(pathTable, new SemanticPathInfo(mount.Name, mount.Path, mount.TrackSourceFileChanges, mount.IsReadable, mount.IsWritable, mount.IsSystem, mount.IsScrubbable,  mount.AllowCreateDirectory));
        }

        /// <summary>
        /// Gets the expander for the specified module
        /// </summary>
        public override SemanticPathExpander GetModuleExpander(ModuleId moduleId)
        {
            return GetModuleMountExpander(moduleId);
        }

        /// <summary>
        /// Gets the expander for the specified module
        /// </summary>
        public MountPathExpander GetModuleMountExpander(ModuleId moduleId)
        {
            MountPathExpander moduleExpander;
            if (m_moduleExpanders.TryGetValue(moduleId, out moduleExpander))
            {
                return moduleExpander;
            }

            return this;
        }

        /// <summary>
        /// Gets or creates the expander for the specified module
        /// </summary>
        public MountPathExpander CreateModuleMountExpander(ModuleId moduleId)
        {
            MountPathExpander moduleExpander;
            if (!m_moduleExpanders.TryGetValue(moduleId, out moduleExpander))
            {
                moduleExpander = new MountPathExpander(this);
                m_moduleExpanders.Add(moduleId, moduleExpander);
            }

            return moduleExpander;
        }

        /// <summary>
        /// Tries to get a mount's root by name.
        /// </summary>
        /// <remarks>
        /// This is a bit of a backdoor to get mount mappings since the IValue for mounts are not available when loading
        /// a cached graph. The mapping is needed to parse pip filters that may be defined in terms of mount names.
        /// </remarks>
        public bool TryGetRootByMountName(string mountName, out AbsolutePath root)
        {
            SemanticPathInfo info;
            if (m_mountsByName.TryGetValue(mountName, out info))
            {
                root = info.Root;
                return true;
            }

            if (m_parent != null)
            {
                return m_parent.TryGetRootByMountName(mountName, out root);
            }

            root = AbsolutePath.Invalid;
            return false;
        }

        /// <summary>
        /// Adds the root to the set of tokenized roots
        /// </summary>
        public void Add(PathTable pathTable, in SemanticPathInfo mount, bool forceTokenize = false)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(mount.IsValid);

            if (forceTokenize || mount.IsSystem)
            {
                m_nameExpander.Add(pathTable, mount);
            }

            string expandedRoot = mount.RootName.ToString(pathTable.StringTable);
            m_expandedRootsByName.Add(expandedRoot, mount.Root.ToString(pathTable));
            m_mountsByName.Add(expandedRoot, mount);
            bool wasAdded = m_semanticPathInfoMap.TryAdd(mount.Root.Value, mount);
            Contract.Assert(wasAdded);
        }

        /// <summary>
        /// Add the root that should be tokenized similarly to a previously added root. 
        /// </summary>
        public void AddWithExistingName(PathTable pathTable, in SemanticPathInfo mount, bool forceTokenize = false)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(mount.IsValid);
            
            string mountName = mount.RootName.ToString(pathTable.StringTable);
            if (!m_mountsByName.ContainsKey(mountName))
            {
                Contract.Assert(false, $"Mount '{mountName}' (path: '{mount.Root.ToString(pathTable)}') has not been added yet.");
            }

            if (forceTokenize || mount.IsSystem)
            {
                m_nameExpander.Add(pathTable, mount);
            }

            m_alternativeRoots.Add(mount);
        }

        /// <inheritdoc />
        public override SemanticPathInfo GetSemanticPathInfo(AbsolutePath path)
        {
            KeyValuePair<HierarchicalNameId, SemanticPathInfo> mapping;
            if (m_semanticPathInfoMap.TryGetFirstMapping(path.Value, out mapping))
            {
                return mapping.Value;
            }

            if (m_parent != null)
            {
                return m_parent.GetSemanticPathInfo(path);
            }

            return SemanticPathInfo.Invalid;
        }

        /// <inheritdoc />
        public override SemanticPathInfo GetSemanticPathInfo(string path)
        {
            AbsolutePath absolutePath;
            return AbsolutePath.TryCreate(PathTable, path, out absolutePath) ? GetSemanticPathInfo(absolutePath) : SemanticPathInfo.Invalid;
        }

        /// <inheritdoc />
        public override string ExpandPath(PathTable pathTable, AbsolutePath path)
        {
            char separator = PathFormatter.GetPathSeparator(PathFormat.HostOs);

            string result = pathTable.ExpandName(path.Value, m_nameExpander, separator);

            // deal with the exceptional case of needing a \ after a drive name
            // This is here so it matches the implementation in AbsolutePath.ToString(). The
            // HierarchicalNameTable itself does not add the trailing slash.
            if (result.Length == 2)
            {
                result += separator;
            }

            return result;
        }

        /// <inheritdoc />
        public override bool TryGetPath(PathTable pathTable, string path, out AbsolutePath absolutePath)
        {
            return TryGetOrCreate(pathTable, path, create: false, absolutePath: out absolutePath);
        }

        /// <inheritdoc />
        public override bool TryCreatePath(PathTable pathTable, string path, out AbsolutePath absolutePath)
        {
            return TryGetOrCreate(pathTable, path, create: true, absolutePath: out absolutePath);
        }

        private bool TryGetOrCreate(PathTable pathTable, string path, bool create, out AbsolutePath absolutePath)
        {
            absolutePath = AbsolutePath.Invalid;
            if (path.Length == 0)
            {
                return false;
            }

            if (path.Length < 2 || path[0] != '%')
            {
                if (!AbsolutePath.TryGet(pathTable, (StringSegment)path, out absolutePath) && create)
                {
                    return AbsolutePath.TryCreate(pathTable, path, out absolutePath);
                }

                return absolutePath.IsValid;
            }

            int index = path.IndexOf('%', 1);
            if (index < 0)
            {
                return false;
            }

            string rootName = path.Substring(1, index - 1);
            string root;
            if (!m_expandedRootsByName.TryGetValue(rootName, out root))
            {
                return false;
            }

            using (PooledObjectWrapper<StringBuilder> wrapper = Pools.StringBuilderPool.GetInstance())
            {
                StringBuilder sb = wrapper.Instance;
                sb.Append(root);
                sb.Append(path, index + 1, path.Length - (index + 1));

                string fullPath = sb.ToString();
                if (!AbsolutePath.TryGet(pathTable, (StringSegment)fullPath, out absolutePath) && create)
                {
                    return AbsolutePath.TryCreate(pathTable, fullPath, out absolutePath);
                }

                return absolutePath.IsValid;
            }
        }

        private sealed class Expander : HierarchicalNameTable.NameExpander
        {
            private readonly Dictionary<HierarchicalNameId, string> m_roots;

            public Expander()
            {
                m_roots = new Dictionary<HierarchicalNameId, string>();
            }

            public void Add(PathTable pathTable, in SemanticPathInfo mount)
            {
                pathTable.SetFlags(mount.Root.Value, HierarchicalNameTable.NameFlags.Root);
                m_roots.Add(
                    mount.Root.Value,
                    I($"%{mount.RootName.ToString(pathTable.StringTable).ToUpperInvariant()}%"));
            }

            private bool TryGetRootToken(HierarchicalNameId name, HierarchicalNameTable.NameFlags nameFlags, out string rootToken)
            {
                if (((nameFlags & HierarchicalNameTable.NameFlags.Root) == HierarchicalNameTable.NameFlags.Root) &&
                    m_roots.TryGetValue(name, out rootToken))
                {
                    return true;
                }

                rootToken = null;
                return false;
            }

            public override int GetLength(
                HierarchicalNameId name,
                StringTable stringTable,
                StringId stringId,
                HierarchicalNameTable.NameFlags nameFlags,
                out bool expandContainer)
            {
                string rootToken;
                if (TryGetRootToken(name, nameFlags, out rootToken))
                {
                    expandContainer = false;
                    return rootToken.Length;
                }

                return base.GetLength(name, stringTable, stringId, nameFlags, out expandContainer);
            }

            public override int CopyString(
                HierarchicalNameId name,
                StringTable stringTable,
                StringId stringId,
                HierarchicalNameTable.NameFlags nameFlags,
                char[] buffer,
                int endIndex)
            {
                string rootToken;
                if (TryGetRootToken(name, nameFlags, out rootToken))
                {
                    rootToken.CopyTo(0, buffer, endIndex - rootToken.Length, rootToken.Length);
                    return rootToken.Length;
                }

                return base.CopyString(name, stringTable, stringId, nameFlags, buffer, endIndex);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> GetWritableRoots()
        {
            return GetRoots(info => info.IsWritable);
        }

        /// <inheritdoc />
        public override IEnumerable<AbsolutePath> GetPathsWithAllowedCreateDirectory()
        {
            return GetRoots(info => info.AllowCreateDirectory);
        }

        /// <summary>
        /// Gets the roots that can be scrubbed
        /// </summary>
        public IEnumerable<AbsolutePath> GetScrubbableRoots()
        {
            return GetRoots(info => info.IsScrubbable);
        }

        /// <summary>
        /// Gets all roots.
        /// </summary>
        public IEnumerable<AbsolutePath> GetAllRoots()
        {
            return GetRoots(info => true);
        }

        /// <summary>
        /// Gets the dictionary of mount name to SemanticPathInfo of this instance
        /// and all of its parents.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, SemanticPathInfo> GetAllMountsByName()
        {
            var result = (m_parent != null) ?
                m_parent.GetAllMountsByName() : new Dictionary<string, SemanticPathInfo>(StringComparer.OrdinalIgnoreCase);

            // Override the parent mounts
            foreach (var mount in m_mountsByName)
            {
                result[mount.Key] = mount.Value;
            }

            return result;
        }

        /// <summary>
        /// Gets the roots that can be scrubbed
        /// </summary>
        private IEnumerable<AbsolutePath> GetRoots(Func<SemanticPathInfo, bool> func)
        {
            using (var pool = Pools.AbsolutePathListPool.GetInstance())
            {
                foreach (var info in m_mountsByName.Values)
                {
                    if (func(info))
                    {
                        pool.Instance.Add(info.Root);
                    }
                }

                return pool.Instance.ToArray();
            }
        }

        /// <summary>
        /// Serializes a MountPathExpander
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            SerializeInfo(writer);

            SerializeAlternativeRoots(writer);

            if (m_moduleExpanders != null)
            {
                writer.Write(true);
                writer.Write(m_moduleExpanders.Count);
                foreach (var moduleExpanderEntry in m_moduleExpanders)
                {
                    ModuleId moduleId = moduleExpanderEntry.Key;
                    MountPathExpander moduleExpander = moduleExpanderEntry.Value;
                    writer.Write(moduleId);
                    moduleExpander.SerializeInfo(writer);
                }
            }
            else
            {
                writer.Write(false);
            }
        }

        private void SerializeInfo(BuildXLWriter writer)
        {
            writer.Write(m_mountsByName.Count);
            foreach (SemanticPathInfo info in m_mountsByName.Values)
            {
                info.Serialize(writer);
            }
        }

        private void SerializeAlternativeRoots(BuildXLWriter writer)
        {
            // NOTE: uses the same 'format' as SerializeInfo method so we could reuse DeserializeInfo
            writer.Write(m_alternativeRoots.Count);
            foreach (var info in m_alternativeRoots)
            {
                info.Serialize(writer);
            }
        }

        /// <summary>
        /// Deserializes a MountPathExpander
        /// </summary>
        public static async Task<MountPathExpander> DeserializeAsync(BuildXLReader reader, Task<PathTable> pathTableTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(pathTableTask != null);

            SemanticPathInfo[] pathInfos = DeserializeInfo(reader);

            SemanticPathInfo[] alternativeRoots = DeserializeInfo(reader);

            var pathTable = await pathTableTask;
            if (pathTable != null)
            {
                MountPathExpander result = new MountPathExpander(pathTable);
                foreach (var pathInfo in pathInfos)
                {
                    result.Add(pathTableTask.Result, pathInfo);
                }

                foreach (var alternativeRoot in alternativeRoots)
                {
                    result.AddWithExistingName(pathTableTask.Result, alternativeRoot);
                }

                if (reader.ReadBoolean())
                {
                    var count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var moduleId = reader.ReadModuleId();
                        var modulePathInfos = DeserializeInfo(reader);
                        var moduleExpander = result.CreateModuleMountExpander(moduleId);
                        foreach (var modulePathInfo in modulePathInfos)
                        {
                            moduleExpander.Add(pathTable, modulePathInfo);
                        }
                    }
                }

                return result;
            }

            return null;
        }

        private static SemanticPathInfo[] DeserializeInfo(BuildXLReader reader)
        {
            var count = reader.ReadInt32();
            SemanticPathInfo[] pathInfos = new SemanticPathInfo[count];
            for (int i = 0; i < count; i++)
            {
                pathInfos[i] = SemanticPathInfo.Deserialize(reader);
            }

            return pathInfos;
        }
    }
}
