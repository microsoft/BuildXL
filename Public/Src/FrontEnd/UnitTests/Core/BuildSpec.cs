// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Represents build spec file name and its content.
    /// </summary>
    public readonly struct BuildSpec
    {
        private BuildSpec(string fileName, string content)
        {
            FileName = fileName;
            Content = content;
        }

        /// <summary>
        /// Build spec file name.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Build spec content.
        /// </summary>
        public string Content { get; }

        /// <nodoc />
        public static BuildSpec Create(string fileName, string content)
        {
            Contract.Requires(!string.IsNullOrEmpty(fileName));

            return new BuildSpec(fileName, content);
        }
    }
}
