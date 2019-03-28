// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;

namespace ResGen.Lite
{
    /// <summary>
    /// Class to help commandline argument parsing
    /// </summary>
    public class Arguments
    {
        /// <summary>
        /// Supported languages to generate code for
        /// </summary>
        public static readonly Dictionary<string, (string, string)> SupportedLanguages =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                {"c#", (LanguageNames.CSharp, ".cs")},
                {"cs", (LanguageNames.CSharp, ".cs")},
                {"csharp", (LanguageNames.CSharp, ".cs")},
                {"vb", (LanguageNames.VisualBasic, ".vb")},
                {"visualbasic", (LanguageNames.VisualBasic, ".vb")},
            };

        /// <summary>
        /// Supported file types for input files
        /// </summary>
        public static readonly HashSet<string> SupportedInputExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".ResW",
                ".ResX",
            };

        /// <summary>
        /// Whether the class to generate has public members. Controlled by the /publicClass.
        /// </summary>
        public bool CodeGenPublicClass { get; private set; }

        /// <summary>
        /// The Language to generate per the /str flag.
        /// </summary>
        public string CodeGenLanguage { get; private set; }

        /// <summary>
        /// The extension for the file to generate based on the language selected for the /str flag.
        /// </summary>
        public string CodeGenFileExtension { get; private set; }

        /// <summary>
        /// The filename to generate per the /str flag.
        /// </summary>
        public string CodeGenFilePath { get; private set; }

        /// <summary>
        /// The classname to generate per the /str flag.
        /// </summary>
        public string CodeGenClassName { get; private set; }

        /// <summary>
        /// The namespace to generate per the /str flag.
        /// </summary>
        public string CodeGenNamespaceName { get; private set; }

        /// <summary>
        /// The input file specified.
        /// </summary>
        public string InputFilePath { get; private set; }

        /// <summary>
        /// The output file specified.
        /// </summary>
        public string OutputFilePath { get; private set; }

        /// <summary>
        /// Parses the args and stores the state extracted in this class.
        /// </summary>
        public bool TryParse(string[] args, Action<string> reportError)
        {
            foreach (var arg in args)
            {
                if (arg.Equals("-publicClass", StringComparison.OrdinalIgnoreCase))
                {
                    CodeGenPublicClass = true;
                }
                else if (arg.StartsWith("-str:", StringComparison.OrdinalIgnoreCase))
                {
                    var strValue = arg.Substring(5);
                    var strParts = strValue.Split(new[] { ',' });
                    if (strParts.Length == 0 || strParts.Length > 5)
                    {
                        reportError("Error: Unexpected number of values for /str: argument");
                        return false;
                    }

                    if (strParts.Length > 3)
                    {
                        CodeGenFilePath = strParts[3];
                    }

                    if (strParts.Length > 2)
                    {
                        CodeGenClassName = strParts[2];
                    }

                    if (strParts.Length > 1)
                    {
                        CodeGenNamespaceName = strParts[1];
                    }

                    if (strParts.Length > 0)
                    {
                        if (!SupportedLanguages.TryGetValue(strParts[0], out var langAndExtension))
                        {
                            reportError($"Error: Unsupported language '{strParts[0]}' encountered for /str: argument. Valid values are: {string.Join(", ", SupportedLanguages.Keys)}");
                            return false;
                        }

                        CodeGenLanguage = langAndExtension.Item1;
                        CodeGenFileExtension = langAndExtension.Item2;
                    }
                }
                else if (!arg.StartsWith("-"))
                {
                    if (string.IsNullOrEmpty(InputFilePath))
                    {
                        InputFilePath = arg;
                        var sourceExtension = Path.GetExtension(arg);
                        if (!SupportedInputExtensions.Contains(sourceExtension))
                        {
                            reportError($"Error: Unsupported extension '{sourceExtension}' encountered for inputFile: '{arg}'. Only the following file formats are supported: {string.Join(",", SupportedInputExtensions)}");
                            return false;
                        }
                    }
                    else if (string.IsNullOrEmpty(OutputFilePath))
                    {
                        OutputFilePath = arg;
                        var ouputFileExtension = Path.GetExtension(arg);
                        if (!string.Equals(ouputFileExtension, ".resources", StringComparison.OrdinalIgnoreCase))
                        {
                            reportError($"Error: Unsupported extension '{ouputFileExtension}' encountered for outputFile: '{arg}'. Only '.resources' is supported.");
                            return false;
                        }
                    }
                    else
                    {
                        reportError($"Error: Both inputFile and outputFile are already specified. Encountered superfluous argument '{arg}'");
                        return false;
                    }
                }
                else
                {
                    reportError($"Error: Unsupported commandline flag '{arg}'");
                    return false;
                }
            }

            // Validate required arguments are set.
            if (string.IsNullOrEmpty(InputFilePath))
            {
                reportError($"Error: Required argument inputFile is missing");
                return false;
            }

            // Infer some defaults when generating code
            if (!string.IsNullOrEmpty(CodeGenLanguage))
            {
                if (string.IsNullOrEmpty(CodeGenFilePath))
                {
                    CodeGenFilePath = Path.GetFileNameWithoutExtension(InputFilePath) + "." + CodeGenFileExtension;
                }

                if (string.IsNullOrEmpty(CodeGenClassName))
                {
                    CodeGenClassName = Path.GetFileNameWithoutExtension(CodeGenClassName);
                }
            }

            return true;
        }
    }
}
