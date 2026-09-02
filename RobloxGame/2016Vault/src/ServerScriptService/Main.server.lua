--[[
	Main.server.lua
	Point d'entrée serveur : crée les RemoteEvents, branche les joueurs qui
	arrivent/partent sur DataService/LeaderstatsService, et relie les
	actions du client (ouvrir une capsule, placer un objet) aux services.
]]

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local DataService = require(script.Services.DataService)
local LeaderstatsService = require(script.Services.LeaderstatsService)
local CapsuleService = require(script.Services.CapsuleService)
local RoomService = require(script.Services.RoomService)

-- Dossier + RemoteEvents créés au démarrage (pas besoin de les placer
-- à la main dans Studio, tout est fait en code).
local remotesFolder = Instance.new("Folder")
remotesFolder.Name = "Remotes"
remotesFolder.Parent = ReplicatedStorage

local openCapsuleRemote = Instance.new("RemoteEvent")
openCapsuleRemote.Name = "OpenCapsule"
openCapsuleRemote.Parent = remotesFolder

local capsuleResultRemote = Instance.new("RemoteEvent")
capsuleResultRemote.Name = "CapsuleResult"
capsuleResultRemote.Parent = remotesFolder

local placeItemRemote = Instance.new("RemoteEvent")
placeItemRemote.Name = "PlaceItem"
placeItemRemote.Parent = remotesFolder

Players.PlayerAdded:Connect(function(player)
	local data = DataService.Load(player)
	LeaderstatsService.Setup(player, data)
end)

Players.PlayerRemoving:Connect(function(player)
	DataService.Release(player)
end)

openCapsuleRemote.OnServerEvent:Connect(function(player)
	local success, result = CapsuleService.OpenCapsule(player)
	capsuleResultRemote:FireClient(player, success, result)
end)

placeItemRemote.OnServerEvent:Connect(function(player, inventoryIndex)
	RoomService.PlaceItem(player, inventoryIndex)
end)

print("2016 Vault: serveur prêt.")
