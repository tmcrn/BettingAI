--[[
	LeaderstatsService.lua
	Crée le dossier "leaderstats" affiché par Roblox au-dessus de la tête
	des joueurs et dans le tableau des scores. Se synchronise avec
	DataService à chaque changement de Cash.
]]

local LeaderstatsService = {}

function LeaderstatsService.Setup(player, data)
	local leaderstats = Instance.new("Folder")
	leaderstats.Name = "leaderstats"
	leaderstats.Parent = player

	local cash = Instance.new("IntValue")
	cash.Name = "Vues"
	cash.Value = data.Cash
	cash.Parent = leaderstats

	return leaderstats
end

function LeaderstatsService.SetCash(player, amount)
	local leaderstats = player:FindFirstChild("leaderstats")
	if leaderstats then
		local cash = leaderstats:FindFirstChild("Vues")
		if cash then
			cash.Value = amount
		end
	end
end

return LeaderstatsService
