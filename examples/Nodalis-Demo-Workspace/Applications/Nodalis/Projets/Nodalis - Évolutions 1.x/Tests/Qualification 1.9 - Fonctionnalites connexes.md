---
type: test
status: active
owner: Validation
tags: v1.9, qualification, recette
---
# Qualification v1.9 — Fonctions connexes et arbitrages P1

Les éléments P1 encore ouverts sur GitHub ne sont **pas présumés livrés**. Pour eux, vérifier d'abord l'existence de la fonctionnalité sur le `develop` effectivement compilé : si absente, documenter le report dans l'issue plutôt que cocher une validation fonctionnelle fictive.

**Mode d'emploi :** cocher une tâche uniquement si le scénario est **OK**. Si **KO** ou bloqué, laisser la case ouverte et noter sous le scénario le commit `develop`, les étapes, le résultat observé et le lien vers le ticket GitHub. Tous les scénarios commencent à **À faire** ; aucune validation n'est présumée. Les essais destructifs se font sur une **copie** du workspace de démo et des documents Word fictifs.

## Régressions fonctionnelles

- [ ] FONC-F01 — Coller des jalons JJ/MM/AAAA sans ID | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Coller dans Jalons.md un tableau court ou sept colonnes en date française sans ID ; éditer, déplacer et supprimer.
  - **Résultat attendu :** Détection sans ID, identité stable et calendrier correct ; ISO AAAA-MM-JJ toujours pris en charge.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] FONC-F02 — Vérifier le health check des documents système | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Lancer le health check sur le workspace démo contenant Glossaire, Jalons et tâches.
  - **Résultat attendu :** Aucune fausse alerte « orphan document » pour ces fichiers accessibles par la palette.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] FONC-F03 — Créer un incident et lui associer une intervention | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Depuis Fiche de Maintenance : Nouvel incident, annuler, saisir champs requis, réouvrir et lier intervention.
  - **Résultat attendu :** INC stable, écriture Markdown sûre, champs invalides refusés et aucun doublon.
  - **Issues :** [#178](https://github.com/TristanStark/Nodalis/issues/178)

- [ ] FONC-F04 — Clore un projet et le réouvrir depuis Archives | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Sur copie d'un projet de démo : clôturer, consulter Archives, réouvrir, tester collision et projet Ultra-simple.
  - **Résultat attendu :** Identité, fichiers, liens et sous-projets conservés ; pas d'écrasement.
  - **Issues :** [#179](https://github.com/TristanStark/Nodalis/issues/179)

- [ ] FONC-F05 — Bloquer archivage pendant récupération DOCX | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Sur données jetables, tenter de clore un projet avec transaction d'import en attente puis de réimporter dans Archives.
  - **Résultat attendu :** Refus protecteur explicite ; projet récupérable, données intactes.
  - **Issues :** [#165](https://github.com/TristanStark/Nodalis/issues/165), [#179](https://github.com/TristanStark/Nodalis/issues/179)

## P1 DOCX — qualifier si présent ou documenter le report

- [ ] FONC-F06 — Arbitrer conversion Word en tâches et jalons | Responsable: Validation | Priorité: Normale | Statut: À faire | Tags: v1.9, p1, docx
  - **Manipulation :** Vérifier si les règles structurées existent ; si oui, convertir tableau de recette et actions.
  - **Résultat attendu :** Tâches/jalons dans vues natives, règles explicites ; sinon report motivé.
  - **Issues :** [#166](https://github.com/TristanStark/Nodalis/issues/166)

- [ ] FONC-F07 — Arbitrer signets et liens Word éclatés | Responsable: Validation | Priorité: Normale | Statut: À faire | Tags: v1.9, p1, docx
  - **Manipulation :** Vérifier la conversion des signets, REF et cibles exclues.
  - **Résultat attendu :** Liens internes sûrs ou limites clairement documentées ; report si non implémenté.
  - **Issues :** [#167](https://github.com/TristanStark/Nodalis/issues/167)

- [ ] FONC-F08 — Arbitrer un Word vers plusieurs projets | Responsable: Validation | Priorité: Normale | Statut: À faire | Tags: v1.9, p1, docx
  - **Manipulation :** Vérifier option multi-projets/sous-projets, collision et profil Ultra-simple.
  - **Résultat attendu :** Transaction globale cohérente si disponible ; sinon report explicite.
  - **Issues :** [#168](https://github.com/TristanStark/Nodalis/issues/168)

- [ ] FONC-F09 — Arbitrer commentaires, notes et révisions Word | Responsable: Validation | Priorité: Normale | Statut: À faire | Tags: v1.9, p1, docx
  - **Manipulation :** Inspecter options de traitement pour notes, sommaire et suivi des changements.
  - **Résultat attendu :** Aucune perte silencieuse si livré ; sinon limites et report consignés.
  - **Issues :** [#169](https://github.com/TristanStark/Nodalis/issues/169)

- [ ] FONC-F10 — Arbitrer socle multi-format sans lecteurs XLSX/VSDX | Responsable: Validation | Priorité: Normale | Statut: À faire | Tags: v1.9, p1, architecture
  - **Manipulation :** Vérifier contrats et tests simulés ; contrôler qu'aucune option XLSX/VSDX réelle n'est annoncée.
  - **Résultat attendu :** Socle documenté ou report accepté ; aucun import fictif exposé dans v1.9.
  - **Issues :** [#170](https://github.com/TristanStark/Nodalis/issues/170), [#172](https://github.com/TristanStark/Nodalis/issues/172), [#173](https://github.com/TristanStark/Nodalis/issues/173)
