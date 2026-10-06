# Accessibilité et cohérence visuelle

Ce document décrit la baseline d'accessibilité de Nodalis 1.0 et l'audit appliqué à l'interface WPF.

## Couverture

Toutes les fenêtres et boîtes de dialogue de `src/Nodalis.App` chargent `Themes/Dark.xaml` via les ressources de l'application. `App.xaml.cs` applique explicitement `NodalisWindowStyle` à chaque `Window` dérivée avant son premier rendu, ce qui évite les retours au thème Windows clair sur les dialogs.

Le script `scripts/verify-accessibility.ps1` inspecte récursivement tous les fichiers XAML en CI. Il bloque notamment :

- la désactivation explicite du focus clavier ;
- les brosses noir/blanc codées en dur sur les surfaces principales ;
- les fenêtres sans titre ;
- la disparition des styles globaux indispensables pour le focus, les menus, les calendriers, les listes et les arborescences.

## Clavier

La navigation par tabulation est cyclique à l'intérieur de chaque fenêtre. Les contrôles interactifs principaux utilisent un anneau de focus commun à fort contraste, y compris les boutons, zones de texte, listes, arbres, onglets, cases à cocher, boutons radio, calendriers et menus.

Les raccourcis déclarés dans `ShortcutCatalog` sont vérifiés au démarrage. Deux commandes ne peuvent pas partager le même identifiant ni la même combinaison de touches sans provoquer un échec explicite. Le mode `--smoke-test` utilisé en CI exécute également cette vérification.

## Dark mode et contraste

La palette reste centralisée dans `Themes/Dark.xaml`. Les contrastes de référence sont volontairement élevés :

- texte principal `#F1F3F5` sur fond fenêtre `#17191D` : environ 15,8:1 ;
- texte secondaire `#A9B0BC` sur fond panneau `#1F2228` : environ 7,3:1 ;
- accent `#7AA2F7` sur fond fenêtre `#17191D` : environ 7,0:1.

Le texte désactivé est moins contrasté par conception et ne doit pas être utilisé pour transmettre une information active.

## DPI Windows

Les fenêtres activent `UseLayoutRounding`, `SnapsToDevicePixels` et le rendu texte `Display/ClearType`. Les dimensions minimales globales évitent qu'un dialog devienne inutilisable à fort facteur d'échelle, tout en laissant les dimensions locales plus strictes prendre le dessus.

## Lecteurs d'écran

Nodalis conserve les contrôles WPF natifs et leurs propriétés sémantiques plutôt que de remplacer l'interface par des surfaces dessinées sans automatisation UI. Les fenêtres doivent conserver un `Title` et les contrôles interactifs doivent garder un contenu textuel ou une propriété d'automatisation lorsqu'une icône seule est utilisée.

## Vérification avant release

Avant une Release Candidate, vérifier au minimum : navigation Tab/Shift+Tab complète, activation via Entrée/Espace, menus au clavier, calendriers au clavier, focus visible, zoom/DPI Windows à 100/125/150/200 %, et lecture des commandes principales avec Narrator.
