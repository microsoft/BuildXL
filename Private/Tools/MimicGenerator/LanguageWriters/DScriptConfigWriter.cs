// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;

namespace Tool.MimicGenerator.LanguageWriters
{
    public sealed class DScriptConfigWriter : ConfigWriter
    {
        private readonly IList<string> m_importedModules;
        private readonly IDictionary<string, string> m_mountPoints;

        public DScriptConfigWriter(string absolutePath)
            : base(Path.GetDirectoryName(absolutePath) + Path.DirectorySeparatorChar + Names.ConfigDsc)
        {
            m_importedModules = new List<string>();
            m_mountPoints = new Dictionary<string, string>();
        }

        /// <inheritdoc />
        public override void AddModule(ModuleWriter moduleWriter)
        {
            m_importedModules.Add(DScriptWriterUtils.ToRelativePath(moduleWriter.AbsolutePath, AbsolutePath));
        }

        /// <inheritdoc />
        public override void WriteMount(string mountName, string mountAbsolutePath)
        {
            m_mountPoints[mountName] = mountAbsolutePath;
        }

        /// <inheritdoc />
        public override string ToRelativePathExpression(string path)
        {
            string drive = char.ToUpperInvariant(path[0]).ToString();
            WriteMount(drive, drive);
            return DScriptWriterUtils.EncloseInQuotes(DScriptWriterUtils.GetPathFromExpression(path), "'");
        }

        /// <inheritdoc />
        protected override void WriteStart()
        {
        }

        /// <inheritdoc />
        protected override void WriteEnd()
        {
            var environmentVariablesString = string.Join(", ", AllowedEnvironmentVariables.Select(DScriptWriterUtils.EncloseInDoubleQuotes));
            Writer.WriteLine(
@"config({
    projects: [],
    modules: [f`package.dsc`],
    resolvers: Environment.getPathValues(""ScriptSdk"", "";"").map(path => <SourceResolver>{
        kind: ""SourceResolver"",
        root: d`${path}`
    }),");
            Writer.WriteLine("    allowedEnvironmentVariables: [{0}],", environmentVariablesString);
            Writer.WriteLine("    mounts: [");
            foreach (var mountPoint in m_mountPoints)
            {
                Writer.WriteLine("        {");
                Writer.WriteLine("            name: PathAtom.create(\"{0}\"),", mountPoint.Key);
                Writer.WriteLine("            path: {0}, ", DScriptWriterUtils.ToPath(mountPoint.Value, PathType.Path));
                Writer.WriteLine("            trackSourceFileChanges: true, ");
                Writer.WriteLine("            isReadable: true, ");
                Writer.WriteLine("            isWritable: true, ");
                Writer.WriteLine("            isSystem: false ");
                Writer.WriteLine("        },");
            }

            Writer.WriteLine("    ]");
            Writer.WriteLine(@"});");
        }
    }
}
