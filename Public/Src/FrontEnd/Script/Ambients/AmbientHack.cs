// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Temporary class untill we have proper foreign function semantics
    /// </summary>
    public static class AmbientHack
    {
        /// <summary>
        /// Computes the name of the hack name
        /// </summary>
        public static string GetName(string name)
        {
            return "_PreludeAmbientHack_" + name;
        }
    }
}
