--[[
	CapsuleConfig.lua
	Toute la config du jeu "2016 Vault" au même endroit : raretés, objets,
	prix, revenus passifs, pity. Modifie juste ce fichier pour ajouter du
	contenu, aucun autre script n'a besoin d'être touché.
]]

local CapsuleConfig = {}

-- Ordre du moins rare au plus rare. Le "Weight" sert au tirage pondéré :
-- plus il est haut, plus la rareté sort souvent. La somme des poids
-- n'a pas besoin de faire 100, RarityUtil la normalise.
CapsuleConfig.Rarities = {
	{ Id = "Commun",     Weight = 600, Color = Color3.fromRGB(176, 176, 176) },
	{ Id = "Rare",       Weight = 250, Color = Color3.fromRGB(85, 170, 255) },
	{ Id = "Epique",     Weight = 110, Color = Color3.fromRGB(170, 85, 255) },
	{ Id = "Legendaire", Weight = 35,  Color = Color3.fromRGB(255, 170, 0) },
	{ Id = "Viral",      Weight = 5,   Color = Color3.fromRGB(255, 0, 128) },
}

-- Objets "culte 2016" par rareté. Ajoute des noms ici pour enrichir le loot.
CapsuleConfig.Items = {
	Commun = {
		"Silly Bandz", "Autocollant Smiley", "Pin's Emoji", "Bracelet Loom Basique",
	},
	Rare = {
		"Slime Pastel", "Casquette Snapback", "Loom Arc-en-ciel", "Sticker Dab",
	},
	Epique = {
		"Fidget Cube", "Peluche Minion", "Écouteurs Filaires Stylés", "Skin Vine Classique",
	},
	Legendaire = {
		"Fidget Spinner Holographique", "Slime Cristal Géant", "Casque Beats Rétro",
	},
	Viral = {
		"Fidget Spinner Doré", "Capsule Vine Légendaire (son Damn Daniel)",
	},
}

-- "Vues" générées par seconde quand l'objet est placé dans la Chambre Rétro.
CapsuleConfig.ViewsPerSecond = {
	Commun = 1,
	Rare = 3,
	Epique = 8,
	Legendaire = 25,
	Viral = 100,
}

-- Coût en Vues (monnaie) d'une capsule "normale" achetée avec l'argent du jeu.
CapsuleConfig.CapsuleCost = 50

-- Pity : après ce nombre d'ouvertures sans obtenir Legendaire/Viral,
-- la prochaine capsule garantit au moins un Legendaire.
CapsuleConfig.PityThreshold = 40

return CapsuleConfig
