// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using static BuildXL.Interop.Unix.IO;
using System.Collections.Generic;

namespace Tool.Download
{
    /// <summary>
    /// Extracts a given file with different formats (zip, tar, etc.)
    /// </summary>
    internal sealed class Extractor : ToolProgram<ExtractorArgs>
    {
        private static readonly Dictionary<string, string> packagesToBeChecked = new Dictionary<string, string>
        {
            {"NodeJs.linux-x64",  "node-v18.6.0-linux-x64/bin/node"},
            {"YarnTool", "yarn-v1.22.19/bin/yarn"}
        };

        private Extractor() : base("Extractor")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new Extractor().MainHandler(arguments);
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out ExtractorArgs arguments)
        {
            try
            {
                arguments = new ExtractorArgs(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetLogEventMessage());
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(ExtractorArgs arguments)
        {
            return TryExtractToDisk(arguments) ? 0 : 1;
        }

        private bool TryExtractToDisk(ExtractorArgs arguments)
        {
            var archive = arguments.PathToFileToExtract;
            var target = arguments.ExtractDirectory;

            try
            {
                FileUtilities.DeleteDirectoryContents(target, false);
                FileUtilities.CreateDirectory(target);
            }
            catch (BuildXLException e)
            {
                ErrorExtractingArchive(archive, target, e.Message);
                return false;
            }

            switch (arguments.ArchiveType)
            {
                case DownloadArchiveType.Zip:
                    try
                    {
                        // SharpZipLib does not work well on mac and nested files are not properly handled when the zip file is constructed on Windows (with backslashes)
                        System.IO.Compression.ZipFile.ExtractToDirectory(archive, target);
                    }
                    catch (Exception e) when (e is IOException || e is DirectoryNotFoundException || e is PathTooLongException)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Gzip:
                    try
                    {
                        var targetFile = Path.Combine(target, Path.GetFileNameWithoutExtension(arguments.PathToFileToExtract));

                        using (var reader = new StreamReader(arguments.PathToFileToExtract))
                        using (var gzipStream = new GZipInputStream(reader.BaseStream))
                        using (var output = FileUtilities.CreateFileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            byte[] buffer = new byte[4096];
                            StreamUtils.Copy(gzipStream, output, buffer);
                        }
                    }
                    catch (GZipException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Tar:
                    try
                    {
                        using (var reader = new StreamReader(arguments.PathToFileToExtract))
                        using (var tar = TarArchive.CreateInputTarArchive(reader.BaseStream, nameEncoding: null))
                        {
                            tar.ExtractContents(target);
                        }
                    }
                    catch (TarException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Tgz:
                    try
                    {
                        using (var reader = new StreamReader(arguments.PathToFileToExtract))
                        using (var gzipStream = new GZipInputStream(reader.BaseStream))
                        using (var tar = TarArchive.CreateInputTarArchive(gzipStream, nameEncoding: null))
                        {
                            tar.ExtractContents(target);
                            if (OperatingSystemHelper.IsLinuxOS)
                            {
                                foreach (var packageName in packagesToBeChecked.Keys)
                                {
                                    if (target.Contains(packageName))
                                    {
                                        if (!CheckForNodePermissions(target, packagesToBeChecked[packageName]))
                                        {
                                            return false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (GZipException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }
                    catch (TarException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.File:
                    Console.WriteLine("Specified download archive type is 'File'. Nothing to extract.");
                    return true;
                default:
                    throw Contract.AssertFailure($"Unexpected archive type '{arguments.ArchiveType}'");
            }

            try
            {
                if (!FileUtilities.DirectoryExistsNoFollow(target))
                {
                    ErrorNothingExtracted(archive, target);
                    return false;
                }
            }
            catch (BuildXLException e)
            {
                ErrorExtractingArchive(archive, target, e.Message);
                return false;
            }

            return true;
        }

        private void ErrorExtractingArchive(string archive, string target, string message)
        {
            Console.Error.WriteLine($"Error occured trying to extract archive  '{archive}' to '{target}': {message}.");
        }

        private void ErrorNothingExtracted(string archive, string target)
        {
            Console.Error.WriteLine($"Error occured trying to extract archive. Nothing was extracted from '{archive}' to '{target}.'");
        }

        /// <summary>
        /// This method is used to check if the execute permission bit has been set or not.
        /// </summary>
        /// <remarks>
        /// In the method below we are checking this specifically for Node package in linux, as that was causing the issue.
        /// TODO: Need to remove this hack once the bug is fixed. Refer bug https://dev.azure.com/mseng/1ES/_workitems/edit/2073919 for further information.
        /// </remarks>
        private bool CheckForNodePermissions(string target, string relativePath)
        {
            string fullPathForExecutableFile = Path.Combine(target, relativePath);

            if (File.Exists(fullPathForExecutableFile))
            {
                var mode = GetFilePermissionsForFilePath(fullPathForExecutableFile, false);
                if (mode < 0)
                {
                    Console.Error.WriteLine($"Failed to retrieve file permissions for : {fullPathForExecutableFile}");
                    return false;
                }
                else
                {
                    // Check if the execute file permission bit has been set or not.
                    var filePermissions = checked((FilePermissions)mode);
                    if ((filePermissions & FilePermissions.S_IXUSR) == FilePermissions.S_IXUSR)
                    {
                        Console.Error.WriteLine($"File : {fullPathForExecutableFile} does not have the execute bit set for the user account");
                        return false;
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"Failed to find the file - {relativePath} at the expected location: {fullPathForExecutableFile}");
                return false;
            }

            return true;
        }
    }
}

