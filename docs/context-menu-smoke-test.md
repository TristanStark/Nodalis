# Smoke test UI — menus contextuels

Cette checklist protège le correctif de l'issue #88 et complète l'audit
automatisé. Elle doit être rejouée sur une Release Candidate avant le tag
stable lorsqu'un changement touche aux menus, à l'arborescence ou au thème.

## Matrice

Exécuter la totalité du scénario avec une mise à l'échelle Windows de **100 %**,
**125 %** puis **150 %**. La validation finale 1.0 doit couvrir Windows 10 x64
et Windows 11 x64 conformément à `release-checklist.md`.

## Éditeur Markdown

1. Ouvrir un document situé dans un projet.
2. Sélectionner un mot puis faire un clic droit au milieu de la sélection.
3. Vérifier que le menu est sombre dès le **premier** clic droit.
4. Vérifier que Couper, Copier, Coller, les séparateurs et les états désactivés
   restent lisibles.
5. Vérifier la présence de **Ajouter « … » au glossaire du projet**.
6. Cliquer cette action, saisir une définition et valider.
7. Vérifier que l'entrée apparaît dans le `Glossaire.md` du projet courant.
8. Refaire le test sans sélection : le mot sous le pointeur doit être proposé.
9. Ouvrir le menu près des bords haut, bas, gauche et droit de l'écran : aucune
   partie du menu ne doit être rognée et le menu doit rester défilable si sa
   hauteur disponible est insuffisante.

## Arborescence

1. Faire un clic droit sur un projet, un module puis un document.
2. Vérifier que l'élément ciblé devient sélectionné avant l'ouverture du menu.
3. Pendant que le menu est ouvert, vérifier que la sélection reste visible par
   le fond accentué, la barre d'accent et le texte renforcé.
4. Vérifier que le menu et ses sous-menus restent entièrement sombres.
5. Refaire les ouvertures près de chaque bord de l'écran.

## Critère de réussite

Le scénario est validé uniquement si aucun fond système blanc n'apparaît, si le
premier clic droit affiche bien le menu Nodalis, si l'action glossaire cible par
défaut le projet courant, si la sélection d'arborescence reste identifiable et
si aucun menu n'est coupé aux trois niveaux de DPI.
