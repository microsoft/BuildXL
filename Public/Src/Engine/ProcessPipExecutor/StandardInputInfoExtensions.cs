// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Core;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Info about the source of standard input.
    /// </summary>
    public class StandardInputInfoExtensions
    {
        /// <summary>
        /// Creates a standard input info where the source comes from raw data.
        /// </summary>
        public static StandardInputInfo CreateForProcess(Process process, PathTable pathTable)
        {
            return process.StandardInput.IsData
                ? StandardInputInfo.CreateForData(process.StandardInput.Data.ToString(pathTable))
                : (process.StandardInput.IsFile
                    ? StandardInputInfo.CreateForFile(process.StandardInput.File.Path.ToString(pathTable))
                    : null);
        }
    }
}
