// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
