# Documentation Nodalis 1.0

Ce dossier constitue la documentation versionnée avec le code pour Nodalis 1.0.
Le point d'entrée utilisateur est README-UTILISATEUR.md. Le présent document
sert de sommaire et de matrice de couverture des fonctionnalités majeures.

## Parcours recommandé

1. Commencer par README-UTILISATEUR.md pour installer, créer ou ouvrir un workspace,
   utiliser l'éditeur et comprendre les fonctions principales.
2. Consulter import-export.md pour les imports DOCX / Markdown et les exports de projet.
3. Consulter backups.md pour les sauvegardes et restaurations.
4. Consulter troubleshooting.md pour le diagnostic et la FAQ.
5. Utiliser les documents de format et compatibilité pour les opérations sensibles
   ou l'intégration avec d'autres outils.

## Matrice fonctionnelle 1.0

| Fonction | Documentation de référence |
| --- | --- |
| Installation portable et premier démarrage | README-UTILISATEUR.md |
| Workspace unique et structure Applications / Modules / Projets | README-UTILISATEUR.md, workspace-format.md |
| Profils de projet et sections personnalisables | project-profiles.md |
| Notes Markdown, front matter et templates | README-UTILISATEUR.md, workspace-format.md |
| Flowcharts Markdown | markdown-flowcharts.md |
| Notes rapides Global / Application / Projet | README-UTILISATEUR.md |
| Recherche exacte / fuzzy et filtres | README-UTILISATEUR.md |
| Liens internes, backlinks et relations typées | README-UTILISATEUR.md, workspace-format.md, data-contracts-v1.md |
| Tâches consolidées et Kanban | README-UTILISATEUR.md, data-contracts-v1.md |
| Jalons et calendrier | README-UTILISATEUR.md, data-contracts-v1.md |
| Réunions et décisions | README-UTILISATEUR.md, data-contracts-v1.md |
| Import DOCX | import-export.md |
| Import Markdown en masse | import-export.md |
| Export projet Markdown / HTML / DOCX | import-export.md |
| Pièces jointes et sources importées | import-export.md, workspace-format.md |
| Corbeille et récupération | README-UTILISATEUR.md, workspace-format.md |
| Sauvegardes et restauration | backups.md |
| Mise à jour, rollback et reprise après interruption | update-recovery.md |
| Migrations de workspace | workspace-migrations.md |
| Politique de compatibilité et lecture seule | workspace-compatibility.md |
| Contrats de données 1.0 | data-contracts-v1.md |
| Crashs, diagnostics et récupération locale | crash-diagnostics.md |
| Accessibilité, clavier et cohérence UI | accessibility.md, README-UTILISATEUR.md |
| Menus contextuels, dark mode et DPI | context-menu-smoke-test.md, accessibility.md |
| Budgets de performance | performance-budgets.md |
| Régression et fixtures CI | regression-testing.md |
| Release Candidate et passage en stable | release-checklist.md |
| Dépannage et FAQ | troubleshooting.md |
| Architecture du code | architecture.md |

## Contrats de données

workspace-format.md décrit l'arborescence et les fichiers visibles.
data-contracts-v1.md définit l'autorité des données, les identités et les invariants.
workspace-migrations.md décrit la migration d'un ancien schéma.
workspace-compatibility.md décrit ce qu'une version donnée peut lire ou écrire.

Ces documents font foi ensemble pour le format 1.0.

## Documentation de support

Pour un problème utilisateur, commencer par troubleshooting.md. Les cas de
crash, de récupération d'update et de restauration de backup possèdent ensuite
des guides dédiés plus détaillés.

Toute nouvelle fonctionnalité majeure destinée à 1.0 doit être ajoutée à cette
matrice ou disposer d'un document dédié avant la release stable.

## Extensions après 1.0 (en cours)

- [Inbox 1.1 — format, sécurité et récupération](inbox-persistence-v1.md) :
  contrat de stockage des captures, indépendant des notes rapides historiques.

- [Inbox 1.1 — raccourci Windows global configurable](global-inbox-hotkey.md) :
  fonctionnement, raccourcis réservés, collisions et vérification interactive.

- [Inbox 1.1 — capture explicite du presse-papiers et des fichiers](inbox-capture.md) :
  formats pris en charge, limites, sélection et absence de capture silencieuse.

- [Inbox 1.1 — vue de tri](inbox-triage.md) :
  recherche, filtres, aperçu, actions par lot, corbeille et compteur de dashboard.

- [Refonte UX de l'explorateur (worktree) — #122](worktree-navigation.md) :
  hiérarchie aplatie visuellement, cartes projets, sélection et contrôles de validation Windows.

## Nodalis 1.2 — maintenance avancée (en cours)

- [Contrat Markdown des incidents et interventions](maintenance-markdown-1.2.md) :
  identité stable, lecture ancienne/nouvelle, liens, diagnostics et ajout non destructif.
- [Saisie et modification des interventions](maintenance-interventions-1.2-guide.md) :
  formulaire compact, liens vers incidents, Command Palette, sauvegarde et reprise des conflits.
- [Historique chronologique et recherche](maintenance-history-1.2-guide.md) :
  contextes projet/application, dates, filtres, sources Markdown et fiches partielles.
- [Suggestions d'incidents récurrents](maintenance-recurrence-1.2-guide.md) :
  rapprochements explicables sans IA, comparaison, décisions lier/ignorer et prévention des faux positifs.
- [Inbox vers fiche de maintenance](maintenance-inbox-1.2-guide.md) :
  incident ou intervention structurés, pièces jointes, provenance, reprise sans doublon et projet Ultra-simple.
- [Relations de maintenance](maintenance-relations-1.2-guide.md) :
  liens typés GUID, références cassées, renommage/restauration et contrat pour l'analyse d'impact.
- [Notes de version 1.2](release-notes-1.2.md) :
  nouveautés, compatibilité 1.0/1.1, récupération et limites avant publication.
- [Checklist de qualification 1.2](release-checklist-1.2.md) :
  fixtures CI, Windows 10/11, dark mode, DPI, clavier, sauvegardes et gate stable.

## Nodalis 1.1 — documentation de sortie

- [Guide Inbox 1.1](inbox-1.1-guide.md) : capturer, trier, convertir, récupérer.
- [Notes de version 1.1](release-notes-1.1.md) : nouveautés, compatibilité et limites.
- [Checklist de qualification 1.1](release-checklist-1.1.md) : Small/Medium/Large, CI, Windows 10/11, DPI et gate stable.
- [Conversion Markdown native](inbox-conversion.md) : formats, idempotence et provenance.
- [Sécurité des captures](inbox-safety.md) : doublons, erreurs, pièces jointes, staging.

## Analyse d'impact 1.3 / 1.4 — en développement sur develop

- [Guide utilisateur : analyser les impacts, graphe, maintenance, filtres](impact-user-guide-1.4.md).
- [Sémantique et sens des relations](impact-semantics-1.3.md).
- [Qualification v1.4](release-checklist-1.4.md).

La documentation v1.4 est publiée dans le dépôt mais la version stable annoncée reste v1.2.0 jusqu'au tag officiel.

## Nodalis 1.4 publiée

- [Nodalis 1.4.0 — notes de version](release-notes-1.4.md)
- [Guide complet d'analyse d'impact](impact-user-guide-1.4.md)

## Nodalis 1.5 — Vues dynamiques

- [Notes de version 1.5](release-notes-1.5.md)
- [Guide utilisateur des vues](views-user-guide-1.5.md)
- [Projection et requêtes des vues dynamiques](views-core-1.5.md)
- [Définitions sauvegardées JSON privées et portables](views-storage-1.5.md)
- [Qualification et restauration 1.5](release-checklist-1.5.md)

## Nodalis 1.6 — Documents composites

- [Nouveautés et notes de version](release-notes-1.6.md)

- [Guide des inclusions, résolution, preview et exports](composite-documents-1.6.md)
- [Checklist de qualification 1.6](release-checklist-1.6.md)

EPIC [Documents composites #126](https://github.com/TristanStark/Nodalis/issues/126). Source Markdown conservée : pas de nouveau format de workspace obligatoire, ni de relations causales ajoutées à l'analyse d'impact.

## Nodalis 1.7 — Recherche sémantique locale (develop)

- [Guide utilisateur : activer, indexer, rechercher, supprimer](semantic-1.7-guide.md)
- [Fournisseur local et contrat d'embeddings](semantic-1.7-provider.md)
- [Checklist de qualification et confidentialité](release-checklist-1.7.md)
