// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Writes a source file
    /// </summary>
    public class SourceWriter : BuildXLFileWriter
    {
        /// <summary>
        /// Writes a source file
        /// </summary>
        /// <param name="absolutePath">absolute path of the file</param>
        /// <param name="repeatingContent">content to be written to file until lengthInBytes is reached</param>
        /// <param name="lengthInBytes">Approximate length of file. Actual written file may be off by a factor of
        /// however long the releatingContent parameter is.</param>
        public SourceWriter(string absolutePath, string repeatingContent, long lengthInBytes)
            : base(absolutePath)
        {
            while (Writer.BaseStream.Position < lengthInBytes)
            {
                Writer.Write(repeatingContent);
            }
        }

        /// <inheritdoc/>
        protected override void WriteStart()
        {
            return;
        }

        /// <inheritdoc/>
        protected override void WriteEnd()
        {
            return;
        }
    }
}
