-- ({ { key, value }[], deletedKeys, actualDeletedKeysCount }) GetOrClean(long maximumEmptyLastAccessTimeInSeconds, bool whatif, params string[] keys)
local maximumEmptyLastAccessTimeInSeconds = tonumber(ARGV[1]);
local whatif = tonumber(ARGV[2]);
-- Rest of arguments is are keys

local TRUE = 1;

local entries = {};
local index = 1;
local deletedKeysIndex = 0;
local actualDeletedKeysCount = 0;
local deletedKeys = {};

for i=3,#ARGV do
    local key = ARGV[i];
    local lastAccessTime = redis.call("OBJECT", "IDLETIME", key);
    local value = redis.pcall("GET", key);
    if (value and value.err == nil) then
        local locations = redis.call("BITCOUNT", key, 8, -1);
        if (locations == 0) then
            if (lastAccessTime > maximumEmptyLastAccessTimeInSeconds) then
                deletedKeysIndex = deletedKeysIndex + 1;
                if (whatif ~= TRUE) then
                    redis.call("DEL", key);
                    actualDeletedKeysCount = actualDeletedKeysCount + 1;
                end
            end

            deletedKeys[deletedKeysIndex] = key;
        else
            entries[index] = { key, value };
            index = index + 1;
        end
    end
end

return { entries, deletedKeys, actualDeletedKeysCount};
