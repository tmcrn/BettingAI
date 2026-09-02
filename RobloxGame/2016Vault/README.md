# 2016 Vault 🕹️✨

Jeu Roblox de collection/gacha sur le thème "nostalgie 2016" : ouvre des
capsules temporelles, obtiens des objets cultes de raretés différentes,
place-les dans ta Chambre Rétro pour générer des Vues en continu.

Ce dossier contient **tout le code déjà fonctionnel** : système de
capsules pondéré avec pity, sauvegarde des données, revenu passif,
interface joueur générée en Lua. Tu n'as rien à assembler à la main dans
Studio — seulement à connecter Rojo et appuyer sur Play.

## Étape 1 — Installer les outils (une seule fois)

1. **Roblox Studio** : télécharge-le sur https://create.roblox.com/ (bouton
   "Start Creating" → connecte-toi avec ton compte Roblox).
2. **Rojo** (l'outil qui synchronise ce dossier de code avec Studio) :
   - Installe [Aftman](https://github.com/LPGhatguy/aftman#installation)
     (le gestionnaire d'outils recommandé par la communauté Roblox).
   - Dans un terminal, place-toi dans ce dossier (`RobloxGame/2016Vault`)
     puis lance :
     ```
     aftman install
     ```
     (si un fichier `aftman.toml` n'existe pas encore chez toi, dis-le moi,
     je peux aussi te donner la version "installe Rojo directement" sans
     Aftman).
3. **Plugin Rojo dans Studio** : ouvre Roblox Studio → onglet "Plugins" →
   "Manage Plugins" → cherche "Rojo" dans le Toolbox → installe-le.

## Étape 2 — Créer ton jeu sur Roblox

1. Ouvre Roblox Studio → "New" → modèle **Baseplate**.
2. Sauvegarde-le et publie-le une première fois (File → Publish to Roblox)
   pour qu'il existe sur ton compte.

## Étape 3 — Synchroniser le code avec Studio

1. Dans un terminal, place-toi dans `RobloxGame/2016Vault` et lance :
   ```
   rojo serve
   ```
2. Dans Studio, ouvre l'onglet "Plugins" → clique sur "Rojo" → "Connect".
3. Le code apparaît instantanément dans l'explorer de Studio
   (ReplicatedStorage, ServerScriptService, StarterPlayer...).

## Étape 4 — Tester

Clique sur "Play" en haut de Studio. Tu dois voir :
- Un bouton rose **"🕹️ Ouvrir une Capsule 2016"** en bas de l'écran.
- Ton compteur **"Vues"** dans le tableau des scores à droite.
- En cliquant sur le bouton, un objet apparaît avec sa rareté (Commun,
  Rare, Épique, Légendaire, ✨Viral✨).

## Comment le jeu est organisé

```
2016Vault/
  default.project.json          → dit à Rojo où mettre chaque fichier
  src/
    ReplicatedStorage/Modules/
      CapsuleConfig.lua          → TOUTE la config : raretés, objets, prix
      RarityUtil.lua             → le tirage au sort pondéré + pity
    ServerScriptService/
      Main.server.lua            → point d'entrée, crée les RemoteEvents
      Services/
        DataService.lua          → sauvegarde/chargement (DataStore)
        LeaderstatsService.lua   → le compteur "Vues" affiché aux joueurs
        CapsuleService.lua       → logique d'ouverture d'une capsule
        RoomService.lua          → revenu passif de la Chambre Rétro
    StarterPlayer/StarterPlayerScripts/
      CapsuleUIController.client.lua → toute l'interface (bouton + popup)
```

**Pour ajouter du contenu** (nouveaux objets, nouvelles raretés, changer
les prix) : modifie uniquement `CapsuleConfig.lua`, rien d'autre à toucher.

## Prochaines étapes (roadmap monétisation)

Ce qui est déjà en place = la boucle de jeu gratuite complète. Pour
monétiser, dans l'ordre de priorité :

1. **Game Passes** (Studio → Monetization → Game Passes) : "2x Vues",
   "2x Chance", slots de Chambre Rétro supplémentaires. Se vérifient avec
   `MarketplaceService:UserOwnsGamePassAsync`.
2. **Developer Products** : capsule premium à l'unité, "capsule garantie
   Légendaire". Se déclenchent via `MarketplaceService:PromptProductPurchase`.
3. **Battle Pass hebdomadaire** thématique (nouvel objet du moment lié à
   la trend TikTok en cours).
4. **Anti-triche renforcé** avant de sortir de vrais paiements : limiter
   la fréquence d'ouverture côté serveur (déjà partiellement fait via le
   coût en Vues), logs des transactions.

⚠️ Respecte les [règles Roblox sur les objets aléatoires](https://en.help.roblox.com/hc/en-us/articles/8592065628180) :
les probabilités de chaque rareté doivent être affichées publiquement dans
le jeu avant tout achat réel de capsule.
