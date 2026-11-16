-- AutoFight-owned target vitals candidate producer (AFT2). Not loaded by RiftAddon.toc.
-- Read-only and fail-closed; integration requires review and live source qualification.
local _, privateVars = ...
local V2 = {}
privateVars.TargetVitalsV2 = V2
local MAX = 9007199254740991

local function finite(x)
    return type(x) == "number" and x == x and x ~= math.huge and x ~= -math.huge
end

local function id(x)
    if type(x) ~= "string" or #x == 0 or #x > 128 then return false end
    for i = 1, #x do
        local c = string.byte(x, i)
        if c < 32 or c > 126 then return false end
    end
    return true
end

local function quote(s)
    return "\"" .. string.gsub(string.gsub(s, "\\", "\\\\"), "\"", "\\\"") .. "\""
end

local function safe(api, name, arg)
    local ok, value = pcall(function()
        local unit = api.Unit
        if type(unit) ~= "table" or type(unit[name]) ~= "function" then return nil end
        return unit[name](arg)
    end)
    if not ok then return nil end
    return value
end

function V2.Capture(api, sequence, epoch, clock)
    if type(api) ~= "table" or not finite(sequence) or not finite(epoch) or
        sequence < 1 or epoch < 1 or sequence > MAX or epoch > MAX or
        sequence ~= math.floor(sequence) or epoch ~= math.floor(epoch) or
        not finite(clock) or clock < 0 then return nil, "invalid-clock-or-counter" end
    local playerBefore = safe(api, "Lookup", "player")
    local targetBefore = safe(api, "Lookup", "player.target")
    if not id(playerBefore) or not id(targetBefore) then return nil, "identity-unavailable" end
    local detail = safe(api, "Detail", targetBefore)
    local targetAfter = safe(api, "Lookup", "player.target")
    local playerAfter = safe(api, "Lookup", "player")
    if targetBefore ~= targetAfter or playerBefore ~= playerAfter or
        type(detail) ~= "table" then return nil, "identity-changed" end
    local ok, health, healthMax, detailId = pcall(function()
        return detail.health, detail.healthMax, detail.id
    end)
    if not ok or not id(detailId) or detailId ~= targetBefore or
        not finite(health) or not finite(healthMax) or healthMax <= 0 or
        health < 0 or health > healthMax then return nil, "health-unqualified" end
    local json = "{\"schemaVersion\":2,\"sequence\":" .. string.format("%.0f", sequence) ..
        ",\"epoch\":" .. string.format("%.0f", epoch) ..
        ",\"clock\":" .. string.gsub(string.format("%.17g", clock), ",", ".") ..
        ",\"playerId\":" .. quote(playerBefore) ..
        ",\"targetId\":" .. quote(targetBefore) ..
        ",\"health\":" .. string.gsub(string.format("%.17g", health), ",", ".") ..
        ",\"healthMax\":" .. string.gsub(string.format("%.17g", healthMax), ",", ".") .. "}"
    local frame = "AFT2|" .. json .. "|ENDAFT2"
    if #frame > 1024 then return nil, "frame-too-large" end
    return frame
end
