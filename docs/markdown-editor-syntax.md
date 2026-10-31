# Coloration du Markdown source — Nodalis 1.9

## Objectif

Améliorer la lecture des documents Markdown volumineux dans les deux panneaux d'édition, sans modifier le texte source ni remplacer le `TextBox` WPF responsable de la saisie.

## Utilisation

- Ouvrir un document Markdown et laisser la case **Couleurs** activée dans la barre d'outils.
- Les titres, citations, liens, emphases, code inline, cases à cocher et métadonnées sont différenciés par la palette sombre de Nodalis.
- Décochez **Couleurs** pour retrouver instantanément le rendu WPF standard. Le choix est sauvegardé dans les préférences du workspace lors de la fermeture normale de l'application.
- Les repères colorés à gauche des sections restent indépendants de la coloration du texte.

## Architecture

- `MarkdownSyntaxTokenizer` (Nodalis.Core) transforme **une ligne** en plages contiguës associées à des rôles de couleur. Il ne modifie jamais le contenu.
- `MarkdownSyntaxOverlay` (Nodalis.App) recrée uniquement les lignes visuelles présentes à l'écran dans un `Canvas` non interactif. Le `TextBox` original conserve clavier, souris, sélection, caret, commandes, presse-papiers et sauvegarde.
- Un délai court regroupe les éditions rapides et les événements de défilement.
- Dès qu'une édition, un scroll ou un changement de taille survient, l'affichage natif redevient immédiatement visible en attendant la prochaine mise en couleur.
- Chaque rendu est limité à 110 lignes visuelles, 1 200 caractères par ligne et 16 000 caractères cumulés. Les lignes avec tabulations passent également au rendu natif pour éviter les différences de taquets.
- Si la géométrie du texte ne peut pas être déterminée, si les limites sont dépassées, ou si la coloration est désactivée, le texte natif normal est conservé. Il n'y a ni modification ni sérialisation du Markdown par l'overlay.
- La coloration des blocs de code multiligne se limite actuellement à leurs délimiteurs et au code inline ; l'interprétation complète du langage de chaque bloc n'est pas encore implémentée.

## Qualification indispensable sous Windows

L'affichage WPF ne peut être validé par des tests de parsing seuls. Avant release, tester en particulier :

1. La superposition des caractères colorés au texte réel et le curseur clignotant pendant la frappe.
2. La sélection partielle de textes colorés et les opérations couper/copier/coller/annuler.
3. Les retours à la ligne automatiques, les caractères accentués, les emojis et le DPI 100/125/150 %.
4. La navigation rapide entre onglets et le double panneau avec 100 000 puis 1 000 000 caractères.
5. La désactivation immédiate de la couleur, la persistance du réglage et l'absence de régression de l'autosave.

La fiche de qualification correspondante se trouve dans le workspace démo, sous **Tests / Qualification 1.9 - Couleurs Markdown**.
