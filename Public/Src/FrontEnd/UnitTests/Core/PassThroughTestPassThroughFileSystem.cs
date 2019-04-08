// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Sdk.FileSystem;

namespace Test.BuildXL.FrontEnd.Core
{
    public class PassThroughMutableFileSystem: PassThroughFileSystem, IMutableFileSystem
    {
        public PassThroughMutableFileSystem(PathTable pathTable)
            : base(pathTable)

        {
        }

        /// <inheritdoc />
        IFileSystem IFileSystem.CopyWithNewPathTable(PathTable pathTable)
        {
            // It is safe to just return a new filesystem because this class does not store any absolute paths.
            return new PassThroughMutableFileSystem(pathTable);
        }

        /// <inheritdoc />
        public IMutableFileSystem WriteAllText(string path, string content)
        {
            return WriteAllText(AbsolutePath.Create(PathTable, path), content);
        }

        /// <inheritdoc />
        public IMutableFileSystem WriteAllText(AbsolutePath path, string content)
        {
            File.WriteAllText(path.ToString(PathTable),content);
            return this;
        }

        /// <inheritdoc />
        public IMutableFileSystem CreateDirectory(AbsolutePath path)
        {
            global::BuildXL.Native.IO.FileUtilities.CreateDirectory(path.ToString(PathTable));
            return this;
        }
    }
}
