// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Google.Protobuf;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Native.IO;
using System.Diagnostics;
using BuildXL.Execution.Analyzer.Model;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using System.Text;
using BuildXL.Pips;


namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for XLGpp ProtoBuf conversions.
    /// </summary>
    public static class XLGppProtobufExtension
    {

        public static BXLInvocationEvent_XLGpp ToBXLInvocationEvent_XLGpp(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var bxlInvEvent = new BXLInvocationEvent_XLGpp();
            var loggingConfig = data.Configuration.Logging;

            var uuid = Guid.NewGuid().ToString();

            bxlInvEvent.UUID = uuid;
            bxlInvEvent.WorkerID = workerID;
            bxlInvEvent.SubstSource = loggingConfig.SubstSource.ToString(pathTable, PathFormat.HostOs);
            bxlInvEvent.SubstTarget = loggingConfig.SubstTarget.ToString(pathTable, PathFormat.HostOs);
            bxlInvEvent.IsSubstSourceValid = loggingConfig.SubstSource.IsValid;
            bxlInvEvent.IsSubstTargetValid = loggingConfig.SubstTarget.IsValid;

            return bxlInvEvent;
        }

        public static FileArtifactContentDecidedEvent_XLGpp ToFileArtifactContentDecidedEvent_XLGpp(this FileArtifactContentDecidedEventData data)
        {
            var fileArtifactContentDecidedEvent = new FileArtifactContentDecidedEvent_XLGpp();

            return fileArtifactContentDecidedEvent;
        }
    }
}
