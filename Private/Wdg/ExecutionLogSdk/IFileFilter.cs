// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Tool.ExecutionLogSdk
{
    public interface IFileFilter
    {
        /// <summary>
        /// Returns true to indicate that the specified file should be loaded
        /// </summary>
        /// <param name="filename">The name of the file to be loaded or not</param>
        /// <returns>True if the specified file should be loaded</returns>
        bool ShouldLoadFile(string filename);
    }
}
