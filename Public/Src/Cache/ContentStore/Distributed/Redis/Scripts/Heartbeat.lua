local clusterStateKey = KEYS[1];
local machineId = tonumber(ARGV[1]);
local declaredState = tonumber(ARGV[2]);
local currentTime = tonumber(ARGV[3]);
local recomputeExpiryInterval = tonumber(ARGV[4]);
local machineActiveToClosedInterval = tonumber(ARGV[5]);
local machineActiveToExpiredInterval = tonumber(ARGV[6]);

-- These values must match enum MachineState
local UNKNOWN, OPEN, UNAVAILABLE, EXPIRED, CLOSED = 0, 1, 2, 3, 4;

local function stateFieldName(machineIdArg)
    return "M#"..machineIdArg..".State";
end

local function lastHeartbeatFieldName(machineIdArg)
    return "M#"..machineIdArg..".LastHeartbeat";
end

-- Keeps the machines that are inactive (i.e. most likely no longer alive in the cluster)
local inactiveMachineBitSetKey = "{"..clusterStateKey.."}.InactiveMachines";

-- Keeps the machines that are closed (i.e. briefly unusable)
local closedMachineBitSetKey = "{"..clusterStateKey.."}.ClosedMachines";

local function updateMachineStateBitSets(machineIdArg, newStateArg)
    -- There's no ternary operator in Lua, so this is it
    if (newStateArg == OPEN) then
        redis.call("SETBIT", inactiveMachineBitSetKey, machineIdArg, 0);
        redis.call("SETBIT", closedMachineBitSetKey, machineIdArg, 0);
    elseif (newStateArg == UNAVAILABLE or newStateArg == EXPIRED) then
        redis.call("SETBIT", inactiveMachineBitSetKey, machineIdArg, 1);
        redis.call("SETBIT", closedMachineBitSetKey, machineIdArg, 0);
    elseif (newStateArg == CLOSED) then
        redis.call("SETBIT", inactiveMachineBitSetKey, machineIdArg, 0);
        redis.call("SETBIT", closedMachineBitSetKey, machineIdArg, 1);
    end
end

-- Update active machines in response to machine state changes
local priorState = tonumber(redis.call("HGET", clusterStateKey, stateFieldName(machineId))) or UNKNOWN;

-- Declaring unknown state only performs a read of the current state, without updating it
if (declaredState ~= UNKNOWN) then
    redis.call("HSET", clusterStateKey, stateFieldName(machineId), declaredState);
    redis.call("HSET", clusterStateKey, lastHeartbeatFieldName(machineId), currentTime);

    updateMachineStateBitSets(machineId, declaredState);
end

local lastRecomputeTime = tonumber(redis.call("HGET", clusterStateKey, "LastInactiveMachinesRecomputeTime")) or 0;

if ((lastRecomputeTime + recomputeExpiryInterval) < currentTime) then
    -- Time to recompute active machines
    redis.call("DEL", closedMachineBitSetKey);
    redis.call("DEL", inactiveMachineBitSetKey);

    local maxMachineId = tonumber(redis.call("HGET", clusterStateKey, "NextMachineId")) or 0;

    for i=1,maxMachineId do
        local currentLastHeartbeat = tonumber(redis.call("HGET", clusterStateKey, lastHeartbeatFieldName(i))) or 0;
        local currentState = tonumber(redis.call("HGET", clusterStateKey, stateFieldName(i))) or UNKNOWN;

        if (currentState ~= UNKNOWN) then
            local computedState = currentState;

            if (currentState == OPEN and ((currentLastHeartbeat + machineActiveToClosedInterval) < currentTime)) then
                computedState = CLOSED;
            end

            if ((currentLastHeartbeat + machineActiveToExpiredInterval) < currentTime) then
                computedState = EXPIRED;
            end

            redis.call("HSET", clusterStateKey, stateFieldName(i), computedState);
            updateMachineStateBitSets(i, computedState);
        end
    end

    redis.call("HSET", clusterStateKey, "LastInactiveMachinesRecomputeTime", currentTime);
end

local inactiveMachineBitSet = redis.call("GET", inactiveMachineBitSetKey);

local closedMachineBitSet = redis.call("GET", closedMachineBitSetKey);

return { priorState, inactiveMachineBitSet, closedMachineBitSet };
