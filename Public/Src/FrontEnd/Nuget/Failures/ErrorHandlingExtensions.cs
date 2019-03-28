// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
