// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Orchestrator.Vsts
{
    /// <summary>
    /// Interface that defines the methods that must be implemented by VSTS loggers
    /// </summary>
    public interface ILogger
    {
        /// <nodoc />
        void Info(string infoMessage);

        /// <nodoc />
        void Debug(string debugMessage);

        /// <nodoc />
        void Warning(string warningMessage);

        /// <nodoc />
        void Error(string errorMessage);

        /// <nodoc />
        void Warning(string format, params object[] args);

        /// <nodoc />
        void Error(string format, params object[] args);

        /// <nodoc />
        void Debug(string format, params object[] args);

        /// <nodoc />
        void Warning(Exception ex, string warningMessage);

        /// <nodoc />
        void Error(Exception ex, string errorMessage);
    }
}
