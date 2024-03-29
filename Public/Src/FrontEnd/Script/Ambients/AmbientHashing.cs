// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
#pragma warning disable SYSLIB0021 // Type or member is obsolete. Temporarily suppressing the warning for .net 6. Work item: 1885580
        private static readonly SHA256 s_hashManager = SHA256Managed.Create();
#pragma warning restore SYSLIB0021 // Type or member is obsolete

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
