# Nodalis 1.9 — Contrat d'arbre d'import commun (#155)

**Statut : premier incrément sur feature/1.9-import-document-tree, destiné à develop. La refonte WPF complète n'est pas encore livrée.**

## Objectif

Le lecteur de format retourne `ImportDocumentTree` contenant une identité de source, son empreinte SHA-256 et des nœuds ordonnés : section, paragraphe, liste, tableau, image, signet, bloc non convertible. Les nœuds ont des IDs de source déterministes **à l'intérieur d'une version donnée du document** (leur rapprochement entre versions différentes sera traité par la provenance #163).

Le modèle et l'interface `IImportDocumentReader` se trouvent dans `Nodalis.Core.Importing` ; le premier adaptateur réel est `DocxStructuredImportReader` dans Infrastructure. Il réutilise le parseur DOCX natif et reconstruit un arbre de sections, y compris les paragraphes d'introduction avant le premier titre, les niveaux imbriqués et les tableaux.

## Mapping sans effet de bord

`ImportDocumentPlanBuilder.Prepare` consomme un arbre et des `ImportSelectionRule` explicites. Par défaut, aucun fragment n'est importé. Une règle appliquée à une section propage sa destination aux descendants ; une exclusion interdit l'import d'un sous-arbre et ne produit pas de fichier. Le plan versionné décrit chaque fragment retenu, son ordre et sa cible Markdown. Il ne touche **jamais** aux documents ou projets existants ; la prévisualisation WPF et le commit transactionnel seront livrés dans #157–#165.

Les chemins de destination sont obligatoirement relatifs, terminés par `.md`, sans traversée `..`, lecteurs Windows, flux alternatifs NTFS, périphériques spéciaux ni segments non sûrs. Toute règle visant un ID source inconnu, ou des IDs en doublon, échoue avant une opération d'écriture.

## Tests

`StructuredImportSmokeTests` vérifie la hiérarchie Word, les exclusions, l'ordre, l'héritage du mapping, la stabilité de la sérialisation JSON, les chemins dangereux et l'utilisation **du même planificateur avec un lecteur fictif d'un format différent**. Aucun lecteur XLSX/VSDX n'est exposé ; aucun Office, IA ou package tiers n'est nécessaire.

## Étapes suivantes

- #156 : styles Word imbriqués, `outlineLvl`, styles personnalisés et diagnostics heuristiques.
- #157–#158 : sélection tri-state, arbre éditable et mapping WPF en deux panneaux.
- #159–#165 : profils, fidélité des tableaux/images, previews, manifestes, réimport et transactions.

Le plan contient une projection de blocs, **pas** une promesse de conversion Markdown fidèle ni un résultat de commit. L'import historique demeure fonctionnel tant que l'assistant 1.9 n'a pas passé ses tests.
