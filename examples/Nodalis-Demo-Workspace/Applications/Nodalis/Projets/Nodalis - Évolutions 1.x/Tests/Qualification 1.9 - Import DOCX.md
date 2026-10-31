---
type: test
status: active
owner: Validation
tags: v1.9, qualification, recette
---
# Qualification v1.9 — Import et réimport DOCX

Sources : [fiche de recette DOCX](https://github.com/TristanStark/Nodalis/blob/develop/docs/docx-1.9-manual-qualification.md) et [gate v1.9](https://github.com/TristanStark/Nodalis/blob/develop/docs/release-checklist-1.9.md). Préparer un DOCX avec titres Développement, Tests, Recette, À ignorer, sous-titres, listes, tableau fusionné, image et lien ; un second DOCX modifié ; un Word de 100+ pages.

**Mode d'emploi :** cocher une tâche uniquement si le scénario est **OK**. Si **KO** ou bloqué, laisser la case ouverte et noter sous le scénario le commit `develop`, les étapes, le résultat observé et le lien vers le ticket GitHub. Tous les scénarios commencent à **À faire** ; aucune validation n'est présumée. Les essais destructifs se font sur une **copie** du workspace de démo et des documents Word fictifs.

## Structure, sélection et mapping

- [ ] DOCX-Q01 — Créer un projet depuis Word avec section personnalisée | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Importer vers un nouveau projet, mapper un titre vers une section DOCX personnalisée.
  - **Résultat attendu :** Projet, section et Markdown créés dans les chemins annoncés, sans donnée perdue.
  - **Issues :** [#155](https://github.com/TristanStark/Nodalis/issues/155), [#156](https://github.com/TristanStark/Nodalis/issues/156), [#158](https://github.com/TristanStark/Nodalis/issues/158), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] DOCX-Q02 — Répartir Développement, Tests et Recette | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Associer chaque titre Word à Technique, Tests et Recette puis valider le plan.
  - **Résultat attendu :** Contenu de chaque branche dans la bonne cible, sans fichiers inattendus.
  - **Issues :** [#156](https://github.com/TristanStark/Nodalis/issues/156), [#158](https://github.com/TristanStark/Nodalis/issues/158), [#162](https://github.com/TristanStark/Nodalis/issues/162), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] DOCX-Q03 — Exclure une branche et un bloc | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Ignorer le titre À ignorer, sa descendance et un paragraphe/image d'une autre section.
  - **Résultat attendu :** Aucun contenu ni média exclu copié ; contenu conservé dans l'ordre.
  - **Issues :** [#157](https://github.com/TristanStark/Nodalis/issues/157), [#161](https://github.com/TristanStark/Nodalis/issues/161), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] DOCX-Q04 — Corriger la hiérarchie et sélectionner finement | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Tester titres personnalisés/mal structurés, changer niveau, sélectionner paragraphe, liste, tableau et image.
  - **Résultat attendu :** Arbre, sélection tri-state et aperçu fidèles, sans perte ni doublon.
  - **Issues :** [#156](https://github.com/TristanStark/Nodalis/issues/156), [#157](https://github.com/TristanStark/Nodalis/issues/157)

- [ ] DOCX-Q05 — Vérifier mapping clavier et glisser-déposer | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Affecter des sections à différents fichiers avec souris puis clavier ; appliquer une règle de mapping.
  - **Résultat attendu :** Même plan exact dans les deux parcours, choix stable et focus lisible.
  - **Issues :** [#158](https://github.com/TristanStark/Nodalis/issues/158), [#159](https://github.com/TristanStark/Nodalis/issues/159), [#162](https://github.com/TristanStark/Nodalis/issues/162)

- [ ] DOCX-Q06 — Fusionner plusieurs sections vers un même fichier | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Mapper deux branches vers une cible commune ; consulter le dry-run et importer.
  - **Résultat attendu :** Ordre annoncé respecté, aucune duplication ni collision cachée.
  - **Issues :** [#158](https://github.com/TristanStark/Nodalis/issues/158), [#162](https://github.com/TristanStark/Nodalis/issues/162)

- [ ] DOCX-Q07 — Comparer aperçu Word et Markdown avant validation | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Changer sélection, destination et règle ; observer comparateur, fichiers et avertissements.
  - **Résultat attendu :** Aperçu et plan se recalculent ; aucun commit sur un aperçu périmé.
  - **Issues :** [#157](https://github.com/TristanStark/Nodalis/issues/157), [#162](https://github.com/TristanStark/Nodalis/issues/162), [#189](https://github.com/TristanStark/Nodalis/issues/189)

## Fidélité, références et protections

- [ ] DOCX-Q08 — Conserver tableaux fusionnés et images intégrées | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Importer cellules vides/fusions horizontales et verticales, légende, image locale et lien distant.
  - **Résultat attendu :** Structure lisible ou avertissement explicite ; aucun téléchargement distant.
  - **Issues :** [#160](https://github.com/TristanStark/Nodalis/issues/160), [#161](https://github.com/TristanStark/Nodalis/issues/161)

- [ ] DOCX-Q09 — Contrôler signets, références et sections exclues | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Importer des références Word entre sections réparties ; exclure ensuite une cible.
  - **Résultat attendu :** Lien utilisable si cible importée ; diagnostic si exclue ; aucune mauvaise destination.
  - **Issues :** [#167](https://github.com/TristanStark/Nodalis/issues/167)

- [ ] DOCX-Q10 — Annuler, tester Word corrompu et droits insuffisants | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Annuler avant import ; réessayer avec copie ZIP/XML abîmée, source verrouillée ou dossier non inscriptible.
  - **Résultat attendu :** Source et notes intactes ; erreur claire et aucun résultat partiel caché.
  - **Issues :** [#162](https://github.com/TristanStark/Nodalis/issues/162), [#165](https://github.com/TristanStark/Nodalis/issues/165), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] DOCX-Q11 — Tester conflits de création après approbation | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Sur copie, créer une collision du Markdown ou de la copie DOCX après confirmation du plan.
  - **Résultat attendu :** Refus sans écraser le tiers, sans renommer arbitrairement ; rollback sûr.
  - **Issues :** [#165](https://github.com/TristanStark/Nodalis/issues/165), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] DOCX-Q12 — Inspecter récupération après arrêt forcé | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Sur workspace jetable, examiner l'UI et les temporaires après échec/reprise aux checkpoints documentés.
  - **Résultat attendu :** Récupération explicite, idempotente ; source, notes et médias conservés.
  - **Issues :** [#165](https://github.com/TristanStark/Nodalis/issues/165), [#171](https://github.com/TristanStark/Nodalis/issues/171)

## Réimport et provenance

- [ ] DOCX-Q13 — Réimporter Word inchangé avec édition locale | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Modifier seulement la note Nodalis avant de réimporter le même DOCX.
  - **Résultat attendu :** Aucune perte de l'édition locale, base et provenance cohérentes.
  - **Issues :** [#163](https://github.com/TristanStark/Nodalis/issues/163), [#164](https://github.com/TristanStark/Nodalis/issues/164)

- [ ] DOCX-Q14 — Résoudre conflit à trois voies | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Modifier Word et Markdown sur le même bloc ; comparer et choisir chaque résolution.
  - **Résultat attendu :** Conflit affiché, choix préservé, aucun écrasement silencieux.
  - **Issues :** [#164](https://github.com/TristanStark/Nodalis/issues/164), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] DOCX-Q15 — Tester déplacement, renommage, suppression et historique | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Déplacer/renommer une section Word, en supprimer une et sélectionner l'historique du premier import.
  - **Résultat attendu :** Correspondance/provenance explicites ; aucune association arbitraire ni suppression non validée.
  - **Issues :** [#163](https://github.com/TristanStark/Nodalis/issues/163), [#164](https://github.com/TristanStark/Nodalis/issues/164)

- [ ] DOCX-Q16 — Confirmer transaction et fichiers réellement produits | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Après import/réimport, comparer plan, Markdown, médias, copie source et manifeste ; recommencer après erreur.
  - **Résultat attendu :** Fichiers conformes au dry-run et cohérents après reprise ; Word original inchangé.
  - **Issues :** [#162](https://github.com/TristanStark/Nodalis/issues/162), [#163](https://github.com/TristanStark/Nodalis/issues/163), [#165](https://github.com/TristanStark/Nodalis/issues/165), [#171](https://github.com/TristanStark/Nodalis/issues/171)
