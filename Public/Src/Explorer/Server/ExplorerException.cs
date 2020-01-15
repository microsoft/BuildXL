// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Explorer.Server
{
    /// <summary>
    /// Exception to be thrown when something goes on on the server and we want to report this to the client app
    /// </summary>
    /// <remarks>
    /// This exception is special cased by ExplroerExceptionFilter to return this object as a Json value.
    /// </remarks>
    public class ExplorerException : Exception
    {
        /// <nodoc />
        public ExplorerException(string message) : base(message)
        {
        }

        /// <nodoc />
        public ExplorerException(string message, Exception ex) : base(message, ex)
        {
        }
    }
}
