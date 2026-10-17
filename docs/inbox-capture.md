# Inbox 1.1 — capture explicite de texte, URL, image et fichiers

Issue #101, après le stockage sûr (#99) et le raccourci global (#100).

## Parcours

1. Avec Nodalis ouvert, saisir le raccourci Inbox configurable
   (par défaut **Ctrl+Shift+I**) ou choisir « Capturer dans Inbox »
   depuis la palette.
2. Saisir directement un texte/Markdown, ou cliquer sur
   « Importer le presse-papiers », ou « Choisir des fichiers… ».
3. Contrôler/compléter le texte présenté, puis Enregistrer ou Annuler.
   Une capture est écrite **uniquement après confirmation**.

Le presse-papiers **n'est jamais inspecté automatiquement**, ni à l'ouverture
du dialogue ni à chaque frappe. Le bouton d'import déclenche une lecture
ponctuelle ; aucun hook de presse-papiers, capture d'écran ou OCR.

## Types

| Type | Provenance explicite | Stockage |
| --- | --- | --- |
| text | texte saisi ou presse-papiers | capture.md |
| link | URL absolue HTTP(S) seule, sans autre ligne | capture.md |
| image | bitmap du presse-papiers, import demandé | capture.md + PNG local |
| file | fichiers du presse-papiers Windows ou sélecteur natif | capture.md + copies locales |

Une sélection de fichiers est résumée en Markdown lisible ; l'utilisateur
peut modifier la description. Les noms d'origine sont conservés dans
item.json mais les fichiers sont copiés sous un **GUID de stockage** pour
éviter les collisions. Les sources ne sont jamais modifiées.

## Sécurité et limites

- 1 à 10 fichiers sélectionnés par capture, au plus 25 Mio par fichier.
- Images du presse-papiers limitées à 40 millions de pixels avant encodage.
- Fichiers absents, liens symboliques et répertoires sont rejetés.
- La sauvegarde revérifie les fichiers sélectionnés, puis le service vérifie
  encore taille et chemin au moment de copier les octets.
- Une image du presse-papiers est d'abord encodée dans un fichier PNG
  temporaire unique, créé **après clic d'import** ; il est nettoyé après
  la capture ou son annulation, même en cas d'échec de persistance.
- Une URL HTTP(S) est conservée comme texte, sans téléchargement automatique.
  Les schémas `file:`, `javascript:` ou autres restent du texte ;
  rien n'est exécuté, interprété comme commande ou ouvert automatiquement.
- Si plusieurs formats coexistent dans le presse-papiers, priorité aux
  fichiers déposés, puis au bitmap, puis au texte. L'utilisateur peut
  choisir « Choisir des fichiers » à la place pour sélectionner moins
  d'éléments.

## Vérification interactive

- Saisir → raccourci → Enregistrer ; Annuler → aucun élément.
- Texte simple, URL unique et contenu multiligne.
- Bitmap Windows → copie PNG lisible après fermeture/réouverture.
- Presse-papiers FileDropList et sélection de 1 à 10 fichiers.
- 11+ fichiers, fichier effacé, verrouillé, >25 Mio et bitmap immense :
  erreur visible sans écrasement des captures précédentes.
- Pas d'exécution d'un fichier joint ou d'une URL capturée.
- Dark mode, DPI 100/125/150 %, navigation au clavier et fenêtre restaurée.

Smoke tests automatiques : classification, sélections, persistance,
réouverture après redémarrage, manque de pièce jointe, quarantaine,
restauration ; les API du presse-papiers exigent une session Windows
interactive et nécessitent une vérification manuelle complémentaire.
