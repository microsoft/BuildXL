// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    public static partial class MarkdownWriter
    {
        private const string IndexFileNameWithoutExtension = "index";
        private const string OrderFileName = ".order";

        internal static void WriteWorkspace(DocWorkspace docWorkspace)
        {
            // Build index
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"# {docWorkspace.Name} Workspace");
            sb.AppendLine("## Modules");

            foreach (var module in docWorkspace.Modules.OrderBy(m => m.Name))
            {
                sb.AppendLine($"* {CreateLink(module)}");
            }

            // Write out the index
            WriteToDoc(IndexFileNameWithoutExtension, sb);

            // Handle all modules under this namespace
            foreach (var module in docWorkspace.Modules)
            {
                WriteModule(module);
            }

            var fileNameArray = Directory.GetFiles(s_rootFolder)
                .Where(f => !f.Equals(OrderFileName, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(s => !s.Equals(IndexFileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                .ThenBy(s => s)
                .ToList();
            if (fileNameArray.Count > 0)
            {
                File.WriteAllLines(Path.Combine(s_rootFolder, OrderFileName), fileNameArray);
            }
        }
    }
}
