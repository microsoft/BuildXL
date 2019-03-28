// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Workspaces.Utilities
{
    /// <summary>
    /// Represent content known by one resolver: a set of module references, where each module descriptor
    /// contains a set of specs.
    /// </summary>
    public sealed class ModuleRepository
    {
        /// <summary>
        /// (SpecName, SpecContent) pair.
        /// </summary>
        public sealed class NameContentPair
        {
            /// <nodoc/>
            public string SpecName { get; }

            /// <nodoc/>
            public string SpecContent { get; }

            /// <nodoc/>
            public NameContentPair(string specName, string specContent)
            {
                SpecName = specName;
                SpecContent = specContent;
            }
        }

        /// <nodoc/>
        public PathTable PathTable { get; }

        /// <nodoc/>
        public AbsolutePath RootDir { get; }

        private readonly MultiValueDictionary<ModuleDescriptor, NameContentPair> m_backingDictionary;

        /// <nodoc/>
        public ModuleRepository(PathTable pathTable, AbsolutePath? rootDir = null)
        {
            Contract.Requires(rootDir == null || rootDir.Value.IsValid);

            PathTable = pathTable;
            RootDir = rootDir != null ? rootDir.Value : AbsolutePath.Create(PathTable, OperatingSystemHelper.IsUnixOS ? "/" : "C:\\");
            m_backingDictionary = new MultiValueDictionary<ModuleDescriptor, NameContentPair>();
        }

        /// <summary>
        /// Creates a new instance of a module repository by copying the content from <param name="moduleRepository"/>
        /// </summary>
        public ModuleRepository(ModuleRepository moduleRepository)
        {
            PathTable = moduleRepository.PathTable;
            RootDir = moduleRepository.RootDir;
            m_backingDictionary = new MultiValueDictionary<ModuleDescriptor, NameContentPair>(moduleRepository.m_backingDictionary);
        }

        /// <summary>
        /// Convenience method, mainly for testing. <see cref="AddContent(ModuleDescriptor,string[])"/>
        /// </summary>.
        public ModuleRepository AddContent(string moduleName, params string[] content)
        {
            return AddContent(ModuleDescriptor.CreateForTesting(moduleName), content);
        }

        /// <summary>
        /// Adds a <param name="module"/> together with a collection of specs with <param name="contents"/>
        /// </summary>
        /// <remarks>
        /// Order of the specs in <paramref name="contents"/> is important since it determines the path
        /// to it: the file name of the i-th spec for a given module is set to $"{i}.dsc".
        /// </remarks>
        public ModuleRepository AddContent(ModuleDescriptor module, params string[] contents)
        {
            IReadOnlyList<NameContentPair> currentSpecs;
            var currentCount = m_backingDictionary.TryGetValue(module, out currentSpecs)
                ? currentSpecs.Count
                : 0;
            var converted = contents
                .Select((content, idx) => new NameContentPair(I($"{idx + currentCount}{Names.DotDscExtension}"), content))
                .ToArray();
            return AddContent(module, converted);
        }

        /// <summary>
        /// Adds a <param name="module"/> together with a collection of specs (<param name="content"/>) with
        /// explicitly specified names and contents.
        /// </summary>
        public ModuleRepository AddContent(ModuleDescriptor module, NameContentPair[] content)
        {
            m_backingDictionary.Add(module, content);
            return this;
        }

        /// <summary>
        /// Adds all the modules in <param name="moduleRepository"/> to this module with content
        /// </summary>
        /// <remarks>
        /// When existing modules are added, specs are appended to the existing collection of specs
        /// </remarks>
        public ModuleRepository AddAll(ModuleRepository moduleRepository)
        {
            foreach (var key in moduleRepository.m_backingDictionary.Keys)
            {
                m_backingDictionary.Add(key, moduleRepository.m_backingDictionary[key].ToArray());
            }

            return this;
        }

        /// <nodoc/>
        [Pure]
        public bool ContainsModule(ModuleDescriptor module)
        {
            return m_backingDictionary.ContainsKey(module);
        }

        /// <nodoc/>
        [Pure]
        public IEnumerable<ModuleDescriptor> GetAllModules()
        {
            return m_backingDictionary.Keys;
        }

        /// <nodoc/>
        [Pure]
        public IReadOnlyCollection<string> GetSpecsForModule(ModuleDescriptor module)
        {
            return m_backingDictionary[module].Select(pair => pair.SpecContent).ToList();
        }

        /// <summary>
        /// Gets an absolute path that represents a spec in a <param name="module"/>. The
        /// position of specs is a zero-based <param name="index"/>, based on the position of the
        /// spec when the module as added.
        /// </summary>
        public AbsolutePath GetPathToModuleAndSpec(ModuleDescriptor module, int index)
        {
            Contract.Requires(ContainsModule(module));
            Contract.Requires(index >= 0 && index < GetSpecsForModule(module).Count);

            return GetPathToModuleAndSpecUnsafe(module, index);
        }

        /// <summary>
        /// A non-validated version of <see cref="GetPathToModuleAndSpec"/>. The module and index
        /// can actually not be part of this modules with content, in which case an exception
        /// is thrown.
        /// </summary>
        private AbsolutePath GetPathToModuleAndSpecUnsafe(ModuleDescriptor module, int index)
        {
            return RootDir
                .Combine(PathTable, module.Name)
                .Combine(PathTable, I($"{m_backingDictionary[module][index].SpecName}"));
        }

        /// <summary>
        /// Gets all the paths contained specs for a given <param name="module"/>
        /// </summary>
        public IEnumerable<AbsolutePath> GetAllPathsForModule(ModuleDescriptor module)
        {
            Contract.Requires(ContainsModule(module));

            return Enumerable
                .Range(0, m_backingDictionary[module].Count)
                .Select(index => GetPathToModuleAndSpec(module, index));
        }

        /// <summary>
        /// If a <param name="path"/> points to a valid module and spec. The path is assumed to be obtained with
        /// <see cref="GetPathToModuleAndSpec"/> or <see cref="GetAllPathsForModule"/>
        /// </summary>
        [Pure]
        public bool ContainsSpec(AbsolutePath path)
        {
            return FindSpec(path) != null;
        }

        /// <summary>
        /// Returns the content of a spec that <param name="path"/> points to.
        /// </summary>
        public string GetSpecContentFromPath(AbsolutePath path)
        {
            Contract.Requires(path != AbsolutePath.Invalid);

            return FindSpec(path)?.SpecContent;
        }

        /// <summary>
        /// From a given <param name="path"/> that points to a module and spec, returns its module.
        /// </summary>
        public ModuleDescriptor GetModuleFromPath(AbsolutePath path)
        {
            var moduleName = path.GetParent(PathTable).GetName(PathTable).ToString(PathTable.StringTable);
            return ModuleDescriptor.CreateForTesting(moduleName);
        }

        private NameContentPair FindSpec(AbsolutePath path)
        {
            var module = GetModuleFromPath(path);
            IReadOnlyList<NameContentPair> specsForModule;
            if (!m_backingDictionary.TryGetValue(module, out specsForModule))
            {
                return null;
            }

            var fileName = path.GetName(PathTable).ToString(PathTable.StringTable);
            return specsForModule.FirstOrDefault(pair => pair.SpecName.Equals(fileName, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
