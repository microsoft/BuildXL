// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.Script.Constants;

#pragma warning disable SA1649 // File name must match first type name

namespace Tool.MimicGenerator.LanguageWriters
{
    internal readonly struct ImportSpec
    {
        public readonly DScriptSpecWriter SpecWriter;
        public readonly string VarName;

        public ImportSpec(DScriptSpecWriter specWriter, string importVarName)
        {
            SpecWriter = specWriter;
            VarName = importVarName;
        }
    }

    /// <summary>
    /// Writes a module file for DScript.
    /// </summary>
    public sealed class DScriptPackageWriter : ModuleWriter
    {
        private readonly Dictionary<string, ImportSpec> m_specs = new Dictionary<string, ImportSpec>();
        private int m_importCounter = 0;

        public DScriptPackageWriter(string absolutePath, string identity, IEnumerable<string> logicAssemblies)
            : base(Path.Combine(absolutePath, Names.PackageDsc), identity, logicAssemblies)
        {
            using (StreamWriter sw = new StreamWriter(Path.Combine(absolutePath, Names.PackageConfigDsc)))
            {
                sw.WriteLine("module({ name: \"Mimic\" });");
            }
        }

        /// <inheritdoc />
        protected override void WriteStart()
        {
        }

        /// <inheritdoc />
        protected override void WriteEnd()
        {
            // Do nothing.
        }

        /// <inheritdoc />
        public override void AddSpec(string specRelativePath, SpecWriter specWriter)
        {
            var dsSpecWriter = specWriter as DScriptSpecWriter;

            if (dsSpecWriter != null && !m_specs.ContainsKey(specRelativePath))
            {
                var importSpec = new ImportSpec(dsSpecWriter, "P" + m_importCounter++);
                m_specs[specRelativePath] = importSpec;
                string normalizedPath = DScriptWriterUtils.NormalizePath(specRelativePath);
                string name = normalizedPath.Replace("/", "__").Replace(".", "__").Replace("-", "_");
                Writer.WriteLine("import * as Mimic{1} from '/{0}';", normalizedPath, name);
            }
        }
    }
}
