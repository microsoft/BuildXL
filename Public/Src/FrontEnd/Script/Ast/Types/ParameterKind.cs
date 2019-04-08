// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Kinds of parameter
    /// </summary>
    public enum ParameterKind : byte
    {
        /// <summary>
        /// Required.
        /// </summary>
        Required,

        /// <summary>
        /// Optional.
        /// </summary>
        Optional,

        /// <summary>
        /// Rest.
        /// </summary>
        Rest,
    }
}
