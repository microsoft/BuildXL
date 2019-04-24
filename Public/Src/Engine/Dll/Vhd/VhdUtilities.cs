// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace BuildXL.Engine
{
    internal static class VhdUtilities
    {
        public static bool Mount(string vhdFilePath, long sizeMb, string mountFolder)
        {
            File.Delete(vhdFilePath);
            Directory.CreateDirectory(mountFolder);

            return RunDiskPart(
                GetEmbeddedResourceFile("BuildXL.Engine.Vhd.CreateSnapVhd.txt")
                    .Replace("{vhdPath}", vhdFilePath)
                    .Replace("{sizeMb}", sizeMb.ToString(CultureInfo.InvariantCulture))
                    .Replace("{mountPath}", mountFolder)
            );
        }

        public static bool Dismount(string vhdFilePath, string mountFolder)
        {
            bool result = RunDiskPart(
                GetEmbeddedResourceFile("BuildXL.Engine.Vhd.DismountSnapVhd.txt")
                    .Replace("{vhdPath}", vhdFilePath)
                    .Replace("{mountPath}", mountFolder)
            );

            Directory.Delete(mountFolder);

            return result;
        }

        public static bool RunDiskPart(string scriptText)
        {
            var scriptFileName = Path.GetTempFileName();
            File.WriteAllText(scriptFileName, scriptText);
            var process = Process.Start(new ProcessStartInfo("diskpart", string.Format(CultureInfo.InvariantCulture, "/s \"{0}\"", scriptFileName))
            {
                UseShellExecute = false,
            });

            process.WaitForExit();

            File.Delete(scriptFileName);
            return process.ExitCode == 0;
        }

        /// <summary>
        /// Helper to get the string content of a resource file from the current assembly.
        /// </summary>
        /// <remarks>This unfortunately cannot be in a shared location like 'AssemblyHelpers' because on .Net Core it ignores the assembly and always tries to extract the resources from the running assembly. Even though GetManifestResourceNames() does respect it.</remarks>
        private static string GetEmbeddedResourceFile(string resourceKey)
        {
            var callingAssembly = typeof(VhdUtilities).GetTypeInfo().Assembly;
            var stream = callingAssembly.GetManifestResourceStream(resourceKey);
            if (stream == null)
            {
                Contract.Assert(false, $"Expected embedded resource key '{resourceKey}' not found in assembly {callingAssembly.FullName}. Valid resource names are: {string.Join(",", callingAssembly.GetManifestResourceNames())}");
                return null;
            }

            using (var sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }

    }
}
