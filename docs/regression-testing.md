# Suite de régression Nodalis

La suite de régression 1.0 reste volontairement sans framework de test ni package tiers.
Elle s'exécute avec le projet tests/Nodalis.SmokeTests et échoue au premier invariant violé.

## Exécution

Depuis la racine du dépôt :

    dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release

Le workflow Build exécute cette commande sur Windows pour chaque push vers main ou develop.
Le workflow de release réexécute la même suite avant de produire l'archive portable.

## Fixtures de workspace

Le fichier tests/Nodalis.SmokeTests/Fixtures/workspace-fixtures.json décrit trois profils
versionnés et non confidentiels :

| Fixture | Documents générés | Corps par document | But |
| --- | ---: | ---: | --- |
| Small | 8 | 256 caractères | régression fonctionnelle rapide |
| Medium | 64 | 1 024 caractères | navigation, recherche et liens sur un workspace réaliste |
| Large | 256 | 4 096 caractères | volumétrie représentative avant 1.0 |

WorkspaceRegressionFixtureSmokeTests matérialise chaque fixture dans un dossier temporaire,
puis vérifie la persistence du manifeste, la navigation, la recherche, l'index de liens,
la prise en compte d'une modification de fichier et la disparition d'un document supprimé.
Les données sont générées de façon déterministe et ne contiennent aucune donnée utilisateur.

## Matrice de couverture 1.0

| Domaine | Couverture de régression |
| --- | --- |
| Fixtures Small / Medium / Large | WorkspaceRegressionFixtureSmokeTests |
| Opérations filesystem | création de projets, corbeille, sauvegardes, TextDocumentSession, autosave, fixtures |
| Migrations | WorkspaceMigrationSmokeTests |
| Compatibilité / lecture seule | WorkspaceCompatibilitySmokeTests |
| Liens / backlinks / relations | VerifyLinksAndBacklinksAsync et fixtures |
| Recherche | VerifySearchAsync et fixtures |
| Import DOCX | VerifyDocxImportAsync, VerifyDocxImportAnalysisAsync, DocxFineSelectionSmokeTests |
| Import Markdown en masse | MarkdownBulkImportSmokeTests |
| Export | ProjectExportSmokeTests |
| Tâches | VerifyTasksAsync et WorkspaceKanbanSmokeTests |
| Jalons | VerifyMilestonesAsync et WorkspaceCalendarSmokeTests |
| Décisions | VerifyDecisionsAsync |
| Updater / rollback | VerifyLocalReleasePackageAsync et UpdaterRecoverySmokeTests |
| Crash / récupération | CrashDiagnosticsSmokeTests et WorkspaceIntegritySmokeTests |
| Gros workspaces | WorkspacePerformanceSmokeTests et fixture Large |
| Thèmes / dialogs critiques | scripts/verify-accessibility.ps1 dans Build et Portable Windows Release |
| Binaire publié | démarrage de Nodalis.exe avec --smoke-test dans les workflows Build et Release |

## Règles

- Aucun package de test tiers.
- Aucun accès réseau nécessaire.
- Toutes les fixtures sont temporaires et supprimées après exécution.
- Toute nouvelle fonctionnalité 1.0 doit soit ajouter un invariant à la suite existante,
  soit être explicitement reliée à une vérification dans cette matrice.

## Contrôle de non-régression Inbox 1.1 (#105)

La suite historique Small / Medium / Large (8, 64, 256 documents) appelle
désormais `InboxReleaseQualificationSmokeTests` sur **le même workspace**,
après avoir vérifié la navigation, la recherche et les liens 1.0.

Chaque fixture valide : création différée Inbox, avertissement de
doublon, aperçu sans écriture, conversion Markdown native idempotente,
tâche dans le service ordinaire, archivage et corbeille/restauration,
absence de modification du manifeste ou des trois Notes rapides,
sauvegarde ZIP et restauration comprenant la capture et le fichier
converti, enfin navigation sur workspace restauré. Small teste en
outre la fiche de maintenance d'un projet ultra-simple réel.

La matrice des systèmes clients, des modes clavier, de dark mode et
de DPI se trouve dans [release-checklist-1.1.md](release-checklist-1.1.md).
Une CI Windows Server hébergée ne vaut pas validation Windows 10/11.
