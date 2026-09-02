--[[
	DataService.lua
	Sauvegarde/chargement des données joueur avec DataStoreService.
	Gère les erreurs réseau (pcall + retries) et sauvegarde automatiquement
	à intervalle régulier + à la déconnexion + à la fermeture du serveur.
]]

local DataStoreService = game:GetService("DataStoreService")
local Players = game:GetService("Players")

local VaultStore = DataStoreService:GetDataStore("Vault2016_PlayerData_v1")

local DataService = {}

local cache = {} -- [player] = dataTable

local DEFAULT_DATA = {
	Cash = 100,
	PityCounter = 0,
	Inventory = {}, -- liste de {Name=..., Rarity=...}
	RoomItems = {}, -- liste des objets placés dans la Chambre Rétro
}

local function deepCopy(t)
	local copy = {}
	for k, v in pairs(t) do
		copy[k] = (type(v) == "table") and deepCopy(v) or v
	end
	return copy
end

local function loadWithRetry(key, attempts)
	attempts = attempts or 3
	for i = 1, attempts do
		local ok, result = pcall(function()
			return VaultStore:GetAsync(key)
		end)
		if ok then
			return true, result
		end
		warn(("DataService: échec GetAsync (%d/%d) pour %s: %s"):format(i, attempts, key, tostring(result)))
		task.wait(1.5 * i)
	end
	return false, nil
end

local function saveWithRetry(key, data, attempts)
	attempts = attempts or 3
	for i = 1, attempts do
		local ok, err = pcall(function()
			VaultStore:SetAsync(key, data)
		end)
		if ok then
			return true
		end
		warn(("DataService: échec SetAsync (%d/%d) pour %s: %s"):format(i, attempts, key, tostring(err)))
		task.wait(1.5 * i)
	end
	return false
end

function DataService.Load(player)
	local key = "Player_" .. player.UserId
	local ok, saved = loadWithRetry(key)

	local data
	if ok and saved then
		data = saved
	else
		data = deepCopy(DEFAULT_DATA)
	end

	cache[player] = data
	return data
end

function DataService.Get(player)
	return cache[player]
end

function DataService.Save(player)
	local data = cache[player]
	if not data then
		return
	end
	local key = "Player_" .. player.UserId
	saveWithRetry(key, data)
end

function DataService.Release(player)
	DataService.Save(player)
	cache[player] = nil
end

-- Sauvegarde automatique toutes les 2 minutes pour limiter les pertes
-- en cas de crash serveur.
task.spawn(function()
	while true do
		task.wait(120)
		for _, player in ipairs(Players:GetPlayers()) do
			if cache[player] then
				DataService.Save(player)
			end
		end
	end
end)

game:BindToClose(function()
	for _, player in ipairs(Players:GetPlayers()) do
		if cache[player] then
			DataService.Save(player)
		end
	end
end)

return DataService
