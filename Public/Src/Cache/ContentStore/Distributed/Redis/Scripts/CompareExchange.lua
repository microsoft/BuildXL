-- bool CompareExchange(string: weakFingerprintKey, byte[]: selectorFieldName, byte[] tokenFieldName, string expectedToken, byte[] contentHashList, string newReplacementToken)

-- StrongFingerprint strongFingerprint, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement
local weakFingerprintKey = KEYS[1];
local selectorFieldName = ARGV[1];
local tokenFieldName = ARGV[2];
local expectedToken = ARGV[3];
local contentHashList = ARGV[4];
local newReplacementToken = ARGV[5];

local actualToken = redis.call("HGET", weakFingerprintKey, tokenFieldName);
if (actualToken == nil or actualToken == expectedToken)
    redis.call("HSET", weakFingerprintKey, selectorFieldName, contentHashList);
    redis.call("HSET", weakFingerprintKey, tokenFieldName, newReplacementToken);
    return true;
end

return false;
