// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Test.Auth.AzureAuthHelperMock
{
    /// <summary>
    /// This is a mock tool that follows some of the behavior of https://github.com/microsoft/ado-codespaces-auth
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The only supported mode is 'get-access-token' and expected CLI is 'azure-auth-helper get-access-token [optional list of scopes]'
        /// </summary>
        /// <remarks>When input arguments are well-formed, it outputs a mock token to the console</remarks>
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Expected at least one argument");
                return 1;
            }

            if (!string.Equals(args[0], "get-access-token", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("This tool only understands 'get-access-token' command");
                return 1;
            }

            // Scopes are optional
            string scopes = string.Join(" ", args.Skip(1));

            Console.Write($"This is a mock bearer token for scopes '{scopes}'");

            return 0;
        }
    }
}
