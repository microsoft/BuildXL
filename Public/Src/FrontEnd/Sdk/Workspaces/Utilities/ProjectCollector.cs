// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Collector of all projects under a DScript module. Examines the disk to enumerate project files that are owned by a package.
    /// </summary>
    public static class ProjectCollector
    {
        /// <summary>
        /// If a package does not specify its projects explicitly (i.e. 'Projects'  is empty), we compute
        /// all the projects explicitly, provided the <param name="fileSystem"/> and path to module (<param name="pathToPackageDirectory"/>).
        /// This is done only when the workspace is in use, since the workspace needs to parse the full build extent upfront.
        /// </summary>
        /// <remarks>
        /// This method assumes directory tracking is done by the underlying file system
        /// </remarks>
        public static List<AbsolutePath> CollectAllProjects(IFileSystem fileSystem, AbsolutePath pathToPackageDirectory)
        {
            var projects = new List<AbsolutePath>();

            // We first collect all projects in the package current directory
            CollectAllPathsToProjects(fileSystem, pathToPackageDirectory, projects);

            // Now we recursively go down the directory structure.
            CollectAllPathsToProjectsRecursively(fileSystem, pathToPackageDirectory, projects);

            return projects;
        }

        /// <summary>
        /// Whether <param name="filePath"/> (created with <param name="pathTable"/> looks like a DScript project file.
        /// </summary>
        public static bool IsPathToProjectFile(AbsolutePath filePath, PathTable pathTable)
        {
            var fileName = filePath.GetName(pathTable).ToString(pathTable.StringTable);

            return ExtensionUtilities.IsNonConfigurationFile(fileName);
        }

        private static bool IsWellKnownConfigurationFileExists(AbsolutePath directory, PathTable pathTable, IFileSystem fileSystem)
        {
            foreach (var path in Names.WellKnownConfigFileNames)
            {
                var absolutePath = directory.Combine(pathTable, path);
                if (fileSystem.Exists(absolutePath))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Recursively goes down each directory and collects project files. The search stops in directories that contain
        /// a project configuration or a configuration file.
        /// </summary>
        private static void CollectAllPathsToProjectsRecursively(
            IFileSystem fileSystem, 
            AbsolutePath pathToPackageDirectory,
            List<AbsolutePath> projects)
        {
            var pathTable = fileSystem.GetPathTable();

            Action<AbsolutePath, Action<AbsolutePath>> collectPackages = (directory, adder) =>
            {
                if (!IsWellKnownConfigurationFileExists(directory, pathTable, fileSystem))
                {
                    CollectAllPathsToProjects(fileSystem, directory, projects);
                    var subDirectories = fileSystem.EnumerateDirectories(directory);
                    foreach (var subDirectory in subDirectories)
                    {
                        adder(subDirectory);
                    }
                }
            };

            ParallelAlgorithms.WhileNotEmpty(
                fileSystem.EnumerateDirectories(pathToPackageDirectory),
                collectPackages);
        }

        private static void CollectAllPathsToProjects(IFileSystem fileSystem, AbsolutePath pathToPackageDirectory, List<AbsolutePath> projects)
        {
            var pathTable = fileSystem.GetPathTable();
            var files =
                fileSystem.EnumerateFiles(pathToPackageDirectory)
                    .Where(pathToFile => ExtensionUtilities.IsNonConfigurationFile(Path.GetFileName(pathToFile.ToString(pathTable))));

            foreach (var file in files)
            {
                AddProjectSynchronized(projects, file);
            }
        }

        /// <summary>
        /// Adds synchronized an element to a list. Using a concurrent structure is more heavyweight when just adding is needed.
        /// </summary>
        private static void AddProjectSynchronized(List<AbsolutePath> projects, AbsolutePath project)
        {
            lock (projects)
            {
                projects.Add(project);
            }
        }
    }
}
