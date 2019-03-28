// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// The result of a redis acquire master role operation
    /// </summary>
    internal readonly struct RedisAcquireMasterRoleResult
    {
        /// <summary>
        /// Gets the actually applied increment to the redis value
        /// </summary>
        public readonly int MasterId;

        /// <summary>
        /// Gets the name of the prior master machine
        /// </summary>
        public readonly string PriorMasterMachineName;

        /// <summary>
        /// Gets the time of the last heartbeat of the prior master
        /// </summary>
        public readonly DateTime PriorMasterLastHeartbeat;

        /// <summary>
        /// The status of the 
        /// </summary>
        public readonly SlotStatus PriorMachineStatus;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisAcquireMasterRoleResult"/> struct.
        /// </summary>
        public RedisAcquireMasterRoleResult(int masterId, string priorMasterMachineName, DateTime priorMasterLastHeartbeat, SlotStatus priorMachineStatus)
        {
            MasterId = masterId;
            PriorMasterMachineName = priorMasterMachineName;
            PriorMasterLastHeartbeat = priorMasterLastHeartbeat;
            PriorMachineStatus = priorMachineStatus;
        }
    }

    /// <summary>
    /// The status of an acquired machine slot
    /// NOTE: These values should match those in AcquireSlot.lua script
    /// </summary>
    internal enum SlotStatus
    {
        Empty = 0,
        Released = 1,
        Acquired = 2,
        Expired = 3
    }
}
