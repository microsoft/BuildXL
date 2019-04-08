// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Result of parsing performed by front-ends.
    /// </summary>
    public class FrontEndParseOutput
    {
        /// <summary>
        /// Checks if parsing is successful.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FrontEndParseOutput(bool success)
        {
            Success = success;
        }
    }

    /// <summary>
    /// Result of parsing performed by front-ends, with additional data.
    /// </summary>
    public sealed class FrontEndParseOutput<T> : FrontEndParseOutput
    {
        /// <summary>
        /// Additional data.
        /// </summary>
        public T Data { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FrontEndParseOutput(bool success, T data)
            : base(success)
        {
            Data = data;
        }
    }
}
