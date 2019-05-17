// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System.Collections.Generic;
using Microsoft.Bond;

namespace BuildXL.Engine.Distribution.InternalBond
{
    internal static class OpenBondConversionUtils
    {
        #region AttachCompletionInfo

        public static AttachCompletionInfo ToInternalBond(this OpenBond.AttachCompletionInfo message)
        {
            return new AttachCompletionInfo()
            {
                AvailableRamMb = message.AvailableRamMb,
                MaxConcurrency = message.MaxConcurrency,
                WorkerCacheValidationContentHash = message.WorkerCacheValidationContentHash.ToDistributedContentHash(),
                WorkerId = message.WorkerId
            };
        }

        public static OpenBond.AttachCompletionInfo ToOpenBond(this AttachCompletionInfo message)
        {
            return new OpenBond.AttachCompletionInfo()
            {
                WorkerId = message.WorkerId,
                AvailableRamMb = message.AvailableRamMb,
                MaxConcurrency = message.MaxConcurrency,
                WorkerCacheValidationContentHash = message.WorkerCacheValidationContentHash.ToBondContentHash(),
            };
        }
        #endregion

        #region WorkerNotificationArgs

        public static WorkerNotificationArgs ToInternalBond(this OpenBond.WorkerNotificationArgs message)
        {
            var workerNotificationArgs = new WorkerNotificationArgs()
            {
                WorkerId = message.WorkerId,
                ExecutionLogBlobSequenceNumber = message.ExecutionLogBlobSequenceNumber,
                ExecutionLogData = new BondBlob(message.ExecutionLogData),
            };

            foreach (var i in message.CompletedPips)
            {
                workerNotificationArgs.CompletedPips.Add(new PipCompletionData()
                {
                    ExecuteStepTicks = i.ExecuteStepTicks,
                    PipIdValue = i.PipIdValue,
                    QueueTicks = i.QueueTicks,
                    ResultBlob = new BondBlob(i.ResultBlob),
                    Step = i.Step
                });
            }

            foreach (var i in message.ForwardedEvents)
            {
                workerNotificationArgs.ForwardedEvents.Add(new EventMessage()
                {
                    EventId = i.EventId,
                    EventKeywords = i.EventKeywords,
                    EventName = i.EventName,
                    Id = i.Id,
                    Level = i.Level,
                    Text = i.Text
                });
            }

            return workerNotificationArgs;
        }

        public static OpenBond.WorkerNotificationArgs ToOpenBond(this WorkerNotificationArgs message)
        {
            var completedPips = new List<OpenBond.PipCompletionData>();
            if (message.CompletedPips != null)
            {
                foreach (var i in message.CompletedPips)
                {
                    completedPips.Add(new OpenBond.PipCompletionData()
                    {
                        ExecuteStepTicks = i.ExecuteStepTicks,
                        PipIdValue = i.PipIdValue,
                        QueueTicks = i.QueueTicks,
                        ResultBlob = i.ResultBlob.Data,
                        Step = i.Step
                    });
                }
            }

            var eventMessages = new List<OpenBond.EventMessage>();
            if (message.ForwardedEvents != null)
            {
                foreach (var i in message.ForwardedEvents)
                {
                    eventMessages.Add(new OpenBond.EventMessage()
                    {
                        EventId = i.EventId,
                        EventKeywords = i.EventKeywords,
                        EventName = i.EventName,
                        Id = i.Id,
                        Level = i.Level,
                        Text = i.Text
                    });
                }
            }

            return new OpenBond.WorkerNotificationArgs()
            {
                WorkerId = message.WorkerId,
                CompletedPips = completedPips,
                ExecutionLogBlobSequenceNumber = message.ExecutionLogBlobSequenceNumber,
                ExecutionLogData = message.ExecutionLogData.Data,
                ForwardedEvents = eventMessages
            };
            
        }
        #endregion

        #region BuildStartData
        public static BuildStartData ToInternalBond(this OpenBond.BuildStartData message)
        {
            return new BuildStartData()
            {
                CachedGraphDescriptor = message.CachedGraphDescriptor.ToDistributionPipGraphCacheDescriptor(),
                FingerprintSalt = message.FingerprintSalt,
                MasterLocation = new ServiceLocation()
                {
                    IpAddress = message.MasterLocation.IpAddress,
                    Port = message.MasterLocation.Port
                },
                SessionId = message.SessionId,
                SymlinkFileContentHash = message.SymlinkFileContentHash.ToDistributedContentHash(),
                EnvironmentVariables = message.EnvironmentVariables,
                WorkerId = message.WorkerId,
            };
        }

        public static OpenBond.BuildStartData ToOpenBond(this BuildStartData message)
        {
            return new OpenBond.BuildStartData()
            {
                WorkerId = message.WorkerId,
                CachedGraphDescriptor = message.CachedGraphDescriptor.ToPipGraphCacheDescriptor(),
                EnvironmentVariables = message.EnvironmentVariables,
                FingerprintSalt = message.FingerprintSalt,
                MasterLocation = new OpenBond.ServiceLocation()
                {
                    IpAddress = message.MasterLocation.IpAddress,
                    Port = message.MasterLocation.Port
                },
                SessionId = message.SessionId,
                SymlinkFileContentHash = message.SymlinkFileContentHash.ToBondContentHash(),
            };
        }
        #endregion

        #region PipBuildRequest
        public static PipBuildRequest ToInternalBond(this OpenBond.PipBuildRequest message)
        {
            var pipBuildRequest = new PipBuildRequest();
            pipBuildRequest.Hashes = new List<FileArtifactKeyedHash>();

            foreach (var hash in message.Hashes)
            {
                var fileArtifactKeyedHash = new FileArtifactKeyedHash()
                {
                    ContentHash = hash.ContentHash.ToDistributedContentHash(),
                    FileName = hash.FileName,
                    Length = hash.Length,
                    PathString = hash.PathString,
                    PathValue = hash.PathValue,
                    ReparsePointType = (BondReparsePointType)hash.ReparsePointType,
                    RewriteCount = hash.RewriteCount,
                    ReparsePointTarget = hash.ReparsePointTarget,
                };

                if (hash.AssociatedDirectories != null)
                {
                    fileArtifactKeyedHash.AssociatedDirectories = new List<BondDirectoryArtifact>();

                    foreach (var dir in hash.AssociatedDirectories)
                    {
                        fileArtifactKeyedHash.AssociatedDirectories.Add(new BondDirectoryArtifact()
                        {
                            DirectoryPathValue = dir.DirectoryPathValue,
                            DirectorySealId = dir.DirectorySealId,
                            IsDirectorySharedOpaque = dir.IsDirectorySharedOpaque,
                        });
                    }
                }

                pipBuildRequest.Hashes.Add(fileArtifactKeyedHash);
            }

            pipBuildRequest.Pips = new List<SinglePipBuildRequest>();

            foreach (var pip in message.Pips)
            {
                var singlePipBuildRequest = new SinglePipBuildRequest()
                {
                    ActivityId = pip.ActivityId,
                    ExpectedRamUsageMb = pip.ExpectedRamUsageMb,
                    Fingerprint = pip.Fingerprint.ToDistributionCacheFingerprint(),
                    PipIdValue = pip.PipIdValue,
                    Priority = pip.Priority,
                    SequenceNumber = pip.SequenceNumber,
                    Step = pip.Step
                };

                pipBuildRequest.Pips.Add(singlePipBuildRequest);
            }

            return pipBuildRequest;
        }

        public static OpenBond.PipBuildRequest ToOpenBond(this PipBuildRequest message)
        {
            var hashes = new List<OpenBond.FileArtifactKeyedHash>();
            foreach (var i in message.Hashes)
            {
                var directories = new List<OpenBond.BondDirectoryArtifact>();

                if (i.AssociatedDirectories != null)
                {
                    foreach (var j in i.AssociatedDirectories)
                    {
                        directories.Add(new OpenBond.BondDirectoryArtifact()
                        {
                            DirectoryPathValue = j.DirectoryPathValue,
                            DirectorySealId = j.DirectorySealId,
                            IsDirectorySharedOpaque = j.IsDirectorySharedOpaque
                        });
                    }
                }

                hashes.Add(new OpenBond.FileArtifactKeyedHash()
                {
                    AssociatedDirectories = directories,
                    ContentHash = i.ContentHash.ToBondContentHash(),
                    FileName = i.FileName,
                    Length = i.Length,
                    PathString = i.PathString,
                    PathValue = i.PathValue,
                    ReparsePointTarget = i.ReparsePointTarget,
                    ReparsePointType = (Cache.Fingerprints.BondReparsePointType)((int)i.ReparsePointType),
                    RewriteCount = i.RewriteCount
                });
            }

            var pips = new List<OpenBond.SinglePipBuildRequest>();
            foreach (var i in message.Pips)
            {
                pips.Add(new OpenBond.SinglePipBuildRequest()
                {
                    ActivityId = i.ActivityId,
                    ExpectedRamUsageMb = i.ExpectedRamUsageMb,
                    Fingerprint = i.Fingerprint.ToCacheFingerprint(),
                    PipIdValue = i.PipIdValue,
                    Priority = i.Priority,
                    SequenceNumber = i.SequenceNumber,
                    Step = i.Step,
                });
            }

            return new OpenBond.PipBuildRequest()
            {
                Hashes = hashes,
                Pips = pips
            };
        }
        #endregion
    }
}
#endif
