---
type: test
status: active
owner: Validation
tags: v1.9, qualification, recette
---
# Qualification v1.9 — Gate de sortie

**À ne pas confondre avec une publication :** cocher ces tâches après vérification factuelle uniquement. La branche `develop` reste la référence jusqu'au feu vert de la recette. La release 1.9 est interdite tant que des P0 bloquants ou le gate [#171](https://github.com/TristanStark/Nodalis/issues/171) restent ouverts.

**Mode d'emploi :** cocher une tâche uniquement si le scénario est **OK**. Si **KO** ou bloqué, laisser la case ouverte et noter sous le scénario le commit `develop`, les étapes, le résultat observé et le lien vers le ticket GitHub. Tous les scénarios commencent à **À faire** ; aucune validation n'est présumée. Les essais destructifs se font sur une **copie** du workspace de démo et des documents Word fictifs.

## Contrôles et compte rendu

- [ ] GATE-G01 — Vérifier la conformité du code | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Exécuter `dotnet run --project tools/Nodalis.CodeStyle/Nodalis.CodeStyle.csproj --configuration Release -- --check`.
  - **Résultat attendu :** Aucune infraction de style, notamment var et documentation XML.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] GATE-G02 — Compiler toute la solution en Release | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Exécuter `dotnet build Nodalis.sln --configuration Release`.
  - **Résultat attendu :** Build sans erreur au dernier SHA develop.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] GATE-G03 — Exécuter la suite de smoke tests | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Exécuter `dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release`.
  - **Résultat attendu :** Tests verts avec les fixtures DOCX et non-régressions.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] GATE-G04 — Vérifier la CI du HEAD develop exact | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Comparer le SHA local aux workflows Build et Code style + smoke et aux artefacts Windows.
  - **Résultat attendu :** Aucune validation issue d'un ancien SHA ; CI du HEAD verte.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] GATE-G05 — Vérifier statiquement le workspace de démo | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Depuis racine du dépôt, lancer `./examples/verify-demo-workspace.ps1` et ouvrir les quatre fiches de recette dans Nodalis.
  - **Résultat attendu :** GUID et fichiers cohérents ; tâches présentes dans la vue Tâches et dans le projet.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] GATE-G06 — Consolider les résultats des tests DOCX | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Reporter états OK/KO des scénarios DOCX-Q01 à Q16 avec SHA et captures anonymisées.
  - **Résultat attendu :** Aucun problème de perte de contenu, conflit, rollback ou P0 ouvert non traité.
  - **Issues :** [#154](https://github.com/TristanStark/Nodalis/issues/154), [#165](https://github.com/TristanStark/Nodalis/issues/165), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] GATE-G07 — Consolider les mesures de performance Windows | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Joindre résultats PERF-P01 à P14 : p95/p99, freezes, index, recherches, onglets et backups.
  - **Résultat attendu :** Pas de gel durable reproductible ; écarts et budgets documentés.
  - **Issues :** [#180](https://github.com/TristanStark/Nodalis/issues/180), [#190](https://github.com/TristanStark/Nodalis/issues/190)

## Décision de sortie

- [ ] GATE-G08 — Clôturer ou faire corriger les P0 après qualification | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Revoir #183/#184 et la sécurité/import du gate DOCX ; ne pas assimiler tests automatiques à qualification WPF.
  - **Résultat attendu :** Toutes les anomalies P0 corrigées et réellement validées avant clôture.
  - **Issues :** [#165](https://github.com/TristanStark/Nodalis/issues/165), [#171](https://github.com/TristanStark/Nodalis/issues/171), [#183](https://github.com/TristanStark/Nodalis/issues/183), [#184](https://github.com/TristanStark/Nodalis/issues/184)

- [ ] GATE-G09 — Décider explicitement des P1 v1.9 | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Trier #166 à #170 et #185 à #190 : qualifier ou reporter avec justification, selon la gravité et les régressions.
  - **Résultat attendu :** Aucune régression majeure ni freeze reproductible repoussé sans décision.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171), [#180](https://github.com/TristanStark/Nodalis/issues/180), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] GATE-G10 — Relire documentation et notes de version | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Vérifier la checklist, aide utilisateur, import/réimport, limitations et release notes.
  - **Résultat attendu :** Pas de promesse XLSX/VSDX, pas d'annonce non vérifiée ; plan de retour arrière documenté.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171), [#170](https://github.com/TristanStark/Nodalis/issues/170)

- [ ] GATE-G11 — Reporter la validation dans les issues GitHub | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Pour chaque KO : étapes, attendu/observé, SHA, OS/DPI, document fictif et lien vers capture anonymisée.
  - **Résultat attendu :** Issues traçables ; aucune donnée sensible du workspace professionnel partagée.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] GATE-G12 — Autoriser la publication seulement après feu vert | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Une fois tous les gates validés et les P1 arbitrés, acter décision de merge, tag v1.9.0 et release.
  - **Résultat attendu :** Publication seulement après approbation finale ; ne rien publier en cochant cette case seule.
  - **Issues :** [#171](https://github.com/TristanStark/Nodalis/issues/171)
