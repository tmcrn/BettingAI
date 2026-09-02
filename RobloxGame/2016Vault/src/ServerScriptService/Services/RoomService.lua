--[[
	RoomService.lua
	Gère la "Chambre Rétro" : placer un objet de l'inventaire dedans le
	fait générer des Vues (monnaie) passivement, en continu, tant qu'il
	y reste. C'est le moteur du "idle income" qui donne envie de revenir.
]]

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Players = game:GetService("Players")

local CapsuleConfig = require(ReplicatedStorage.Modules.CapsuleConfig)

local DataService = require(script.Parent.DataService)
local LeaderstatsService = require(script.Parent.LeaderstatsService)

local RoomService = {}

--[[
	Déplace l'objet à l'index `inventoryIndex` de l'inventaire vers la
	Chambre Rétro. Retourne (success, reason?)
]]
function RoomService.PlaceItem(player, inventoryIndex)
	local data = DataService.Get(player)
	if not data then
		return false, "DONNEES_NON_CHARGEES"
	end

	local item = data.Inventory[inventoryIndex]
	if not item then
		return false, "OBJET_INTROUVABLE"
	end

	table.remove(data.Inventory, inventoryIndex)
	table.insert(data.RoomItems, item)

	return true
end

-- Calcule le revenu total par seconde généré par tout ce qui est placé
-- dans la Chambre Rétro d'un joueur.
local function computeIncomePerSecond(data)
	local total = 0
	for _, item in ipairs(data.RoomItems) do
		total += CapsuleConfig.ViewsPerSecond[item.Rarity] or 0
	end
	return total
end

-- Boucle de revenu passif : tourne toutes les 5 secondes pour tous les
-- joueurs connectés. Un intervalle de 5s (plutôt que chaque frame) suffit
-- largement pour un jeu idle et évite de gaspiller des ressources serveur.
local TICK_SECONDS = 5

task.spawn(function()
	while true do
		task.wait(TICK_SECONDS)
		for _, player in ipairs(Players:GetPlayers()) do
			local data = DataService.Get(player)
			if data then
				local income = computeIncomePerSecond(data) * TICK_SECONDS
				if income > 0 then
					data.Cash += income
					LeaderstatsService.SetCash(player, data.Cash)
				end
			end
		end
	end
end)

return RoomService
