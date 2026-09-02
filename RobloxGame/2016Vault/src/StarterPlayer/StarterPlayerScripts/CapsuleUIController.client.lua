--[[
	CapsuleUIController.client.lua
	Construit toute l'interface (bouton "Ouvrir Capsule" + popup de résultat)
	directement en Lua, sans rien à assembler à la main dans Studio.
	Écoute le résultat renvoyé par le serveur et l'affiche.
]]

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local TweenService = game:GetService("TweenService")

local player = Players.LocalPlayer
local remotes = ReplicatedStorage:WaitForChild("Remotes")
local openCapsuleRemote = remotes:WaitForChild("OpenCapsule")
local capsuleResultRemote = remotes:WaitForChild("CapsuleResult")

-- === Construction de l'interface ===

local screenGui = Instance.new("ScreenGui")
screenGui.Name = "CapsuleUI"
screenGui.ResetOnSpawn = false
screenGui.Parent = player:WaitForChild("PlayerGui")

local openButton = Instance.new("TextButton")
openButton.Name = "OpenCapsuleButton"
openButton.Size = UDim2.new(0, 220, 0, 60)
openButton.Position = UDim2.new(0.5, -110, 0.85, 0)
openButton.BackgroundColor3 = Color3.fromRGB(255, 0, 128)
openButton.Text = "🕹️ Ouvrir une Capsule 2016"
openButton.TextColor3 = Color3.new(1, 1, 1)
openButton.TextScaled = true
openButton.Font = Enum.Font.GothamBold
openButton.Parent = screenGui

local corner = Instance.new("UICorner")
corner.CornerRadius = UDim.new(0, 12)
corner.Parent = openButton

local resultFrame = Instance.new("Frame")
resultFrame.Name = "ResultFrame"
resultFrame.Size = UDim2.new(0, 300, 0, 140)
resultFrame.Position = UDim2.new(0.5, -150, 0.3, 0)
resultFrame.BackgroundColor3 = Color3.fromRGB(30, 30, 40)
resultFrame.Visible = false
resultFrame.Parent = screenGui

local resultCorner = Instance.new("UICorner")
resultCorner.CornerRadius = UDim.new(0, 16)
resultCorner.Parent = resultFrame

local rarityLabel = Instance.new("TextLabel")
rarityLabel.Name = "RarityLabel"
rarityLabel.Size = UDim2.new(1, 0, 0, 40)
rarityLabel.BackgroundTransparency = 1
rarityLabel.Font = Enum.Font.GothamBold
rarityLabel.TextScaled = true
rarityLabel.Parent = resultFrame

local itemLabel = Instance.new("TextLabel")
itemLabel.Name = "ItemLabel"
itemLabel.Size = UDim2.new(1, 0, 0, 60)
itemLabel.Position = UDim2.new(0, 0, 0, 45)
itemLabel.BackgroundTransparency = 1
itemLabel.TextColor3 = Color3.new(1, 1, 1)
itemLabel.Font = Enum.Font.Gotham
itemLabel.TextScaled = true
itemLabel.Parent = resultFrame

-- === Logique ===

local RARITY_LABELS = {
	Commun = "COMMUN",
	Rare = "RARE",
	Epique = "ÉPIQUE",
	Legendaire = "LÉGENDAIRE",
	Viral = "✨ VIRAL ✨",
}

local resultHideThread

openButton.MouseButton1Click:Connect(function()
	openCapsuleRemote:FireServer()
end)

capsuleResultRemote.OnClientEvent:Connect(function(success, result)
	if not success then
		-- `result` est une chaîne d'erreur ici (ex: "PAS_ASSEZ_DE_VUES")
		openButton.Text = "❌ " .. tostring(result)
		task.delay(1.5, function()
			openButton.Text = "🕹️ Ouvrir une Capsule 2016"
		end)
		return
	end

	local color = Color3.new(result.Color[1], result.Color[2], result.Color[3])
	rarityLabel.Text = RARITY_LABELS[result.Rarity] or result.Rarity
	rarityLabel.TextColor3 = color
	itemLabel.Text = result.ItemName
	resultFrame.BackgroundColor3 = Color3.fromRGB(30, 30, 40)

	resultFrame.Visible = true
	resultFrame.Size = UDim2.new(0, 0, 0, 0)
	resultFrame.Position = UDim2.new(0.5, 0, 0.3, 70)

	TweenService:Create(resultFrame, TweenInfo.new(0.25, Enum.EasingStyle.Back, Enum.EasingDirection.Out), {
		Size = UDim2.new(0, 300, 0, 140),
		Position = UDim2.new(0.5, -150, 0.3, 0),
	}):Play()

	if resultHideThread then
		task.cancel(resultHideThread)
	end
	resultHideThread = task.delay(2.5, function()
		resultFrame.Visible = false
	end)
end)
