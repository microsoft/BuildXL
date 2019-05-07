// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace AppHostPatcher
{
    /// <summary>
    /// Embeds the a specified app name into the target OS AppHost binary
    /// </summary>
    public class AppHostPatcher
    {
        /// <nodoc />
        public string AppHostSourcePath { get; set; }

        /// <nodoc />
        public string AppHostDestinationDirectoryPath { get; set; }

        /// <nodoc />
        public string AppBinaryName { get; set; }

        // See: https://github.com/dotnet/sdk/blob/4e90cac1d4b8743e39ed6945677020f9d4cbfd81/src/Tasks/Microsoft.NET.Build.Tasks/AppHost.cs#L20
        // Basically the un-patched apphost from the official Nuget package contains this string as a placeholder
        private static readonly string s_placeHolder = "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";
        private static readonly byte[] s_bytesToSearch = Encoding.UTF8.GetBytes(s_placeHolder);

        // See: https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm
        private static int[] ComputeKMPFailureFunction(byte[] pattern)
        {
            int[] table = new int[pattern.Length];
            if (pattern.Length >= 1)
            {
                table[0] = -1;
            }
            if (pattern.Length >= 2)
            {
                table[1] = 0;
            }

            int pos = 2;
            int cnd = 0;
            while (pos < pattern.Length)
            {
                if (pattern[pos - 1] == pattern[cnd])
                {
                    table[pos] = cnd + 1;
                    cnd++;
                    pos++;
                }
                else if (cnd > 0)
                {
                    cnd = table[cnd];
                }
                else
                {
                    table[pos] = 0;
                    pos++;
                }
            }

            return table;
        }

        // See: https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm
        private static int KMPSearch(byte[] pattern, byte[] bytes)
        {
            int m = 0;
            int i = 0;
            int[] table = ComputeKMPFailureFunction(pattern);

            while (m + i < bytes.Length)
            {
                if (pattern[i] == bytes[m + i])
                {
                    if (i == pattern.Length - 1)
                    {
                        return m;
                    }
                    i++;
                }
                else
                {
                    if (table[i] > -1)
                    {
                        m = m + i - table[i];
                        i = table[i];
                    }
                    else
                    {
                        m++;
                        i = 0;
                    }
                }
            }

            return -1;
        }

        private static void SearchAndReplace(byte[] array, byte[] searchPattern, byte[] patternToReplace)
        {
            int offset = KMPSearch(searchPattern, array);
            if (offset < 0)
            {
                throw new Exception();
            }

            patternToReplace.CopyTo(array, offset);

            if (patternToReplace.Length < searchPattern.Length)
            {
                for (int i = patternToReplace.Length; i < searchPattern.Length; i++)
                {
                    array[i + offset] = 0x0;
                }
            }
        }

        /// <nodoc />
        protected int ExecuteCore(string unpatchedAppHostPath, string hostedFilePath)
        {
            var hostExtension = Path.GetExtension(unpatchedAppHostPath);
            var appBaseName = Path.GetFileNameWithoutExtension(hostedFilePath);

            var bytesToWrite = Encoding.UTF8.GetBytes(Path.GetFileName(hostedFilePath));

            var destinationDirectory = Path.GetFullPath("Output");
            var patchedAppHostPath = Path.Combine(destinationDirectory, $"{appBaseName}{hostExtension}");

            if (bytesToWrite.Length > 1024)
            {
                throw new Exception("Destination file name not supported!");
            }

            var array = File.ReadAllBytes(unpatchedAppHostPath);
            SearchAndReplace(array, s_bytesToSearch, bytesToWrite);

            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            // Copy unpatchedHostFilePath to patchedAppHostPath so it inherits the same attributes and permissions.
            File.Copy(unpatchedAppHostPath, patchedAppHostPath);

            // Re-write patchedAppHostPath with the proper contents.
            using (FileStream fs = new FileStream(patchedAppHostPath, FileMode.Truncate, FileAccess.ReadWrite, FileShare.Read))
            {
                fs.Write(array, 0, array.Length);
            }

            return 0;
        }

        /// <nodoc />
        public static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: AppHostPatcher absolute_path_to_unpatched_apphost absolute_path_to_hosted_binary");
                return 1;
            }

            var patcher = new AppHostPatcher();
            return patcher.ExecuteCore(args[0], args[1]);
        }
    }
}
