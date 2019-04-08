-- (int: maxMachineId, HashEntry[]: unknownMachines) GetUnknownMachines(string clusterStateKey, int maxKnownMachineId)
local clusterStateKey = KEYS[1];
local maxKnownMachineId = tonumber(ARGV[1]);

local maxMachineId = tonumber(redis.call("HGET", clusterStateKey, "NextMachineId")) or 0;
local unknownMachines = {};

local function addUnknownMachine(id, location)
    unknownMachines[#unknownMachines+1] = {id, location};
end

if (maxMachineId > maxKnownMachineId) then
    for unknownMachineId=maxKnownMachineId+1,maxMachineId do
        local machineLocationFieldName = "M#"..unknownMachineId..".MachineLocation";
        addUnknownMachine(unknownMachineId, redis.call("HGET", clusterStateKey, machineLocationFieldName) or "");
    end
end

return { maxMachineId, unknownMachines };
