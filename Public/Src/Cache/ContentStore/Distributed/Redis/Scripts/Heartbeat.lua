-- (MachineState: priorState, BitSet: inactiveMachineBitSet) Heartbeat(string clusterStateKey, int machineId, MachineStatus declaredState, long currentTime, long recomputeExpiryInterval, long machineExpiryInterval)
local clusterStateKey = KEYS[1];
local machineId = tonumber(ARGV[1]);
local declaredState = tonumber(ARGV[2]);
local currentTime = tonumber(ARGV[3]);
local recomputeExpiryInterval = tonumber(ARGV[4]);
local machineExpiryInterval = tonumber(ARGV[5]);

-- These values must match enum MachineStatus
local UNKNOWN, ACTIVE, UNAVAILABLE, EXPIRED = 0, 1, 2, 3;

local function stateFieldName(machineIdArg)
    return "M#"..machineIdArg..".State";
end

local function lastHeartbeatFieldName(machineIdArg)
    return "M#"..machineIdArg..".LastHeartbeat";
end

local priorState = tonumber(redis.call("HGET", clusterStateKey, stateFieldName(machineId))) or UNKNOWN;
redis.call("HSET", clusterStateKey, stateFieldName(machineId), declaredState);

-- Update active machines in response to machine state changes
local inactiveMachineBitSetKey = "{"..clusterStateKey.."}.InactiveMachines";

if (declaredState == ACTIVE) then
    redis.call("HSET", clusterStateKey, lastHeartbeatFieldName(machineId), currentTime);

    -- Machine switched to active state
    redis.call("SETBIT", inactiveMachineBitSetKey, machineId, 0);
else
    -- Machine is switching to an inactive state. Set the bit for the inactive machine bit set
    redis.call("SETBIT", inactiveMachineBitSetKey, machineId, 1);
end

local lastRecomputeTime = tonumber(redis.call("HGET", clusterStateKey, "LastInactiveMachinesRecomputeTime")) or 0;

if ((lastRecomputeTime + recomputeExpiryInterval) < currentTime) then
    -- Time to recompute active machines
    redis.call("DEL", inactiveMachineBitSetKey);
    local maxMachineId = tonumber(redis.call("HGET", clusterStateKey, "NextMachineId")) or 0;

    for i=1,maxMachineId do
        local currentLastHeartbeat = tonumber(redis.call("HGET", clusterStateKey, lastHeartbeatFieldName(i))) or 0;
        local currentState = tonumber(redis.call("HGET", clusterStateKey, stateFieldName(i))) or UNKNOWN;

        if ((currentLastHeartbeat + machineExpiryInterval) < currentTime) then
            -- Machine expired mark bit in expired bit set and change current state
            -- and stored state so that machine gets marked in inactive bit set
            currentState = EXPIRED;
            redis.call("HSET", clusterStateKey, stateFieldName(i), EXPIRED);
        end

        if (currentState == UNAVAILABLE or currentState == EXPIRED) then
            -- Machine state is inactive
            redis.call("SETBIT", inactiveMachineBitSetKey, i, 1);
        end
    end

    redis.call("HSET", clusterStateKey, "LastInactiveMachinesRecomputeTime", currentTime);
end

local inactiveMachineBitSet = redis.call("GET", inactiveMachineBitSetKey);

return { priorState, inactiveMachineBitSet };
