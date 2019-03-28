// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Represents the public facade of a spec together with its correponding serialized AST
    /// </summary>
    public sealed class PublicFacadeSpecWithAst
    {
        /// <nodoc/>
        public AbsolutePath SpecPath { get; }

        /// <nodoc/>
        public FileContent PublicFacadeContent { get; }

        /// <nodoc/>
        public ByteContent SerializedAst { get; }

        /// <nodoc/>
        public PublicFacadeSpecWithAst(AbsolutePath specPath, FileContent publicFacadeContent, ByteContent serializedAstContent)
        {
            Contract.Requires(specPath.IsValid);
            Contract.Requires(publicFacadeContent.IsValid);
            Contract.Requires(serializedAstContent.IsValid);

            SpecPath = specPath;
            PublicFacadeContent = publicFacadeContent;
            SerializedAst = serializedAstContent;
        }
    }
}
