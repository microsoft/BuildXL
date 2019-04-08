// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Sdk.FileSystem;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Class for writing DScript tests.
    /// </summary>
    public sealed class DsTestWriter
    {
        /// <summary>
        /// Configuration file writer.
        /// </summary>
        /// <remarks>
        /// Currently the test writer only supports a single configuration writer.
        /// </remarks>
        public DsConfigFileWriter ConfigWriter { get; }

        /// <summary>
        /// Extra files to be added.
        /// </summary>
        /// <remarks>
        /// These are mappings from relative paths to contents.
        /// </remarks>
        public Dictionary<string, string> ExtraFiles { get; }

        /// <summary>
        /// Absolute path to which all files will be written.
        /// </summary>
        public string RootPath { get; }

        public IMutableFileSystem FileSystem { get; }

        public PathTable PathTable { get; }

        /// <nodoc />
        public DsTestWriter(string rootPath = null)
        {
            ConfigWriter = new DsConfigFileWriter();
            ExtraFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RootPath = rootPath;
        }

        /// <nodoc />
        public DsTestWriter(PathTable pathTable, IMutableFileSystem pathBasedFileSystem= null, string rootPath = null) : this(rootPath)
        {
            FileSystem = pathBasedFileSystem?? new InMemoryFileSystem(pathTable);
            PathTable = pathTable;
        }

        /// <nodoc />
        public static DsTestWriter Create(string sourceRoot, IEnumerable<BuildSpec> buildSpecs, IMutableFileSystem fileSystem)
        {
            // Putting source into the nested folder in order to avoid file access violation when writing package files outside the source cone.
            var testWriter = new DsTestWriter(fileSystem.GetPathTable(), fileSystem, sourceRoot);
            foreach (var spec in buildSpecs)
            {
                if (spec.FileName == Names.ConfigDsc)
                {
                    testWriter.ConfigWriter.SetConfigContent(spec.Content);
                    testWriter.ConfigWriter.UseLegacyConfigExtension();
                }
                else if (spec.FileName == Names.ConfigBc)
                {
                    testWriter.ConfigWriter.SetConfigContent(spec.Content);
                }
                else
                {
                    testWriter.ConfigWriter.AddBuildSpec(spec.FileName, spec.Content);
                }
            }

            return testWriter;
        }

        /// <summary>
        /// Writes the test into <paramref name="directory" />.
        /// </summary>
        /// <param name="directory">Directory for writing the test.</param>
        /// <param name="cleanExistingDirectory">Clean existing directory if set to true.</param>
        public void Write(string directory, bool cleanExistingDirectory = true)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directory));
            Contract.Requires(PathTable!= null);

            // If the root path is specified, it overrides the given directory
            if (RootPath != null)
            {
                directory = RootPath;
            }
            
            if (cleanExistingDirectory && FileSystem.Exists(AbsolutePath.Create(PathTable, directory)))
            {
                // TODO: delete the directory.
                // TODO: Add this to interface
                //global::BuildXL.Native.IO.FileUtilities.DeleteDirectoryContents(directory);
                
            }
            FileSystem.CreateDirectory(AbsolutePath.Create(PathTable, directory));
            ConfigWriter.Write(directory, this);

            foreach (var extraFile in ExtraFiles)
            {
                WriteFile(Path.Combine(directory, extraFile.Key), extraFile.Value);
            }
        }

        /// <summary>
        /// Adds an extra file, and override existing one if exists.
        /// </summary>
        /// <param name="relativePath">Relative path to which the content is to be written.</param>
        /// <param name="content">The content.</param>
        public DsTestWriter AddExtraFile(string relativePath, string content)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));
            Contract.Requires(content != null);

            ExtraFiles[relativePath] = content;
            return this;
        }

        /// <summary>
        /// Writes a content to a file specified by <paramref name="fullPath"/>.
        /// </summary>
        /// <param name="fullPath">Path to a file to which the content is written.</param>
        /// <param name="content">The content to be written.</param>
        public void WriteFile(string fullPath, string content)
        {

            Contract.Requires(!string.IsNullOrWhiteSpace(fullPath));
            Contract.Requires(content != null);
            Contract.Requires(Path.IsPathRooted(fullPath));

            var directory = Path.GetDirectoryName(fullPath);
            Contract.Assume(directory != null);

            //Directory.CreateDirectory(directory);
            FileSystem.CreateDirectory(AbsolutePath.Create(PathTable, directory));
            //File.WriteAllText(fullPath, content);
            FileSystem.WriteAllText(fullPath, content);
        }

        /// <summary>
        /// Gets all files known by this test writer.
        /// </summary>
        /// <returns>All files known by this test writer.</returns>
        public IEnumerable<Tuple<string, string>> GetAllFiles()
        {
            var files = new List<Tuple<string, string>>();
            files.AddRange(ConfigWriter.GetAllFiles());
            files.AddRange(ExtraFiles.Select(kvp => Tuple.Create(kvp.Key, kvp.Value)));

            return files;
        }
    }
}
