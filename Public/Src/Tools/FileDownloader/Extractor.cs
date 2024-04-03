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
using System.Collections.Generic;
using System.Linq;

namespace Tool.Download
{
    /// <summary>
    /// Extracts a given file with different formats (zip, tar, etc.)
    /// </summary>
    internal sealed class Extractor : ToolProgram<ExtractorArgs>
    {
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

            // Need to set the execute permissions bit for all the extracted files.
            SetExecutePermissionsForExtractedFiles(target);
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
        /// This method is used to set the execute permissions bit for the extracted files.
        /// </summary>
        /// <remarks>
        /// The reason for doing this being:
        /// ZIP files and other archive formats may not preserve the executable bit, leading to lost permissions upon extraction.
        /// Files are often transited through Windows OS, where these executable permissions are not natively supported, potentially stripping these permissions.
        /// Without execute permissions, files like node.exe won't run after they are retrieved via DownloadResolver, impacting the build. Given the difficulty in identifying executables, all files are granted execute permissions to avoid this issue.
        /// Also this change also makes the DownloadResolver to be reliably used by our customers.
        /// </remarks>
        private void SetExecutePermissionsForExtractedFiles(string target)
        {
            if (OperatingSystemHelper.IsLinuxOS)
            {
                var files = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    _ = FileUtilities.SetExecutePermissionIfNeeded(file);
                }
            }
        }
    }
}