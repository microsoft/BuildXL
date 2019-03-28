-- (int: machineId, bool: isAdded) GetOrAddMachine(string clusterStateKey, string machineLocation, long currentTime)
local clusterStateKey = KEYS[1];
local machineLocation = ARGV[1];
local currentTime = tonumber(ARGV[2]);

local machineIdFieldName = "M["..machineLocation.."].MachineId";

local function lastHeartbeatFieldName(machineIdArg)
    return "M#"..machineIdArg..".LastHeartbeat";
end

-- Try to get current machine id.
-- If current machine id still maps to machine name, then return the id
local machineId = tonumber(redis.call("HGET", clusterStateKey, machineIdFieldName));
if (machineId ~= nil) then
    redis.call("HSET", clusterStateKey, lastHeartbeatFieldName(machineId), currentTime);
    return { machineId, 0 };
end

-- NOTE: This call means that the min machine id is 1. If this is changed Heartbeat should be changed to only compute inactive machines for [newMinMachineId, maxMachineId]
-- Create next machine id HINCRBY(clusterStateKey, NextMachineId, 1)
local nextMachineId = tonumber(redis.call("HINCRBY", clusterStateKey, "NextMachineId", 1));
machineId = nextMachineId;

local machineLocationFieldName = "M#"..machineId..".MachineLocation";
 
redis.call("HSET", clusterStateKey, machineLocationFieldName, machineLocation);
redis.call("HSET", clusterStateKey, machineIdFieldName, machineId);
redis.call("HSET", clusterStateKey, lastHeartbeatFieldName(machineId), currentTime);

return { machineId, 1 };
