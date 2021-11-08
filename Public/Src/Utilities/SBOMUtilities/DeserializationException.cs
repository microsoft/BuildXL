// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using Newtonsoft.Json;
using SBOMApi.Contracts;

namespace BuildXL.SBOMUtilities
{
    /// <summary>
    /// Exception thrown when encountering errors during a deserialization operation.
    /// </summary>
    public class DeserializationException : Exception
    {
        /// <nodoc />
        public DeserializationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
