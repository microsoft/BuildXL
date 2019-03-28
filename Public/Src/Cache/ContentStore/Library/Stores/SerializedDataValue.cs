// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     A class that helps read and write a marker file to keep track of a metadata value for the data in a directory.
    /// </summary>
    public class SerializedDataValue
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _valueFilePath;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SerializedDataValue" /> class.
        /// </summary>
        /// <param name="fileSystem">File system to use.</param>
        /// <param name="valueFilePath">Directory to track.</param>
        /// <param name="initialValue">Value to set if the directory does not initially exist.</param>
        public SerializedDataValue(IAbsFileSystem fileSystem, AbsolutePath valueFilePath, int initialValue)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(valueFilePath != null);

            _fileSystem = fileSystem;
            _valueFilePath = valueFilePath;

            if (!_fileSystem.FileExists(_valueFilePath))
            {
                _fileSystem.CreateDirectory(_valueFilePath.Parent);
                WriteValueFile(initialValue);
            }
        }

        /// <summary>
        ///     Reads the value from the marker file. Returns 0 is the marker does not exist.
        /// </summary>
        public int ReadValueFile()
        {
            int valueRead = 0;
            if (_fileSystem.FileExists(_valueFilePath))
            {
                valueRead = BitConverter.ToInt32(_fileSystem.ReadAllBytes(_valueFilePath), 0);
            }

            return valueRead;
        }

        /// <summary>
        ///     Writes the given value number to the marker file.
        /// </summary>
        /// <param name="newValue">Value to write into the marker file.</param>
        public void WriteValueFile(int newValue)
        {
            Contract.Requires(newValue >= 0); // Allow writes of value 0 for tests

            _fileSystem.WriteAllBytes(_valueFilePath, BitConverter.GetBytes(newValue));
        }
    }
}
