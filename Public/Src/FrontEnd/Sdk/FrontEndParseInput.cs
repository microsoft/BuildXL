// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Parsing input.
    /// </summary>
    public sealed class FrontEndParseInput
    {
        /// <summary>
        /// Parsing function delegate.
        /// </summary>
        public delegate FrontEndParseOutput ParseFunction(AbsolutePath path);

        /// <summary>
        /// Path of file to parse.
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Parse function.
        /// </summary>
        public readonly ParseFunction Parse;

        /// <summary>
        /// Constructor.
        /// </summary>
        public FrontEndParseInput(AbsolutePath path, ParseFunction parse)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(parse != null);

            Path = path;
            Parse = parse;
        }
    }
}
