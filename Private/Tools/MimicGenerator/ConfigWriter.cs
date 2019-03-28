// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace Tool.MimicGenerator
{
    public abstract class ConfigWriter : BuildXLFileWriter
    {
        /// <summary>
        /// Gets the allowed environment variables
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
        public static readonly string[] AllowedEnvironmentVariables = new string[]
        {
            "RuntimeScaleFactor",
            "IoScaleFactor",
            "PredictNativeSpecs",
        };

        protected ConfigWriter(string absolutePath)
            : base(absolutePath) { }

        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public abstract void AddModule(ModuleWriter moduleWriter);

        public abstract void WriteMount(string mountName, string mountAbsolutePath);

        public abstract string ToRelativePathExpression(string path);
    }
}
