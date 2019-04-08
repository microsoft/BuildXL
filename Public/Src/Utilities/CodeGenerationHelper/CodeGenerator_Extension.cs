// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.CodeGenerationHelper
{
    /// <summary>
    /// General helpers to generate C# source code.
    /// </summary>
    public sealed partial class CodeGenerator
    {
        /// <summary>
        /// Returns an instance name from type name. The convention is to make the first letter in the type name lower case.
        /// </summary>
        /// <param name="typeName">The type name.</param>
        /// <returns>The instance name.</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo", MessageId = "System.Char.ToLower(System.Char)")]
        public static string GetInstanceNameFromType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return string.Empty;
            }

            char first = char.ToLower(typeName[0]);
            return string.Concat(first, typeName.Remove(0, 1));
        }

        /// <summary>
        /// Concats a list of argument using ", " as separator.
        /// </summary>
        /// <param name="args">An enumerable list of arguments</param>
        /// <returns>A string of comma separated arguments.</returns>
        public static string JoinCallArguments(IEnumerable<string> args)
        {
            Contract.Requires(args != null);
            return string.Join(", ", args);
        }

        /// <summary>
        /// Writes a given code content into a csharp file.
        /// </summary>
        /// <param name="outputDir">The output directory.</param>
        /// <param name="name">The name of csharp file.</param>
        /// <param name="content">The content of chsharp file.</param>
        /// <returns>The path of written file.</returns>
        public static string WriteCSharpCodeToFile(string outputDir, string name, string content)
        {
            Contract.Requires(!string.IsNullOrEmpty(outputDir));
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(!string.IsNullOrEmpty(content));

            Directory.CreateDirectory(outputDir);
            string filePath = Path.Combine(outputDir, name + ".cs");

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(filePath, FileMode.Create);
                using (var writer = new StreamWriter(fileStream))
                {
                    fileStream = null;
                    writer.Write(content);
                    writer.Flush();
                }
            }
            finally
            {
                if (fileStream != null)
                {
                    fileStream.Dispose();
                }
            }

            return filePath;
        }
    }
}
