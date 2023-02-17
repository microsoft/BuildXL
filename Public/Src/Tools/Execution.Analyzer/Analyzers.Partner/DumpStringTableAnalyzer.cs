// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeDumpStringTableAnalyzer()
        {
            string outputFile = null;
            var outputFormat = DumpStringTableAnalyzer.OutputFormat.PlainText;
            bool sortBySize = false;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else if (opt.Name.Equals("format", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Enum.TryParse(opt.Value, true, out outputFormat))
                    {
                        throw Error("Failed to parse the value of format: {0}", opt.Value);
                    }
                }
                else if (opt.Name.Equals("sortBySize", StringComparison.OrdinalIgnoreCase))
                {
                    sortBySize = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputFile))
            {
                throw Error("Missing required argument 'outputFile'");
            }

            return new DumpStringTableAnalyzer(GetAnalysisInput(), outputFile, outputFormat, sortBySize);
        }

        private static void WriteDumpStringTableAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Dump StringTable");
            writer.WriteModeOption(nameof(AnalysisMode.DumpStringTable), "Dumps out the string table to the given output file path");
            writer.WriteOption("outputFile", "Required. The path to the output file.", shortName: "o");
            writer.WriteOption("format", "Optional. PlainText or Json. Default is PlainText.");
            writer.WriteOption("sortBySize", "Optional. This can be used to sort the strings descending by size.");
        }
    }

    internal sealed class DumpStringTableAnalyzer : Analyzer
    {
        public enum OutputFormat
        {
            PlainText,
            Json,
        }

        private readonly string m_outputFilePath;
        private readonly OutputFormat m_outputFormat;
        private readonly bool m_sortBySize;

        public DumpStringTableAnalyzer
        (
            AnalysisInput input,
            string outputFilePath,
            OutputFormat outputFormat,
            bool sortBySize
        ) : base(input)
        {
            m_outputFilePath = outputFilePath;
            m_outputFormat = outputFormat;
            m_sortBySize = sortBySize;
        }

        private IEnumerable<string> GetOrderedStringsInTable()
        {
            if (!m_sortBySize)
            {
                return StringTable.Strings;
            }

            return StringTable.Strings.OrderByDescending(str => str.Length);
        }

        private void DumpUsingPlainText()
        {
            using (TextWriter writer = new StreamWriter(m_outputFilePath, append: false, Encoding.UTF8))
            {
                writer.WriteLine($"NumStrings: {StringTable.Count}");

                int stringIndex = 0;
                foreach (var str in GetOrderedStringsInTable())
                {
                    writer.WriteLine($"{stringIndex}: {str}");
                    ++stringIndex;
                }
            }
        }

        private void DumpUsingJson()
        {
            using (var jsonStream = new StreamWriter(m_outputFilePath, append: false, Encoding.UTF8))
            using (var json = new JsonTextWriter(jsonStream))
            {
                json.Formatting = Formatting.Indented;
                json.IndentChar = ' ';
                json.Indentation = 2;

                json.WriteStartArray();

                foreach (var str in GetOrderedStringsInTable())
                {
                    json.WriteValue(str);
                }

                json.WriteEndArray();
            }
        }

        public override int Analyze()
        {
            Console.WriteLine($"Dumping the string table to {m_outputFilePath} using {m_outputFormat}...");

            switch (m_outputFormat)
            {
                case OutputFormat.PlainText:
                    DumpUsingPlainText();
                    break;
                case OutputFormat.Json:
                    DumpUsingJson();
                    break;
            }

            return 0;
        }
    }
}