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
