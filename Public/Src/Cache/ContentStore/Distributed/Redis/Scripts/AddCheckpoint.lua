-- (int slotNumber) AddCheckpoint(string checkpointsKey, string checkpointId, long sequenceNumber, long checkpointCreationTime, string machineName, int maxSlotCount)
local checkpointsKey = KEYS[1];
local checkpointId = ARGV[1];
local sequenceNumberRaw = ARGV[2];
local sequenceNumber = tonumber(sequenceNumberRaw);
local checkpointCreationTime = ARGV[3];
local machineName = ARGV[4];
local maxSlotCount = tonumber(ARGV[5]);

local selectedSlot = nil;
local selectedSlotSequenceNumber = sequenceNumber;

-- Find the slot to occupy/replace:
-- 1. Slot matches the machine
-- 2. Slot is empty
-- 3. Slot is has the lowest sequence number

for i=0,maxSlotCount-1 do
    local slotPrefix = "Slot#"..i..".";
    local slotMachineName = redis.call("HGET", checkpointsKey, slotPrefix.."MachineName");
    local slotSequenceNumber = tonumber(redis.call("HGET", checkpointsKey, slotPrefix.."SequenceNumber"));

    if (slotMachineName == machineName or slotMachineName == nil or slotSequenceNumber == nil) then
        selectedSlot = i;
        break;
    end

    if (selectedSlotSequenceNumber > slotSequenceNumber) then
        selectedSlot = i;
        selectedSlotSequenceNumber = slotSequenceNumber;
    end
end

if (selectedSlot ~= nil) then
    local slotPrefix = "Slot#"..selectedSlot..".";
    redis.call("HSET", checkpointsKey, slotPrefix.."CheckpointId", checkpointId);
    redis.call("HSET", checkpointsKey, slotPrefix.."SequenceNumber", sequenceNumberRaw);
    redis.call("HSET", checkpointsKey, slotPrefix.."CheckpointCreationTime", checkpointCreationTime);
    redis.call("HSET", checkpointsKey, slotPrefix.."MachineName", machineName);
end

return selectedSlot;
