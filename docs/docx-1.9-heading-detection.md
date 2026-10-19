# Nodalis 1.9 — Détection des titres Word (#156)

Le parseur DOCX natif gère désormais les styles de paragraphes hérités avec `w:basedOn`, les niveaux définis dans `styles.xml`, ainsi que les valeurs directes `w:pPr/w:outlineLvl` sur un paragraphe. Le niveau Word 0 correspond au titre Nodalis 1 ; Word 8 correspond au titre 9 ; Word 9 correspond au **corps de texte**, pas à un titre.

L'ordre de résolution est le suivant : **définition directe du paragraphe**, **niveau explicite du style**, **nom Heading/Titre 1..9**, puis **niveau hérité**. Les cycles de styles ne provoquent pas de récursion infinie. Le traitement fonctionne sur les XML internes, sans Word installé ni IA.

## Titres visuels non stylés

Un texte court, majoritairement gras et avec une taille explicite d'au moins 16 points, ou avec un espacement significatif avant, peut produire `SuggestedHeadingLevel` et `HeadingSuggestionReason`. **Ce n'est jamais converti automatiquement en section** : l'utilisateur devra accepter ou rejeter la suggestion dans l'éditeur d'arbre (#157). L'intention est de proposer une aide explicable pour les Word mal structurés, pas d'inférer le sens du contenu.

Les styles et suggestions sont conservés sur les nœuds du modèle commun #155. Les assertions vérifient des styles personnalisés hérités, un niveau direct, le niveau body-text 9, les cycles et une suggestion réversible sur un DOCX créé entièrement en mémoire ZIP/XML.
