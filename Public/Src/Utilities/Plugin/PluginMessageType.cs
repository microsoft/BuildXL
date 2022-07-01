// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static BuildXL.Plugin.Grpc.SupportedOperationResponse.Types;

namespace BuildXL.Plugin
{
    ///<nodoc />
    public enum PluginMessageType
    {
        ///<nodoc />
        ParseLogMessage,

        ///<nodoc />
        HandleExitCode,

        ///<nodoc />
        SupportedOperation,

        ///<nodoc />
        Unknown
    }

    ///<nodoc />
    public static class PluginMessageTypeHelper
    {
        /// <summary>
        /// Convert the <see cref="SupportedOperation" /> to <see cref="PluginMessageType" />
        /// </summary>
        /// <param name="supportedOperation"></param>
        /// <returns>plugin message type</returns>
        public static PluginMessageType ToPluginMessageType(SupportedOperation supportedOperation)
        {
            switch (supportedOperation)
            {
                case SupportedOperation.LogParse:
                    return PluginMessageType.ParseLogMessage;

                case SupportedOperation.HandleExitCode:
                    return PluginMessageType.HandleExitCode;

                default:
                    return PluginMessageType.Unknown;
            }
        }
    }
}
