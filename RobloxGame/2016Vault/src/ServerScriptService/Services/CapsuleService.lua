--[[
	CapsuleService.lua
	Gère l'achat/ouverture des Capsules Temporelles 2016. C'est le coeur
	du système "gacha" : tout le calcul se fait ici, côté serveur, jamais
	côté client (sinon un joueur pourrait tricher).
]]

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Random_ = Random.new(os.clock() * 1000)

local CapsuleConfig = require(ReplicatedStorage.Modules.CapsuleConfig)
local RarityUtil = require(ReplicatedStorage.Modules.RarityUtil)

local DataService = require(script.Parent.DataService)
local LeaderstatsService = require(script.Parent.LeaderstatsService)

local CapsuleService = {}

--[[
	Tente d'ouvrir une capsule pour `player`.
	Retourne (success: boolean, resultOrReason)
	  - si succès : { ItemName, Rarity, Color = {r,g,b} }
	  - si échec  : chaîne expliquant pourquoi (ex: "PAS_ASSEZ_DE_VUES")
]]
function CapsuleService.OpenCapsule(player)
	local data = DataService.Get(player)
	if not data then
		return false, "DONNEES_NON_CHARGEES"
	end

	if data.Cash < CapsuleConfig.CapsuleCost then
		return false, "PAS_ASSEZ_DE_VUES"
	end

	data.Cash -= CapsuleConfig.CapsuleCost

	local itemName, rarityId, rarityColor, newPity =
		RarityUtil.OpenCapsule(Random_, data.PityCounter)

	data.PityCounter = newPity
	table.insert(data.Inventory, { Name = itemName, Rarity = rarityId })

	LeaderstatsService.SetCash(player, data.Cash)

	return true, {
		ItemName = itemName,
		Rarity = rarityId,
		Color = { rarityColor.R, rarityColor.G, rarityColor.B },
	}
end

return CapsuleService
