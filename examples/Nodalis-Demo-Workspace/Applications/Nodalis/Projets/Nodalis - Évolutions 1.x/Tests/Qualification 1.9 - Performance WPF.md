---
type: test
status: active
owner: Validation
tags: v1.9, qualification, recette
---
# Qualification v1.9 — Performance et stabilité WPF

Qualification interactive de l'EPIC [#180](https://github.com/TristanStark/Nodalis/issues/180), complémentaire aux benchmarks et tests automatiques. Noter la machine Windows, le SHA, le nombre de documents, la RAM, le DPI et les mesures p50/p95/p99/max si disponibles. Ne pas utiliser de workspace professionnel.

**Mode d'emploi :** cocher une tâche uniquement si le scénario est **OK**. Si **KO** ou bloqué, laisser la case ouverte et noter sous le scénario le commit `develop`, les étapes, le résultat observé et le lien vers le ticket GitHub. Tous les scénarios commencent à **À faire** ; aucune validation n'est présumée. Les essais destructifs se font sur une **copie** du workspace de démo et des documents Word fictifs.

## Réactivité UI et index

- [ ] PERF-P01 — Observer Diagnostics locaux et les freezes | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Activer les diagnostics, travailler puis consulter retards Dispatcher supérieurs à 200 ms.
  - **Résultat attendu :** Rapport p50/p95/p99/max exploitable, sans contenu sensible ni télémétrie réseau.
  - **Issues :** [#181](https://github.com/TristanStark/Nodalis/issues/181), [#184](https://github.com/TristanStark/Nodalis/issues/184), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] PERF-P02 — Ouvrir un workspace 500, 5 000 puis 10 000 documents | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Charger fixtures petites/grandes avec liens, tâches, jalons et archives ; mesurer ouverture à froid et à chaud.
  - **Résultat attendu :** Pas de gel prolongé, ni résolution perdue, ni dérive incontrôlée des scans.
  - **Issues :** [#182](https://github.com/TristanStark/Nodalis/issues/182), [#183](https://github.com/TristanStark/Nodalis/issues/183), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] PERF-P03 — Contrôler accueil Tâches et Jalons au démarrage | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Ouvrir directement l'accueil puis passer à un document et revenir ; consulter diagnostics d'index.
  - **Résultat attendu :** Snapshots cohérents ; pas de double rafraîchissement inutile si accueil masqué.
  - **Issues :** [#183](https://github.com/TristanStark/Nodalis/issues/183), [#185](https://github.com/TristanStark/Nodalis/issues/185)

- [ ] PERF-P04 — Autosave simple et rafale multi-onglets | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Éditer une note sans checklist puis sauvegarder rapidement plusieurs onglets.
  - **Résultat attendu :** Pas de triple scan global par sauvegarde ; aucune écriture d'index inutile ni perte de notes.
  - **Issues :** [#183](https://github.com/TristanStark/Nodalis/issues/183), [#185](https://github.com/TristanStark/Nodalis/issues/185)

- [ ] PERF-P05 — Actualiser tâches, jalons et liens ciblés | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Ajouter/retirer une checkbox, changer un jalon, ajouter un lien et modifier un fichier externe.
  - **Résultat attendu :** Vues et backlinks à jour sans valeurs anciennes ni JSON corrompu.
  - **Issues :** [#183](https://github.com/TristanStark/Nodalis/issues/183), [#185](https://github.com/TristanStark/Nodalis/issues/185)

- [ ] PERF-P06 — Basculer rapidement entre 30 onglets | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Saisir dans de longs Markdown, changer d'onglet, de preview et fermer pendant un rendu.
  - **Résultat attendu :** Interface interactive, rendu du bon onglet uniquement, pas d'erreur cross-thread.
  - **Issues :** [#184](https://github.com/TristanStark/Nodalis/issues/184), [#186](https://github.com/TristanStark/Nodalis/issues/186), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] PERF-P07 — Éditer Markdown 100 k à 1 M caractères | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Frappe continue sur document avec tableau, glossaire, Mermaid et composite ; écran partagé.
  - **Résultat attendu :** Aucun blocage durable ni preview périmée ; annotations et liens corrects.
  - **Issues :** [#184](https://github.com/TristanStark/Nodalis/issues/184), [#186](https://github.com/TristanStark/Nodalis/issues/186)

- [ ] PERF-P08 — Parcourir 1 000+ nœuds du worktree | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Filtrer, défiler entre applications/projets, renommer et archiver un projet, DPI 125/150 %.
  - **Résultat attendu :** Headers sticky, sélection et scroll stables ; aucun crop, saut ou gel.
  - **Issues :** [#187](https://github.com/TristanStark/Nodalis/issues/187), [#190](https://github.com/TristanStark/Nodalis/issues/190)

## Recherche, DOCX, backup et erreurs

- [ ] PERF-P09 — Enchaîner 20 recherches et annulations | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Recherche exacte/fuzzy puis annulation rapide avec fichiers modifiés entre deux requêtes.
  - **Résultat attendu :** Aucun crash, résultat obsolète ou recherche bloquante.
  - **Issues :** [#188](https://github.com/TristanStark/Nodalis/issues/188), [#184](https://github.com/TristanStark/Nodalis/issues/184)

- [ ] PERF-P10 — Ouvrir et actualiser les vues dynamiques | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Alterner vues, dashboard, projet et modification externe dans un workspace chargé.
  - **Résultat attendu :** Résultats cohérents sans rescans inutiles et navigation fluide.
  - **Issues :** [#188](https://github.com/TristanStark/Nodalis/issues/188), [#185](https://github.com/TristanStark/Nodalis/issues/185)

- [ ] PERF-P11 — Sélectionner rapidement dans un Word de 100+ pages | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Cocher/décocher sous-sections, changer de destination/titre, annuler puis réimporter.
  - **Résultat attendu :** Prévisualisation réactive ; dry-run, exclusions et fichiers identiques aux choix retenus.
  - **Issues :** [#189](https://github.com/TristanStark/Nodalis/issues/189), [#171](https://github.com/TristanStark/Nodalis/issues/171)

- [ ] PERF-P12 — Vérifier backup ZIP et fermeture pendant autosave | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Sauvegarder des modifications, lancer un backup puis fermer normalement sur une copie.
  - **Résultat attendu :** Archive lisible et exhaustive, aucune saisie perdue, fermeture bornée.
  - **Issues :** [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] PERF-P13 — Simuler index absent/corrompu, I/O et fermeture | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Sur copie, supprimer un index dérivé, bloquer un fichier, ouvrir/fermer pendant régénération.
  - **Résultat attendu :** Reconstruction sûre, message compréhensible, ni deadlock ni perte.
  - **Issues :** [#183](https://github.com/TristanStark/Nodalis/issues/183), [#184](https://github.com/TristanStark/Nodalis/issues/184), [#190](https://github.com/TristanStark/Nodalis/issues/190)

- [ ] PERF-P14 — Comparer mesures et vérifier Windows/DPI | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v1.9, recette
  - **Manipulation :** Répéter scénarios clés sur Win10/11 si disponibles, DPI 100/125/150 %, thème sombre et clavier.
  - **Résultat attendu :** Reporter chiffres avant/après, anomalies et limites matérielles dans les issues.
  - **Issues :** [#184](https://github.com/TristanStark/Nodalis/issues/184), [#190](https://github.com/TristanStark/Nodalis/issues/190)
