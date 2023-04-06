// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Interop
{
    /// <summary>
    /// The supported host operating system values
    /// </summary>
    public enum OperatingSystem
    {
        /// <summary>
        /// Windows
        /// </summary>
        Win = 1,

        /// <summary>
        /// MacOs
        /// </summary>
        MacOS,

        /// <summary>
        /// Unix variants
        /// </summary>
        Unix
    }

    /// <nodoc />
    public static class OperatingSystemExtensions
    {
        /// <summary>
        /// DScript string constant that corresponds to <paramref name="this"/>
        /// </summary>
        public static string GetDScriptValue(this OperatingSystem @this)
        {
            return
                @this == OperatingSystem.Win ? "win" :
                @this == OperatingSystem.MacOS ? "macOS" :
                @this == OperatingSystem.Unix ? "unix" :
                null;
        }
    }
}
