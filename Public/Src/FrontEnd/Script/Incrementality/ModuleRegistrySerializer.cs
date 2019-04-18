// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Incrementality
{
    /// <summary>
    /// Helper loading <see cref="ModuleRegistry"/> state between runs.
    /// </summary>
    public sealed class ModuleRegistrySerializer
    {
        private readonly GlobalModuleLiteral m_globalModule;
        private readonly PathTable m_pathTable;

        /// <nodoc />
        public ModuleRegistrySerializer(GlobalModuleLiteral globalModule, PathTable pathTable)
        {
            m_globalModule = globalModule;
            m_pathTable = pathTable;
        }

        /// <nodoc />
        public void Read(Stream stream, ModuleRegistry registry)
        {
            var reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true);

            int count = reader.ReadInt32Compact();
            for (int i = 0; i < count; i++)
            {
                var uninstantiatedModule = ReadModuleInfo(registry, reader);
                registry.AddUninstantiatedModuleInfo(uninstantiatedModule);
            }
        }

        /// <nodoc />
        public UninstantiatedModuleInfo ReadModuleInfo(ModuleRegistry registry, BuildXLReader reader)
        {
            var module = FileModuleLiteral.Read(reader, m_pathTable, m_globalModule, registry);

            var qualifierSpaceId = reader.ReadQualifierSpaceId();

            var uninstantiatedModule = new UninstantiatedModuleInfo(null, module, qualifierSpaceId);
            return uninstantiatedModule;
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public void Write(BuildXLWriter writer, UninstantiatedModuleInfo module)
        {
            Contract.Requires(module.FileModuleLiteral != null, "Can serialize only file modules.");

            module.FileModuleLiteral.Serialize(writer);
            writer.Write(module.QualifierSpaceId);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public void Write(Stream stream, ModuleRegistry registry)
        {
            var writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false);
            var modules = registry.UninstantiatedModules.Values.Where(m => m.FileModuleLiteral != null).ToList();

            int count = modules.Count;
            writer.WriteCompact(count);

            foreach (var module in modules)
            {
                Write(writer, module);
            }
        }
    }
}
