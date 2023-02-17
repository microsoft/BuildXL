// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Storage;
using BuildXL.Utilities.Core;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// A struct represents a file observed by BuildXL inside a sealed directory
    /// </summary>
    public readonly struct SealedDirectoryFile
    {
        private const string RenderSeparator = "|";

        /// <summary>
        /// Full file name
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// BuildXL file artifact
        /// </summary>
        public FileArtifact Artifact { get; }

        /// <summary>
        /// File content info (hash, length)
        /// </summary>
        public FileContentInfo ContentInfo { get; }

        /// <nodoc/>
        public SealedDirectoryFile(string fileName, FileArtifact artifact, FileContentInfo contentInfo)
        {
            FileName = fileName;
            Artifact = artifact;
            ContentInfo = contentInfo;
        }

        /// <summary>
        /// Renders this struct as a string that can be parsed using <see cref="TryParse"/>
        ///
        /// Format: {FileName}|{FileArtifact}|{FileContentInfo}
        /// </summary>
        public string Render()
        {
            return string.Join(string.Empty, RenderContent());
        }

        /// <summary>
        /// A variant of Render that returns individual components of the rendered string representation of this struct.
        /// </summary>
        public IEnumerable<string> RenderContent()
        {
            yield return FileName;
            yield return RenderSeparator;
            yield return FileId.ToString(Artifact);
            yield return RenderSeparator;
            yield return ContentInfo.Render();
        }

        /// <summary>
        /// Parses a string and forms a <see cref="SealedDirectoryFile"/> struct. The string must follow the format specified by <see cref="Render"/> method.
        /// </summary>
        public static bool TryParse(string value, out SealedDirectoryFile sealedDirectoryFile)
        {
            sealedDirectoryFile = default(SealedDirectoryFile);

            string[] splits = value.Split(new[] { RenderSeparator }, StringSplitOptions.None);
            if (splits.Length != 3)
            {
                return false;
            }

            if (splits[0] == string.Empty)
            {
                return false;
            }

            if (splits[1] == string.Empty || !FileId.TryParse(splits[1], out var fileArtifact))
            {
                return false;
            }

            if (splits[2] == string.Empty)
            {
                return false;
            }

            FileContentInfo fileContentInfo;
            try
            {
                fileContentInfo = FileContentInfo.Parse(splits[2]);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return false;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            sealedDirectoryFile = new SealedDirectoryFile(splits[0], fileArtifact, fileContentInfo);
            return true;
        }
    }
}
