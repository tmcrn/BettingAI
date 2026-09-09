#!/bin/zsh
# Double-clique ce fichier dans le Finder pour lancer Robert (BettingAI) :
# ça ouvre un Terminal et fait `dotnet run` tout seul, sans taper de commande.
#
# Si jamais tu déplaces le projet ailleurs que ~/Desktop/BettingAI, change
# le chemin ci-dessous en conséquence.

cd ~/Desktop/BettingAI || {
    echo "❌ Dossier introuvable : ~/Desktop/BettingAI"
    echo "   (le projet a peut-être été déplacé - édite ce fichier pour corriger le chemin)"
    read "?Appuie sur Entrée pour fermer..."
    exit 1
}

echo "🤖 Lancement de Robert..."
echo ""

dotnet run

# Le Terminal reste ouvert après un arrêt (Ctrl+C, crash, etc.) pour que
# tu puisses lire les derniers messages avant que la fenêtre disparaisse.
echo ""
echo "Robert s'est arrêté."
read "?Appuie sur Entrée pour fermer cette fenêtre..."
