# Nodalis 1.9 — Détection des titres Word (#156)

Le parseur DOCX natif gère désormais les styles de paragraphes hérités avec `w:basedOn`, les niveaux définis dans `styles.xml`, ainsi que les valeurs directes `w:pPr/w:outlineLvl` sur un paragraphe. Le niveau Word 0 correspond au titre Nodalis 1 ; Word 8 correspond au titre 9 ; Word 9 correspond au **corps de texte**, pas à un titre.

L'ordre de résolution est le suivant : **définition directe du paragraphe**, **niveau explicite du style**, **nom Heading/Titre 1..9**, puis **niveau hérité**. Les cycles de styles ne provoquent pas de récursion infinie. Le traitement fonctionne sur les XML internes, sans Word installé ni IA.

## Titres visuels non stylés

Un texte court, majoritairement gras et avec une taille explicite d'au moins 16 points, ou avec un espacement significatif avant, peut produire `SuggestedHeadingLevel` et `HeadingSuggestionReason`. **Ce n'est jamais converti automatiquement en section** : l'utilisateur devra accepter ou rejeter la suggestion dans l'éditeur d'arbre (#157). L'intention est de proposer une aide explicable pour les Word mal structurés, pas d'inférer le sens du contenu.

Les styles et suggestions sont conservés sur les nœuds du modèle commun #155. Les tests automatisés couvrent maintenant les styles personnalisés hérités, les niveaux directs (dont le corps de texte 9), les cycles, les noms de styles **Titre** (FR) et **Heading** (EN), les titres imbriqués/répétés, une page de garde non titrée, une liste numérotée et un tableau intercalés, ainsi qu'une proposition de titre non appliquée. Le DOCX de test est construit directement en ZIP/XML, sans Office ni package tiers.

**Qualification manuelle restante avant clôture :** ouvrir un vrai document Word francophone/anglophone, inspecter l'arbre et accepter/refuser une suggestion depuis l'interface ; vérifier le résultat dans le dry-run et après import. Voir `docs/docx-1.9-manual-qualification.md`.
