// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic hashing functionality.
    /// </summary>
    public sealed class AmbientHashing : AmbientDefinitionBase
    {
        /*used to handling sha256 hashing of strings*/
        private static readonly SHA256 s_hashManager = SHA256Managed.Create();

        /// <nodoc />
        public AmbientHashing(PrimitiveTypes knownTypes) 
            : base("Hashing", knownTypes)
        {
        }

        /// <nodoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("Hashing"),
                new[]
                {
                    Function("sha256", Sha256, CreateSHA256Signature),
                });
        }

        private static EvaluationResult Sha256(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            // AsStringOptional will handle type checks and null check
            string content = Args.AsString(args, 0);
            // getting evaluation result
            return EvaluationResult.Create(ComputeSha256(content));
        }

        private CallSignature CreateSHA256Signature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.StringType);

        private static string ComputeSha256(string content)
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            byte[] hashBytes = s_hashManager.ComputeHash(contentBytes);
            return hashBytes.ToHex();
        }
    }
}
