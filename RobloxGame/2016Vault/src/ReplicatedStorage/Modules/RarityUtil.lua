--[[
	RarityUtil.lua
	Logique de tirage au sort pondéré + système de pity.
	Utilisé uniquement côté serveur (CapsuleService) pour éviter toute
	triche : le client ne doit jamais décider ce qu'il gagne.
]]

local CapsuleConfig = require(script.Parent.CapsuleConfig)

local RarityUtil = {}

local totalWeight = 0
for _, rarity in ipairs(CapsuleConfig.Rarities) do
	totalWeight += rarity.Weight
end

-- Rareté minimale garantie par le pity (la plus haute rareté considérée
-- "juteuse"). On garantit tout ce qui est Legendaire ou Viral.
local PITY_MIN_INDEX = 4 -- index de "Legendaire" dans CapsuleConfig.Rarities

--- Tire une rareté au hasard selon les poids définis dans CapsuleConfig.
local function rollRarityIndex(random)
	local roll = random:NextNumber(0, totalWeight)
	local cumulative = 0
	for index, rarity in ipairs(CapsuleConfig.Rarities) do
		cumulative += rarity.Weight
		if roll <= cumulative then
			return index
		end
	end
	return #CapsuleConfig.Rarities
end

--[[
	Ouvre une capsule pour un joueur.
	`pityCounter` = nombre d'ouvertures depuis le dernier Legendaire/Viral.
	Retourne : itemName, rarityId, rarityColor, newPityCounter
]]
function RarityUtil.OpenCapsule(random, pityCounter)
	local rarityIndex = rollRarityIndex(random)

	-- Pity : si le joueur n'a rien eu de rare depuis trop longtemps,
	-- on force au moins un Legendaire.
	if pityCounter >= CapsuleConfig.PityThreshold and rarityIndex < PITY_MIN_INDEX then
		rarityIndex = PITY_MIN_INDEX
	end

	local rarity = CapsuleConfig.Rarities[rarityIndex]
	local pool = CapsuleConfig.Items[rarity.Id]
	local itemName = pool[random:NextInteger(1, #pool)]

	local newPityCounter = pityCounter + 1
	if rarityIndex >= PITY_MIN_INDEX then
		newPityCounter = 0
	end

	return itemName, rarity.Id, rarity.Color, newPityCounter
end

return RarityUtil
