// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeAstredAnalyzer()
        {
            string outputFilePath = null;
            bool useOriginalPaths = false;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("useOriginalPaths", StringComparison.OrdinalIgnoreCase))
                {
                    useOriginalPaths = true;
                }
                else
                {
                    throw Error("Unknown option for Astred Analyzer: {0}", opt.Name);
                }
            }

            return new AstredAnalyzer(GetAnalysisInput(), outputFilePath, useOriginalPaths);
        }

        private static void WriteAstredAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Astred project file generator");
            writer.WriteModeOption(nameof(AnalysisMode.AstredAnalyzer), "Generates an Astred project file based on an execution log. Make sure to run bxl.exe with tracing options: '/logProcesses /logObservedFileAccesses'. See Astred Project: https://dev.azure.com/FoSSE/Astred");
            writer.WriteOption("outputFile", "The location of the output file for the Astred project.", shortName: "o");
            writer.WriteOption("useOriginalPaths", "Optional. Converts translated paths back to original location if build used /subst option.");
        }
    }

    /// <summary>
    /// Creates an Astred project file based on compiler invocations observed in a build.
    /// </summary>
    /// <remarks>
    /// See https://dev.azure.com/FoSSE/Astred for details of Astred project
    /// </remarks>
    public sealed class AstredAnalyzer : Analyzer
    {
        // configuration
        private readonly string m_outputFilePath;
        private readonly bool m_useOriginalPaths;

        // state
        private bool m_processExecutionMonitoringReportedSeen = false;
        private PathTranslator m_pathTranslator;
        private readonly AstredProject m_astredProject;

        public AstredAnalyzer(AnalysisInput input, string outputFilePath, bool useOriginalPaths)
            : base(input)
        {
            if (string.IsNullOrEmpty(outputFilePath))
            {
                string defaultFileName = ".astred.project.json";

                if (!string.IsNullOrEmpty(input.ExecutionLogPath))
                {
                    outputFilePath = !string.IsNullOrEmpty(input.ExecutionLogPath)
                        ? Path.Combine(Path.GetDirectoryName(input.ExecutionLogPath), defaultFileName)
                        : defaultFileName;
                }
                else
                {
                    outputFilePath = defaultFileName;
                }

                Console.WriteLine($"Missing option /outputFilePath using: {outputFilePath}");
            }

            m_outputFilePath = outputFilePath;
            m_useOriginalPaths = useOriginalPaths;

            m_astredProject = new AstredProject();
            m_astredProject.Units = new List<Unit>();
            m_astredProject.Packages = new Dictionary<string, string>();
        }

        /// <summary>
        /// Serialization classes for an Astred project file. Astred projects represent a codebases source file
        /// and references needed to interpret them. For example 'units' of C++ or C# source files along with
        /// header includes and compiler directives.
        /// Detailed documentation at:
        /// https://dev.azure.com/FoSSE/_git/Astred?path=/docs/wiki/Astred/Parsing-Guidance-by-Language.MD
        /// </summary>
        #region JSON Serialization
        public class AstredProject
        {
            [JsonPropertyName("packages")]
            public Dictionary<string, string> Packages { get; set; }

            [JsonPropertyName("units")]
            public List<Unit> Units { get; set; }
        }

        public class Unit
        {
            public Unit()
            {
                // Empty constructor
            }

            public Unit(string language)
            {
                Language = language;
                if (language.Equals("cpp", StringComparison.OrdinalIgnoreCase))
                {
                    AddPredefinedCppMacros();
                }
                else if (language.Equals("c", StringComparison.OrdinalIgnoreCase))
                {
                    AddPredefinedCMacros();
                }
            }

            /// <summary>
            /// Adds predefined C macros as per MSVC documentation
            /// <see cref="https://learn.microsoft.com/en-us/cpp/preprocessor/predefined-macros?view=msvc-170"/>
            /// </summary>
            /// <remarks>These are sane defaults at the time of writing, but it'd be great to read them from the input data</remarks>
            private void AddPredefinedCMacros()
            {
                Defines["__STDC_HOSTED__"] = "1";
                Defines["_INTEGRAL_MAX_BITS"] = "64";
                Defines["_IS_ASSIGNABLE_NOCHECK_SUPPORTED"] = "1";
                Defines["_MSVC_EXECUTION_CHARACTER_SET"] = "1252";
                Defines["_MSVC_TRADITIONAL"] = "0";
                Defines["_MSVC_WARNING_LEVEL"] = "1L";
                Defines["_M_X64"] = "100";
                Defines["_M_AMD64"] = "100";
                Defines["_MSC_BUILD"] = "0";
                Defines["_MSC_EXTENSIONS"] = "1";
                Defines["_MSC_FULL_VER"] = "193933523";
                Defines["_MSC_VER"] = "1939";
                Defines["_MT"] = "1";
                Defines["_WIN32"] = "1";
                Defines["_WIN64"] = "1";
            }

            /// <summary>
            /// Adds predefined C++ macros as per MSVC documentation
            /// <see cref="https://learn.microsoft.com/en-us/cpp/preprocessor/predefined-macros?view=msvc-170"/>
            /// </summary>
            /// <remarks>These are sane defaults at the time of writing, but it'd be great to read them from the input data</remarks>
            private void AddPredefinedCppMacros()
            {
                AddPredefinedCMacros();
                Defines["__cplusplus"] = "202002L"; // C++20 (see if we can load it from  compiler args instead)
                Defines["__BOOL_DEFINED"] = "1";
                Defines["__cpp_aggregate_nsdmi"] = "201304L";
                Defines["__cpp_alias_templates"] = "200704L";
                Defines["__cpp_attributes"] = "200809L";
                Defines["__cpp_binary_literals"] = "201304L";
                Defines["__cpp_constexpr"] = "201304L";
                Defines["__cpp_decltype"] = "200707L";
                Defines["__cpp_decltype_auto"] = "201304L";
                Defines["__cpp_delegating_constructors"] = "200604L";
                Defines["__cpp_enumerator_attributes"] = "201411L";
                Defines["__cpp_generic_lambdas"] = "201304L";
                Defines["__cpp_inheriting_constructors"] = "200802L";
                Defines["__cpp_init_captures"] = "201304L";
                Defines["__cpp_initializer_lists"] = "200806L";
                Defines["__cpp_lambdas"] = "200907L";
                Defines["__cpp_namespace_attributes"] = "201411L";
                Defines["__cpp_nsdmi"] = "200809L";
                Defines["__cpp_range_based_for"] = "200907L";
                Defines["__cpp_raw_strings"] = "200710L";
                Defines["__cpp_ref_qualifiers"] = "200710L";
                Defines["__cpp_return_type_deduction"] = "201304L";
                Defines["__cpp_rtti"] = "199711L";
                Defines["__cpp_rvalue_references"] = "200610L";
                Defines["__cpp_sized_deallocation"] = "201309L";
                Defines["__cpp_static_assert"] = "200410L";
                Defines["__cpp_threadsafe_static_init"] = "200806L";
                Defines["__cpp_unicode_characters"] = "200704L";
                Defines["__cpp_unicode_literals"] = "200710L";
                Defines["__cpp_user_defined_literals"] = "200809L";
                Defines["__cpp_variable_templates"] = "201304L";
                Defines["__cpp_variadic_templates"] = "200704L";
                Defines["__STDCPP_DEFAULT_NEW_ALIGNMENT__"] = "16ull";
                Defines["__STDCPP_THREADS__"] = "1";
                Defines["_CONSTEXPR_CHAR_TRAITS_SUPPORTED"] = "1";
                Defines["_CRT_USE_BUILTIN_OFFSETOF"] = "1";
                Defines["_HAS_CHAR16_T_LANGUAGE_SUPPORT"] = "1";
                Defines["_MSVC_CONSTEXPR_ATTRIBUTE"] = "1";
                Defines["_MSVC_LANG"] = "202002L";
                Defines["_NATIVE_NULLPTR_SUPPORTED"] = "1";
                Defines["_NATIVE_WCHAR_T_DEFINED"] = "1";
                Defines["_WCHAR_T_DEFINED"] = "1";
                Defines["_CPPRTTI"] = "1";
            }


            [JsonPropertyName("language")]
            public string Language { get; set; }

            [JsonPropertyName("sources")]
            public List<string> Sources
            {
                get
                {
                    return SourcesSet.ToList();
                }
                set
                {
                    SourcesSet.AddRange(value);
                }
            }

            public HashSet<string> SourcesSet = new HashSet<string>(comparer: OperatingSystemHelper.IsUnixOS ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            [JsonPropertyName("includepaths")]
            public List<string> IncludePaths
            {
                get
                {
                    return IncludePathsSet.ToList();
                }
                set
                {
                    IncludePathsSet.AddRange(value);
                }
            }

            public HashSet<string> IncludePathsSet = new HashSet<string>(comparer: OperatingSystemHelper.IsUnixOS ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            [JsonPropertyName("defines")]
            public Dictionary<string, string> Defines { get; set; } = new Dictionary<string, string>();
        }
        #endregion

        public override int Analyze()
        {
            if (!m_processExecutionMonitoringReportedSeen)
            {
                Console.Error.WriteLine("ERROR: No process execution monitoring data was found in the log. Ensure you run bxl with flags: /logProcesses /logObservedFileAccesses /incremental-");
                return 1;
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(m_astredProject, options);
            File.WriteAllText(m_outputFilePath, json);

            return 0;
        }

        public override void BxlInvocation(BxlInvocationEventData data)
        {
            if (m_useOriginalPaths)
            {
                var conf = data.Configuration.Logging;
                m_pathTranslator = GetPathTranslator(conf.SubstSource, conf.SubstTarget, PathTable);
            }
        }

        /// <summary>
        /// Callback when the execution analyzer hits an event containing the process and file monitoring
        /// results from a pip.
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (data.ReportedProcesses.Count != 0)
            {
                m_processExecutionMonitoringReportedSeen = true;
            }

            // Check for relevant compilers
            foreach (var process in data.ReportedProcesses)
            {
                // Cl.exe
                if (Path.GetFileName(process.Path).Equals("cl.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Unit unit = new Unit("cpp");
                    ExtractClExeIncludesAndDefines(NormalizeCommandLine(process),  unit);
                    ExtractCompilerFileAccesses(data, unit);
                    m_astredProject.Units.Add(unit);
                }
                // csc.exe (C#)
                else if (IsCscInvocation(process))
                {
                    Unit unit = new Unit("C#");
                    ExtractCompilerFileAccesses(data, unit);
                    ExtractFromCscCommandLine(NormalizeCommandLine(process), unit);
                    m_astredProject.Units.Add(unit);
                }
                // TODO - add tsc.exe (TypeScript) etc.
            }
        }

        /// <summary>
        /// Identifies whether a process invocation is the C# compiler (csc.exe)
        /// </summary>
        /// <remarks>
        /// Csc.exe can be invoked directly or via dotnet.exe
        /// </remarks>
        internal bool IsCscInvocation(ReportedProcess process)
        {
            if (process.Path.EndsWith(Path.DirectorySeparatorChar + "csc.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (process.Path.EndsWith(Path.DirectorySeparatorChar + "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var commandLine = NormalizeCommandLine(process);
                if (commandLine.Count > 2 && commandLine[1].EndsWith("csc.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly HashSet<string> s_compilerHeaderFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".h",
            ".hpp",
            ".hxx",
            ".inc",
            ".inl",
        };

        private static readonly HashSet<string> s_compilerSourceFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".h",
            ".hpp",
            ".cpp",
            ".cxx",
            ".c",
            ".cs",
        };

        /// <summary>
        /// Adds observed file accesses from a compiler invocation as either a header or a source file
        /// </summary>
        /// <remarks>
        /// This is based on file extension conventions
        /// </remarks>
        internal void ExtractCompilerFileAccesses(ProcessExecutionMonitoringReportedEventData data, Unit unit)
        {
            foreach (var access in data.ReportedFileAccesses)
            {
                // The file access may be represented on the ManifestPath if it as a statically declared dependency
                // or in the Path field for a dynamically declared dependency.
                string path = access.GetPath(PathTable);
                if (!string.IsNullOrEmpty(path))
                {
                    var translatedPath = TranslatePath(path);

                    // Add include paths for accessed header files. Extracting them from cl.exe's command line is not sufficient since
                    // header references are not always specified on he comman dline
                    if (s_compilerHeaderFileExtensions.Contains(Path.GetExtension(translatedPath)))
                    {
                        // Check to see whether the accessed header is under an existing include path and omit it if already covered
                        if (!unit.IncludePathsSet.Contains(Path.GetDirectoryName(translatedPath)))
                        {
                            unit.IncludePathsSet.Add(translatedPath);
                        }
                    }
                    else if (s_compilerSourceFileExtensions.Contains(Path.GetExtension(translatedPath)))
                    {
                        // Add source files
                        unit.SourcesSet.Add(translatedPath);
                    }
                }
            }
        }

        /// <summary>
        /// Extracts include paths and defines from a cl.exe command line
        /// </summary>
        internal void ExtractClExeIncludesAndDefines(List<string> commandLine, Unit unit)
        {
            for (int i = 0; i < commandLine.Count; i++)
            {
                var arg = commandLine[i];

                // /I<dir> or /I <dir>
                if (arg.StartsWith("/I", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-I", StringComparison.OrdinalIgnoreCase))
                {
                    string path;
                    if (arg.Length > 2)
                    {
                        path = arg.Substring(2).Trim();
                    }
                    else if (i + 1 < commandLine.Count)
                    {
                        path = commandLine[++i].Trim();
                    }
                    else
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        unit.IncludePathsSet.Add(TranslatePath(path));
                    }
                }
                // /D<name>[=|#]<text>
                else if (arg.StartsWith("/D", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-D", StringComparison.OrdinalIgnoreCase))
                {
                    string def = arg.Substring(2);
                    string name, value;
                    int eq = def.IndexOf('=');
                    int hash = def.IndexOf('#');
                    int sep = (eq >= 0 && hash >= 0) ? Math.Min(eq, hash) : Math.Max(eq, hash);

                    if (sep >= 0)
                    {
                        name = def.Substring(0, sep);
                        value = def.Substring(sep + 1);
                    }
                    else if ((sep = def.IndexOf(':')) >= 0) // Handle case of /DNAME:VALUE
                    {
                        name = def.Substring(0, sep);
                        value = def.Substring(sep + 1);
                    }
                    else
                    {
                        name = def;
                        value = "1";
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        unit.Defines[name] = value;
                    }
                }
            }
            // NOTE: The sorting isn't needed for functionality, but it makes the output more deterministic for testing and comparison, and the performance hit is negligible
            unit.Defines = unit.Defines.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        internal void ExtractFromCscCommandLine(List<string> normalizedArgs, Unit unit)
        {
            // Look through the command line arguments for compiler defines and source files
            for (int i = 0; i < normalizedArgs.Count; i++)
            {
                var arg = normalizedArgs[i];

                // Handle /define: or -define: or /d: or -d: options for preprocessor symbols
                if (arg.StartsWith("/define:", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("-define:", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("/d:", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("-d:", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the part after the colon
                    int colonIndex = arg.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex < arg.Length - 1)
                    {
                        string defines = arg.Substring(colonIndex + 1);

                        // Split by semicolon as multiple defines can be specified
                        var defineParts = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var define in defineParts)
                        {
                            // Each define can be NAME or NAME=VALUE
                            var equalIndex = define.IndexOf('=');
                            if (equalIndex >= 0)
                            {
                                string name = define.Substring(0, equalIndex).Trim();
                                string value = define.Substring(equalIndex + 1).Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    unit.Defines[name] = value;
                                }
                            }
                            else
                            {
                                string name = define.Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    unit.Defines[name] = string.Empty;
                                }
                            }
                        }
                    }
                }
                // Skip other compiler options (arguments starting with / or -)
                else if (arg.StartsWith("/", StringComparison.Ordinal) ||
                         arg.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }
                // Check if this is a .cs source file
                else if (arg.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    unit.SourcesSet.Add(TranslatePath(arg));
                }
            }
        }

        private string TranslatePath(string path) => m_pathTranslator?.Translate(path) ?? path;

        private static PathTranslator GetPathTranslator(AbsolutePath substSource, AbsolutePath substTarget, PathTable pathTable)
        {
            return substTarget.IsValid && substSource.IsValid
                ? new PathTranslator(substTarget.ToString(pathTable), substSource.ToString(pathTable))
                : null;
        }

        /// <summary>
        /// Normalizes the command line for the process by:
        /// 1. Handling any quoting in arguments
        /// 2. Expanding any response files
        /// </summary>
        /// <returns>List of each command line option</returns>
        internal List<string> NormalizeCommandLine(ReportedProcess process)
        {
            List<string> result = new List<string>();
            var input = process.ProcessArgs;

            if (string.IsNullOrEmpty(input))
            {
                return result;
            }

            // Parse command line with quote handling
            var args = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                args.Add(current.ToString());
            }

            // Process each argument, expanding response files
            foreach (var arg in args)
            {
                if (arg.StartsWith('@') && arg.Length > 1)
                {
                    // Response file - attempt to expand it
                    string responseFilePath = arg.Substring(1);

                    if (!File.Exists(responseFilePath))
                    {
                        var translated = TranslatePath(responseFilePath);
                        if (File.Exists(translated))
                        {
                            responseFilePath = translated;
                        }
                    }

                    if (File.Exists(responseFilePath))
                    {
                        // Process the response file
                        // According to documentation https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-response-files?view=vs-2022
                        // response files should contain one argument per line OR be whitespace separated if only a single line
                        string[] responseFileContent = File.ReadAllLines(responseFilePath);
                        if (!responseFileContent.Any()) 
                        {
                            // Empty response file - skip
                            continue;
                        }
                        if (responseFileContent.Length > 1)
                        {
                            // Multiple lines - each line is an argument
                            result.AddRange(responseFileContent.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()));
                        }
                        else
                        {
                            // Single line - parse with whitespace and quote handling
                            var argumentBuilder = new System.Text.StringBuilder();
                            inQuotes = false;

                            foreach (var c in responseFileContent.Single())
                            {
                                inQuotes = c == '"' ? !inQuotes : inQuotes;
                                if (!inQuotes && char.IsWhiteSpace(c))
                                {
                                    if (argumentBuilder.Length > 0)
                                    {
                                        result.Add(argumentBuilder.ToString());
                                        argumentBuilder.Clear();
                                    }
                                }
                                else
                                {
                                    argumentBuilder.Append(c);
                                }
                            }

                            if (argumentBuilder.Length > 0)
                            {
                                result.Add(argumentBuilder.ToString());
                            }
                        }
                    }
                    else
                    {
                        // If file doesn't exist, add the argument as-is
                        Console.WriteLine($"Warning: Response file '{responseFilePath}' not found. Adding argument as-is.");
                        result.Add(arg);
                    }
                }
                else
                {
                    result.Add(arg);
                }
            }

            return result;
        }
    }
}
