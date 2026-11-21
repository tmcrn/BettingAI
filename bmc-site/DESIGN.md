# BMC : guide de style

Source de vérité pour le design. Ne pas improviser en dehors de ce document.

## Identité verrouillée (ne jamais modifier)
- Logo : `assets/logo-bmc.jpeg` (casque rétro, lunettes, barbe, drapeaux à damier). Toujours sur fond blanc ou dans une pastille blanche ronde.
- Palette officielle (coolors) : jaune `#FAF019`, bleu `#434A7C`, noir `#000000`, brun `#483527`. Compléments neutres : blanc `#FFFFFF`, gris clair `#EEF0F2`, gris texte `#5A5C60`, gris moyen `#9A9CA0`.
- Typographie : **Permanent Marker** (titres, chiffres de date, montants de remise) et **Barlow** (tout le reste).

## Principes
1. Pensé pour le pouce : usage sur téléphone d'abord. Cibles tactiles ≥ 44 px, actions principales en bas ou pleine largeur.
2. Un seul moment mémorable : la bande damier. Le reste est calme.
3. Bords nets, contours noirs de 3 px, ombre dure décalée (style autocollant). Pas de dégradés décoratifs, pas d'ombres douces.
4. Texte en phrases, verbes clairs (« Je viens », « Voir la prochaine sortie »). Pas de majuscules forcées.
5. Les photos du club remplaceront les emplacements bruns, bleus et noirs. Ne jamais inventer de photo.

## Échelle
- Texte courant 17 px, interligne 1,5. Titre de page 30 px (Marker). Titre de section 21 px (Barlow 700).
- Espacement : 4 · 8 · 12 · 14 · 20 px. Marge d'écran 20 px.
- Largeur maximale de l'appli : 480 px, centrée.

## Composants
- Bouton : fond jaune, contour noir 3 px, rayon 6 px, hauteur ≥ 48 px, ombre dure 3 px. Appui : l'ombre disparaît et le bouton s'enfonce.
- Carte de sortie : date noire (jour en Marker jaune), contour noir 3 px.
- Album : emplacement photo plein cadre, voile noir 40 %, titre Marker blanc.
- Barre d'onglets : noire, filet jaune 4 px, onglet actif en jaune.
- Carte adhérent : fond noir, contour jaune 4 px, rayon 14 px.

## Mouvement
- Entrée d'un écran : les blocs montent de 10 px en fondu, décalés de 45 ms (max. 10 blocs). Une seule fois par changement d'écran.
- Bande damier : défile une fois au chargement.
- Réponses aux gestes : bouton qui s'enfonce, « Je viens » qui rebondit, icône d'onglet qui sursaute, message qui glisse.
- Tout est désactivé avec `prefers-reduced-motion`.

## Accessibilité
- Contraste texte ≥ 4,5:1. Focus visible partout (3 px bleu, jaune sur fond noir).
- Chaque champ a une étiquette. Les états sont dits en texte, jamais par la couleur seule.
