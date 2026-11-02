---
type: test
status: active
owner: Validation
tags: v2.0, worktree, wpf, migration, performance
---
# Qualification 2.0 — Worktree, Quick Open et migration

Cette fiche contient uniquement des scénarios sur des **données fictives**.
Ne pas copier de noms de projets, chemins personnels, captures ou documents
professionnels dans le dépôt, les tickets GitHub ou les commits.

**Mode d'emploi :** cocher `[x]` seulement après essai visuel **OK** sur
le commit `develop` indiqué. En cas de KO, laisser `[ ]`, noter les
étapes reproductibles et un diagnostic **anonymisé** dans l'issue.
Tester les opérations de migration et le rollback sur une **copie** du
workspace de démo, jamais sur le workspace de travail original.

## Réactivité WPF et navigation

- [ ] QUAL2-W01 — Navigation entre projets de plusieurs applications | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, worktree
  - **Manipulation :** utiliser une fixture de 1 000+ nœuds, défiler en continu entre applications et projets, développer/replier des dossiers ; tester un touchpad si disponible.
  - **Résultat attendu :** en-têtes collants cohérents avec le projet sélectionné, scroll stable, aucune sélection fantôme ni déplacement spontané.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

- [ ] QUAL2-W02 — Filtre, position et réapparition | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, worktree
  - **Manipulation :** filtrer un document profond, activer le mode application courante, changer d'onglet/application, retirer le filtre et révéler un document caché.
  - **Résultat attendu :** identités et indices visibles corrects, sections et sous-projets conservés, expansion initiale restaurée, aucun saut de scroll non demandé.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

- [ ] QUAL2-W03 — Rafale de 30 onglets et fermeture à droite | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, wpf
  - **Manipulation :** ouvrir 30 documents, changer rapidement d'onglet puis « Fermer ceux à droite », revenir au Worktree et recommencer.
  - **Résultat attendu :** réponse interactive, aucune perte de document non sauvegardé, aucun gel durable ni recentrage automatique du Worktree.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

- [ ] QUAL2-W04 — Favoris, récents et signets | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, quickopen
  - **Manipulation :** ajouter/retirer un favori, ouvrir plusieurs documents, rouvrir un document récent, déplacer un titre de signet et rafraîchir l'index des liens.
  - **Résultat attendu :** listes courantes et identifiants exacts, signet cassé détecté, pas de données figées du cache ou ouverture de mauvais document.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

- [ ] QUAL2-W05 — Mesures locales des opérations Worktree | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, diagnostics
  - **Manipulation :** activer Diagnostics locaux, répéter W01 à W04 et relever p50/p95/p99 du Dispatcher et des opérations « Worktree · en-têtes persistants », « projection du filtre », « favoris et récents ».
  - **Résultat attendu :** opérations mesurées sans nom ni chemin de document dans les durées ; comparer avec et sans débogueur, noter les retards > 200 ms sans leur attribuer automatiquement une cause.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

- [ ] QUAL2-W07 — Rafales d'onglets avec glossaire et Markdown volumineux | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, wpf, glossaire
  - **Manipulation :** préparer trois documents fictifs de grande taille avec des termes de glossaire, les sélectionner rapidement et modifier le texte du dernier avant la fin des analyses.
  - **Résultat attendu :** parsing et recherche de glossaire hors Dispatcher, une prévisualisation uniquement pour le dernier onglet stabilisé, aucune annotation/preview/prise de focus provenant d'un onglet remplacé, saisie préservée.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

- [ ] QUAL2-W06 — Sortie de veille et DPI | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, wpf
  - **Manipulation :** mettre Windows en veille avec plusieurs onglets ouverts, reprendre et manipuler Worktree, filtre et panneaux ; recommencer en DPI 100 %, 125 % puis 150 %.
  - **Résultat attendu :** aucun gel prolongé, pas de rafales persistantes d'erreurs de binding TreeViewItem.VerticalContentAlignment, aucun chevauchement ou déplacement non demandé.
  - **Issue :** [#205](https://github.com/TristanStark/Nodalis/issues/205)

## Migration Complexe (copie uniquement)

- [ ] QUAL2-M01 — Aucun blocage provenant d'une application indépendante | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, migration
  - **Manipulation :** créer deux applications fictives A/B ; migrer un projet ancien Complexe de A tandis que B possède des références Markdown cassées et des sources non UTF-8 sans lien.
  - **Résultat attendu :** aucune référence de B sans rapport ne bloque la migration de A ; tout backlink réellement dirigé vers un fichier déplacé reste détecté et qualifié.
  - **Issue :** [#194](https://github.com/TristanStark/Nodalis/issues/194)

- [ ] QUAL2-M02 — Annulation et progression de l'inventaire | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, migration
  - **Manipulation :** lancer le préflight sur un grand workspace fictif, annuler (bouton, Échap, fermeture), puis relancer.
  - **Résultat attendu :** Dispatcher réactif, compteurs anonymes et cohérents, aucune écriture ni plan partiellement approuvable après annulation.
  - **Issue :** [#194](https://github.com/TristanStark/Nodalis/issues/194)

- [ ] QUAL2-M03 — Sauvegarde, liens et rollback transactionnel | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, migration
  - **Manipulation :** sur copie, migrer avec vrais backlinks inter-projets et documents internes, vérifier les chemins, puis restaurer depuis le journal ; recommencer avec une référence ambiguë.
  - **Résultat attendu :** octets et liens restaurés à l'identique, aucune opération implicite et refus prudent des liens ambigus.
  - **Issue :** [#194](https://github.com/TristanStark/Nodalis/issues/194)

- [ ] QUAL2-M04 — Documents externes volumineux et encodage incorrect | Responsable: Validation | Priorité: Haute | Statut: À faire | Tags: v2.0, migration
  - **Manipulation :** ajouter des dizaines de Markdown fictifs sans lien, un document non UTF-8 volumineux et un backlink concerné traversant la frontière d'un bloc de lecture.
  - **Résultat attendu :** analyse bornée et annulable, aucune source pertinente oubliée, aucun blocage inventé ; mesurer mémoire et temps avant/après sur le même scénario.
  - **Issues :** [#194](https://github.com/TristanStark/Nodalis/issues/194), [#205](https://github.com/TristanStark/Nodalis/issues/205)

**Gate de qualification :** conserver tous les scénarios ouverts tant qu'ils ne
sont pas effectivement vérifiés sur Windows. Les tests CI ne remplacent pas
cette qualification et ne déclenchent aucune release 2.0 automatique.
