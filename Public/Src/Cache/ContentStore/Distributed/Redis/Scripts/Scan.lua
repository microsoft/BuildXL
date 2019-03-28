-- ({ nextCursor, key[] }) Scan(string cursor, int entryCount)
local cursor = ARGV[1];
local entryCount = tonumber(ARGV[2]);

return redis.call("SCAN", cursor, "COUNT", entryCount);
