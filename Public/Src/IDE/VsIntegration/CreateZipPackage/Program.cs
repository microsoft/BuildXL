// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BuildXL.ToolSupport;

namespace BuildXL.IDE.CreateZipPackage
{
    /// <summary>
    /// Zips a directory
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entrypoint
        /// </summary>
        public static int Main(string[] arguments)
        {
            try
            {
                var args = new Args(arguments);

                if (args.InputDirectory != null)
                {
                    ZipFile.CreateFromDirectory(args.InputDirectory, args.OutputFile);
                }

                if (args.Files.Count != 0)
                {
                    // Normalize path
                    string root = args.OutputFilesRoot?.Replace(Path.DirectorySeparatorChar, '/');

                    // If the root is provided, make sure that it ends with the path separator.
                    root = root?.EndsWith("/") == false ? root + '/' : root;
                    using (var archive = ZipFile.Open(args.OutputFile, args.InputDirectory != null ? ZipArchiveMode.Update : ZipArchiveMode.Create))
                    {
                        // Need to add additional files to the zip
                        foreach (var file in args.Files)
                        {
                            // Normalize path
                            var filePath = file.Replace(Path.DirectorySeparatorChar, '/');

                            // In some cases the root for all the output files should be provided.
                            // For instance, we want to add 2 files from 'd:/sources'.
                            // In this case we can call this tool in the following way:
                            // Tool.CreateZipPackage.exe /inputFilesRoot:d:\sources d:\sources\1.txt d:\sources\foo\2.txt
                            // And the tool will zip these two files and will lay out them by substructing d:\sources from the file names inside the zip.
                            var outputFile = root != null ? filePath.Replace(root, "") : filePath;
                            Console.WriteLine($"Root: '{root}', file: '{filePath}', outputFile: '{outputFile}'");
                            archive.CreateEntryFromFile(filePath, outputFile, CompressionLevel.Optimal);
                        }
                    }
                }

                if (args.UseUriEncoding)
                {
                    using (var archive = ZipFile.Open(args.OutputFile, ZipArchiveMode.Update))
                    {
                        foreach (var entry in archive.Entries.ToList())
                        {
#if FEATURE_EXTENDED_ATTR
                            if (args.FixUnixPermissions)
                            {
                                // The ZIP specification describes ExternalAttributes as OS specific region, used to
                                // set general file attributes - we apply 'execution' permissions to every file here. The
                                // magic constant comes from inspecting the bits set by the zip tool on Unix system for files
                                // with the 'exec' bit set and then being added to an archive.
                                entry.ExternalAttributes = -2115158016;
                            }
#endif
                            Console.WriteLine(entry.FullName);
                            var newFullName = entry.FullName.Replace('\\', '/');
                            if (entry.FullName != newFullName)
                            {
                                var newEntry = archive.CreateEntry(newFullName);
                                using (var es = entry.Open())
                                using (var nes = newEntry.Open())
                                {
                                    es.CopyTo(nes);
                                }

                                entry.Delete();
                            }
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                string message = ex is InvalidArgumentException ? ex.Message : ex.ToString();
                Console.Error.WriteLine(message);
                Args.WriteHelp();
                return 1;
            }
        }
    }
}
