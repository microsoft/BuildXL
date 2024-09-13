// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeLinuxPipDebugAnalyzer()
        {
            string outputDirectory = null;
            long semiStableHash = 0;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    semiStableHash = ParseSemistableHash(opt);
                }
                else
                {
                    throw Error("Unknown option for linux pip debug analysis: {0}", opt.Name);
                }
            }

            if (semiStableHash == 0)
            {
                throw Error("pip parameter is required");
            }

            return new LinuxPipDebugAnalyzer(GetAnalysisInput(), outputDirectory, semiStableHash);
        }

        private static void WriteLinuxPipDebugAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("LinuxPipDebugConfig options:");
            writer.WriteModeOption(nameof(AnalysisMode.LinuxPipDebug), $"Generates launch.json configuration for vscode to launch linux test debugger");
            writer.WriteOption("outputDirectory", "Optional. Output directory for the launch.json file. If not specified, the execution log path will be used.");
            writer.WriteOption("pip", "Required. The semi-stable hash for the pip.", shortName: "p");
        }
    }

    public sealed class LinuxPipDebugAnalyzer : Analyzer
    {
        private readonly Process m_pip;
        private readonly string m_logPath;

        private readonly StringId m_spaceSeparator;

        public LinuxPipDebugAnalyzer(AnalysisInput input, string outputDirectory, long semiStableHash) : base(input)
        {
            m_spaceSeparator = StringId.Create(StringTable, " ");

            if (string.IsNullOrEmpty(outputDirectory))
            {
                // Use the execution log path
                m_logPath = Path.Combine(Path.GetDirectoryName(input.ExecutionLogPath), $"LinuxPipDebugConfig");
            }
            else
            {
                m_logPath = Path.Combine(outputDirectory, $"LinuxPipDebugConfig");
            }

            var pipTable = input.CachedGraph.PipTable;
            foreach (var pipId in pipTable.StableKeys)
            {
                if (pipTable.GetPipType(pipId) != PipType.Process)
                {
                    continue;
                }

                var possibleMatch = pipTable.GetPipSemiStableHash(pipId);
                if (possibleMatch == semiStableHash)
                {
                    m_pip = (Process)pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                }
            }

            if (m_pip == null)
            {
                // If no matches were found, then we likely got some bad input from the user.
                throw new InvalidArgumentException($"Specified Pip 'Pip{semiStableHash:X}' does not exist or is not a process.");
            }
        }

        public override int Analyze()
        {
            var workingDirectory = m_pip.WorkingDirectory.ToString(PathTable);
            var program = Path.Combine(workingDirectory, "xunit.console.dll");

            var json = new JsonObject
            {
                ["configurations"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = $"Debug Pip{m_pip.SemiStableHash}",
                        ["type"] = "coreclr",
                        ["request"] = "launch",
                        ["program"] = program,
                        ["args"] = GetArgumentsJsonArray(m_pip, program),
                        ["cwd"] = workingDirectory,
                        ["stopAtEntry"] = false,
                        ["console"] = "internalConsole"
                    }
                }
            };

            try
            {
                var jsonString = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
                FileUtilities.CreateDirectoryWithRetry(m_logPath);
                File.WriteAllText(Path.Combine(m_logPath, $"{m_pip.FormattedSemiStableHash}.json"), jsonString);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to create launch.json to {m_logPath} for pip {m_pip.SemiStableHash}: {e}");
                return 1;
            }
            return 0;
        }

        private JsonArray GetArgumentsJsonArray(Process process, string programPath)
        {
            var argumentJsonArray = new JsonArray();
            var renderer = new PipFragmentRenderer(PathTable);

            var arguments = m_pip.Arguments;
            AddArgumentToJsonArray(argumentJsonArray, arguments, false, renderer, programPath);
            return argumentJsonArray;
        }

        private bool AddArgumentToJsonArray(JsonArray array, PipData arguments, bool start, PipFragmentRenderer renderer, string programPath)
        {
            var enumerator = arguments.GetEnumerator();
            while(enumerator.MoveNext())
            {
                var arg = enumerator.Current;
                if (arg.FragmentType == PipFragmentType.NestedFragment)
                {
                    start = AddArgumentToJsonArray(array, arg.GetNestedFragmentValue(), start, renderer, programPath);
                    continue;
                }

                if (start)
                {
                    string value = renderer.Render(enumerator.Current);
                    array.Add(value);
                }
                else
                {
                    if (arg.FragmentType == PipFragmentType.AbsolutePath)
                    {
                        var path = arg.GetPathValue();
                        if (path.Equals(AbsolutePath.Create(PathTable, programPath)))
                        {
                            start = true;
                        }
                    }

                }
            }

            return start;
        }
    }
}
