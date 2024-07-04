// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// File system view exposed to (see "SandboxedProcessPipExecutor")
    /// </summary>
    public interface ISandboxFileSystemView
    {
        /// <summary>
        /// Reports to the output file system that a given directory was created by the build.
        /// </summary>
        /// <remarks>
        /// This indicates the directory was not present when the build started and was later created by some pip. This is intended to record the first time such event happens
        /// and cases like a pip deleting an existing directory and another pip creating it back shouldn't be reported here. This is about understanding if the directory was there
        /// before the build started and if it should therefore be considered a source artifact.
        /// </remarks>
        void ReportOutputFileSystemDirectoryCreated(AbsolutePath path);

        /// <summary>
        /// Reports that a given directory existed before the build started and a pip deleted it.
        /// </summary>
        /// <remarks>
        /// Similarly to <see cref="ReportOutputFileSystemDirectoryCreated(AbsolutePath)"/>, this is intended to record the first time in the build when such event happens and
        /// cases like a pip creating a directory and another pip deleting it shouldn't be reported here. This is about understanding if the directory was there
        /// before the build started and if it should therefore be considered a source artifact.
        /// </remarks>
        public void ReportOutputFileSystemDirectoryRemoved(AbsolutePath path);

        /// <summary>
        /// Returns whether the given path represents a directory created by the build that the output file system knows about
        /// </summary>
        /// <remarks>
        /// If true, this means the directory was non-existent before the build started, and either pip effectively created it, 
        /// or the engine created it for the pip's sake (e.g., as parent of an output). 
        /// Note that these facts are only guaranteed to be reported after the pip has completed execution.
        /// <see cref="ReportOutputFileSystemDirectoryCreated(AbsolutePath)"/>
        /// </remarks>
        public bool ExistCreatedDirectoryInOutputFileSystem(AbsolutePath path);

        /// <summary>
        /// Returns whether the given path represents a directory removed by the build that the output file system knows about
        /// </summary>
        /// <remarks>
        /// If true, this means the directory existed before the build started, and a pip deleted it. <see cref="ReportOutputFileSystemDirectoryRemoved(AbsolutePath)"/>
        /// </remarks>
        public bool ExistRemovedDirectoryInOutputFileSystem(AbsolutePath path);
    }
}