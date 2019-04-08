// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tool.MimicGenerator.LanguageWriters
{
    /// <summary>
    /// Writes a spec for DScript.
    /// </summary>
    public sealed class DScriptSpecWriter : SpecWriter
    {
        // key: specPath of pip value: variable name
        private readonly Dictionary<string, string> m_imports;
        private readonly List<string> m_runners;
        private int m_counter;
        private readonly MemoryStream m_memoryStream;
        private readonly StreamWriter m_writer;

        /// <summary>
        /// Constructor
        /// </summary>
        public DScriptSpecWriter(string absolutePath)
            : base(absolutePath)
        {
            m_imports = new Dictionary<string, string>();
            m_runners = new List<string>();
            m_memoryStream = new MemoryStream();
            m_writer = new StreamWriter(m_memoryStream);
        }

        /// <nodoc/>
        public IEnumerable<string> Runners
        {
            get { return m_runners; }
        }

        /// <inheritdoc />
        protected override void WriteStart()
        {
            Writer.WriteLine("import * as Mimic from \"Tools.Mimic\";");
        }

        /// <inheritdoc />
        protected override void WriteEnd()
        {
            Writer.WriteLine();
            m_writer.Flush();
            var contents = Encoding.UTF8.GetString(m_memoryStream.GetBuffer(), 0, (int)m_memoryStream.Length);
            m_writer.Dispose();

            Writer.WriteLine(contents);
        }

        /// <inheritdoc />
        public override void AddSealDirectory(string valueName, string relativeDir, IEnumerable<string> relativePaths)
        {
            valueName = ToSealCopyWriteVarName(valueName);
            m_runners.Add(valueName);

            m_writer.WriteLine("export const {0} = Transformer.sealPartialDirectory(", valueName);
            m_writer.WriteLine("    {0},", IfStringThenToPath(relativeDir, PathType.Directory));
            m_writer.WriteLine("    [");
            foreach (var relativePath in relativePaths)
            {
                m_writer.WriteLine("        {0}, ", IfStringThenToPath(relativePath, PathType.File));
            }

            m_writer.WriteLine("    ]);");
        }

        /// <inheritdoc />
        public override void AddMimicInvocation(
            string outputValue,
            IEnumerable<string> sealDirectoryInputs,
            IEnumerable<string> pathInputs,
            IEnumerable<MimicFileOutput> outputs,
            string observedAccessesPath,
            IEnumerable<SemaphoreInfo> semaphores,
            int runTimeInMs,
            bool isLongestProcess = false)
        {
            string mimicName = ToMimicRunnerVarName(outputValue);
            m_runners.Add(mimicName);

            m_writer.WriteLine("export const {0} = Mimic.evaluate({{", mimicName);
            m_writer.WriteLine("    processRunningTime: {0},", runTimeInMs);
            m_writer.WriteLine("    isLongestProcess: {0},", isLongestProcess.ToString().ToLower(CultureInfo.CurrentCulture));
            if (observedAccessesPath != null)
            {
                m_writer.WriteLine("    observedAccesses: {0},", DScriptWriterUtils.ToPath(observedAccessesPath, PathType.Path));
                m_writer.WriteLine("    observedAccessesRoot: {0},", "p`/RootMarker.dummy`.parent");
            }

            m_writer.WriteLine("    sealDirectoryInputs: [");
            foreach (var sealDirectory in sealDirectoryInputs)
            {
                m_writer.WriteLine("        {0},", IfStringThenToPath(sealDirectory, PathType.Directory));
            }

            m_writer.WriteLine("    ],");

            m_writer.WriteLine("    fileInputs: [");
            foreach (var pathInput in pathInputs)
            {
                m_writer.WriteLine("       {0},", IfStringThenToPath(pathInput, PathType.File));
            }

            m_writer.WriteLine("    ],");

            m_writer.WriteLine("    fileOutputs: [");

            foreach (var output in outputs)
            {
                m_writer.WriteLine("        {");
                m_writer.WriteLine("            path: {0},", IfStringThenToPath(output.Path, PathType.Path));
                m_writer.WriteLine("            repeatingContent: \"{0}\",", output.RepeatingContent);
                m_writer.WriteLine("            lengthInBytes: {0},", output.LengthInBytes);
                m_writer.WriteLine("            fileId: {0}", output.FileId);
                m_writer.WriteLine("        }, ");
            }

            m_writer.WriteLine("    ],");

            m_writer.WriteLine("    semaphores: [");

            foreach (var semaphore in semaphores)
            {
                m_writer.WriteLine("       {");
                m_writer.WriteLine("           name: {0},", semaphore.Name);
                m_writer.WriteLine("           value: \"{0}\",", semaphore.Value);
                m_writer.WriteLine("           limit: {0},", semaphore.Limit);
                m_writer.WriteLine("        }, ");
            }

            m_writer.WriteLine("   ],");

            // TODO: need to plumb this stuff through to DScript
            m_writer.WriteLine("});");
        }

        /// <inheritdoc />
        public override void AddWriteFile(string valueName, string relativeDestination)
        {
            valueName = ToSealCopyWriteVarName(valueName);
            m_runners.Add(valueName);
            m_writer.WriteLine(
                "export const {0} = Transformer.writeFile({1}, {2});",
                valueName,
                IfStringThenToPath(relativeDestination, PathType.Path),
                "\"Dummy Write file\"");
        }

        /// <inheritdoc />
        public override void AddCopyFile(string valueName, string relativeSource, string relativeDestination)
        {
            valueName = ToSealCopyWriteVarName(valueName);
            m_runners.Add(valueName);
            m_writer.WriteLine(
                "export const {0} = Transformer.copyFile({1}, {2});",
                valueName,
                IfStringThenToPath(relativeSource, PathType.File),
                IfStringThenToPath(relativeDestination, PathType.Path));
        }

        /// <inheritdoc />
        public override string GetProcessInputName(string variableName, string specPath, int depId)
        {
            variableName = ToMimicRunnerVarName(variableName);
            var importVariable = GetImportVariable(specPath);
            string lhs = string.IsNullOrEmpty(importVariable) ? variableName : importVariable + "." + variableName;
            return string.Format(CultureInfo.InvariantCulture, "{0}.producedFiles.get({1})", lhs, depId);
        }

        /// <inheritdoc />
        public override string GetSealCopyWriteInputName(string variableName, string specPath)
        {
            variableName = ToSealCopyWriteVarName(variableName);
            var importVariable = GetImportVariable(specPath);
            return string.IsNullOrEmpty(importVariable)
                ? string.Format(CultureInfo.InvariantCulture, "{0}", variableName)
                : string.Format(CultureInfo.InvariantCulture, "{0}.{1}", importVariable, variableName);
        }

        [SuppressMessage("Microsoft.Design", "CA1063")]
        public new void Dispose()
        {
            base.Dispose();
            m_memoryStream.Dispose();
            m_writer.Dispose();
        }

        private string GetImportVariable(string specPath)
        {
            specPath = DScriptWriterUtils.GetPathFromExpression(specPath);

            // Check whether producer of variableName is the same file or not.
            if (DScriptWriterUtils.NormalizePath(AbsolutePath).EndsWith(specPath, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            specPath = DScriptWriterUtils.RemoveFileEnding(specPath);

            string variableName;
            if (m_imports.TryGetValue(specPath, out variableName))
            {
                // return null;
                return variableName;
            }

            variableName = "P" + m_counter++;
            m_imports.Add(specPath, variableName);
            Writer.WriteLine("import * as {1} from '{0}.dsc';", DScriptWriterUtils.NormalizePath(specPath), variableName);
            return variableName;
        }

        private static string ToSealCopyWriteVarName(string name)
        {
            return "sealCopyWrite" + name;
        }

        private static string ToMimicRunnerVarName(string name)
        {
            return "mimicRunner" + name;
        }

        /// <summary>
        /// If given 'expr' is quoted (<see cref="IsStringExpression"/>), the expression is converted
        /// to path (<see cref="ToPath"/>); otherwise, the expression is returned.
        /// </summary>
        private static string IfStringThenToPath(string expression, PathType type)
        {
            return DScriptWriterUtils.IsStringExpression(expression)
                ? DScriptWriterUtils.ToPath(expression, type)
                : expression;
        }
    }
}
