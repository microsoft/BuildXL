// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;

namespace BuildXL.FrontEnd.Nuget
{
    internal static class ErrorHandlingExtensions
    {
        /// <summary>
        /// Returns a user-friendly error message of the <paramref name="result"/>.
        /// </summary>
        public static string GetNativeErrorMessage(this EnumerateDirectoryResult result)
        {
            Contract.Requires(result != null);
            Contract.Requires(!result.Succeeded);

            return new Win32Exception(result.NativeErrorCode).Message;
        }
    }
}
