// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Module registry.
    /// </summary>
    public sealed class ModuleRegistry : IModuleRegistry
    {
        /// <summary>
        /// The legacy global module registration
        /// </summary>
        public GlobalModuleLiteral GlobalLiteral { get; }

        /// <nodoc />
        public PrimitiveTypes PrimitiveTypes { get; }

        /// <nodoc />
        public PredefinedTypes PredefinedTypes { get; }

        /// <summary>
        /// Registry for module instances.
        /// </summary>
        /// <remarks>
        /// A module instance can be partial or full. A partial module instance has unresolved import declarations.
        /// </remarks>
        private readonly ConcurrentDictionary<QualifiedModuleId, FileModuleLiteral> m_moduleDictionary = new ConcurrentDictionary<QualifiedModuleId, FileModuleLiteral>();

        private readonly ConcurrentDictionary<QualifiedModuleId, TypeOrNamespaceModuleLiteral> m_namespaceModuleDictionary = new ConcurrentDictionary<QualifiedModuleId, TypeOrNamespaceModuleLiteral>();

        /// <summary>
        /// Registry for uninstantiated modules and their data (owning package, lists of unresolved import declarations).
        /// </summary>
        /// <remarks>
        /// An uninstantiated module is always partial.
        /// </remarks>
        private readonly ConcurrentDictionary<ModuleLiteralId, UninstantiatedModuleInfo> m_uninstantiatedModuleDictionary = new ConcurrentDictionary<ModuleLiteralId, UninstantiatedModuleInfo>();

        /// <summary>
        /// For serialization purposes only.
        /// </summary>
        internal ConcurrentDictionary<ModuleLiteralId, UninstantiatedModuleInfo> UninstantiatedModules => m_uninstantiatedModuleDictionary;

        /// <nodoc />
        public ModuleRegistry(SymbolTable symbolTable)
        {
            PrimitiveTypes = new PrimitiveTypes(symbolTable.StringTable);
            GlobalLiteral = new GlobalModuleLiteral(symbolTable);
            PredefinedTypes = new Ambients.PredefinedTypes(PrimitiveTypes);
            PredefinedTypes.Register(GlobalLiteral);

        }

        /// <nodoc/>
        [NotNull]
        public UninstantiatedModuleInfo GetUninstantiatedModuleInfoByModuleId(ModuleLiteralId moduleId)
        {
            Contract.Requires(moduleId.IsValid);

            if (!m_uninstantiatedModuleDictionary.TryGetValue(moduleId, out UninstantiatedModuleInfo result))
            {
                string message = I($"Can't find uninstantiated module by module id '{moduleId}'");
                Contract.Assume(false, message);

                // Throwing an exception to prevent potential NRE if contracts are disabled.
                throw new InvalidOperationException(message);
            }

            return result;
        }

        /// <summary>
        /// Returns uninstantiated module by a given path.
        /// </summary>
        public UninstantiatedModuleInfo GetUninstantiatedModuleInfoByPathForTests(AbsolutePath path)
        {
            foreach (var kvp in m_uninstantiatedModuleDictionary)
            {
                if (kvp.Key.Path.Equals(path) && kvp.Value.FileModuleLiteral != null)
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <nodoc />
        public FileModuleLiteral InstantiateModule<TState>(TState state, QualifiedModuleId moduleKey, Func<TState, QualifiedModuleId, FileModuleLiteral> valueFactory)
        {
            return m_moduleDictionary.GetOrAddWithState(state, moduleKey, valueFactory);
        }

        /// <nodoc />
        public TypeOrNamespaceModuleLiteral InstantiateModule<TState>(TState state, QualifiedModuleId moduleKey, Func<TState, QualifiedModuleId, TypeOrNamespaceModuleLiteral> valueFactory)
        {
            return m_namespaceModuleDictionary.GetOrAddWithState(state, moduleKey, valueFactory);
        }

        /// <nodoc/>
        public bool TryGetInstantiatedModule(QualifiedModuleId moduleKey, out FileModuleLiteral qualifiedFileModule)
        {
            return m_moduleDictionary.TryGetValue(moduleKey, out qualifiedFileModule);
        }

        /// <nodoc/>
        public void AddUninstantiatedModuleInfo(IUninstantiatedModuleInfo moduleInfo)
        {
            Contract.Requires(moduleInfo != null, "moduleInfo != null");
            var uninstantiatedModule = (UninstantiatedModuleInfo)moduleInfo;
            m_uninstantiatedModuleDictionary.TryAdd(uninstantiatedModule.ModuleLiteral.Id, uninstantiatedModule);
        }

        /// <nodoc/>
        public bool TryGetUninstantiatedModuleInfoByPath(AbsolutePath path, out UninstantiatedModuleInfo result)
        {
            return m_uninstantiatedModuleDictionary.TryGetValue(ModuleLiteralId.Create(path), out result);
        }

        /// <nodoc/>
        [NotNull]
        public UninstantiatedModuleInfo GetUninstantiatedModuleInfoByPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid, "path.IsValid");

            if (!m_uninstantiatedModuleDictionary.TryGetValue(ModuleLiteralId.Create(path), out UninstantiatedModuleInfo result))
            {
                string message = I($"Can't find uninstantiated module by path '{path.ToDebuggerDisplay()}'");
                Contract.Assume(false, message);

                // Throwing an exception to prevent potential NRE if contracts are disabled.
                throw new InvalidOperationException(message);
            }

            return result;
        }
    }
}
