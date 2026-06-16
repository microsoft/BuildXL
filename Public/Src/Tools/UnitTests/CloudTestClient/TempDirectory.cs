// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace Test.Tool.CloudTestClient
{
    /// <summary>
    /// Lightweight disposable temp directory. All files created under <see cref="Root"/>
    /// are deleted when the instance is disposed.
    /// </summary>
    internal sealed class TempDirectory : IDisposable
    {
        /// <summary>
        /// The root directory of the temp directory.
        /// </summary>
        public string Root { get; }

        /// <nodoc/>
        public TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "CloudTestClientTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        /// <summary>Returns an absolute path under the temp root.</summary>
        public string GetPath(string relativePath) => Path.Combine(Root, relativePath);

        /// <summary>Creates a file under the temp root with the given content and returns its absolute path.</summary>
        public string WriteFile(string relativePath, string content)
        {
            string path = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        /// <nodoc/>
        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup — files may be locked
            }
        }
    }
}
