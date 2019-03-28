-- Attempts to acquire a slot between [0, slotCount)
-- NOTE: This method is very similar to AddMachine except machines always try to steal from the top slots.
-- (int: slotNumber, long: replaceMachineLastHeartbeat, string: replacedMachineName, int replacedMachineStatus) AcquireSlot(string slotsKey, string machineName, long currentTime, long machineExpiryTime, int slotCount, int status)
local slotsKey = KEYS[1];
local machineName = ARGV[1];
local currentTime = tonumber(ARGV[2]);
local machineExpiryTime = tonumber(ARGV[3]);
local slotCount = tonumber(ARGV[4]);
local status = tonumber(ARGV[5]);

-- These values must match enum SlotStatus
local EMPTY, RELEASED, ACQUIRED, EXPIRED = 0, 1, 2, 3;

local replacedMachineName = "";
local replaceMachineLastHeartbeat = 0;
local replacedMachineStatus = EMPTY;
local slotLastHeartbeatFieldName = nil;
local slotMachineNameFieldName = nil;
local slotStatusFieldName = nil;

local acquiredSlotData = nil;

-- Try to steal machine id by iterating through all machine ids [1, slotCount] and checking if last
-- heartbeat is beyond machineExpiryTime
for slotNumber=1,slotCount do
    -- lastHeartbeatFieldName = M#{slotNumber}.LastHeartbeat 
    local lastHeartbeatFieldName = "M#"..slotNumber..".LastHeartbeat"
    -- machineNameFieldName = M#{slotNumber}.MachineName 
    local machineNameFieldName = "M#"..slotNumber..".MachineName";
    -- statusFieldName = M#{slotNumber}.Status 
    local statusFieldName = "M#"..slotNumber..".Status";

    local lastHeartbeat = tonumber(redis.call("HGET", slotsKey, lastHeartbeatFieldName)) or 0;
    local slotMachineName = redis.call("HGET", slotsKey, machineNameFieldName) or "";
    local slotStatus = tonumber(redis.call("HGET", slotsKey, statusFieldName)) or EMPTY;
    local acquired = false;

    if (slotMachineName == machineName) then
        -- Found the old slot for the machine. Just use that.
        acquired = true;
    elseif (acquiredSlotData == nil and status == ACQUIRED) then
        -- No currently picked slot and trying to acquire so check if this slot can be acquired.
        if (slotStatus == RELEASED or slotStatus == EMPTY) then
            -- Slot is available due to being empty or released
            acquired = true;
        elseif (lastHeartbeat < machineExpiryTime) then
            -- Stole the slot from another machine whose lease expired.
            acquired = true;
            slotStatus = EXPIRED;
        end
    end

    if (acquired) then
        acquiredSlotData = { slotNumber, slotMachineName, lastHeartbeat, slotStatus };
        slotLastHeartbeatFieldName = lastHeartbeatFieldName;
        slotMachineNameFieldName = machineNameFieldName;
        slotStatusFieldName = statusFieldName;
    end
end

if (acquiredSlotData == nil) then
    -- Could not acquire a slot
    return nil;
end

redis.call("HSET", slotsKey, slotLastHeartbeatFieldName, currentTime);
redis.call("HSET", slotsKey, slotMachineNameFieldName, machineName);
redis.call("HSET", slotsKey, slotStatusFieldName, status);

return acquiredSlotData;
