# Nodalis 2.0 — Contrat Parcours projet (EPIC #195)

## État de l'incrément

Le socle Core et la persistance Infrastructure sont disponibles sur `develop`.
**Ce lot ne crée pas encore l'onglet WPF ni les commandes de palette** : il
stabilise d'abord un format documenté, portable et testé. Aucun parcours
n'est matérialisé à la simple ouverture, à l'indexation ou à la création
d'un projet 1.9/2.0.

## Sources canoniques

- Modèles intégrés en mémoire : `ProjectJourneyDefaults.CreateCatalog()`.
- Surcharge explicite propre au workspace :
  `Templates/project-journeys.json` (schéma `schemaVersion: 1`).
- Parcours réellement approuvé d'un projet :
  `<projet>/.project-journey.json` (schéma `schemaVersion: 1`).

Le JSON est lisible et transportable, sans base externe, IA ni connexion réseau.
La surcharge workspace remplace le catalogue intégré lorsque ce fichier existe :
aucune migration implicite ne modifie le fichier d'un utilisateur.

Chaque parcours porte le `projectId` de `.project.json` et la clé du
modèle choisi à l'origine. Ce modèle sert de **provenance** : modifier le
catalogue ne réécrit pas les étapes d'un projet existant.

## Profils initiaux

| Complexité | Étapes proposées | Comportement |
| --- | --- | --- |
| Ultra-simple | Aucune par défaut | Aucune création implicite ; possibilité d'opt-in sur un modèle personnalisé |
| Simple | Cadrage, Réalisation, Clôture | Proposition en lecture seule |
| Moyen | Cadrage, Conception, Réalisation, Tests, Clôture | Proposition en lecture seule |
| Complexe | Cadrage, Exigences, Risques, Conception, Préparation développement, Réalisation et changements, Tests, Mise en production, Clôture | Proposition en lecture seule, exactement 9 étapes |

Chaque étape contient des définitions d'opérations non exécutables
et peut ensuite prendre un état distinct des tâches :
`todo`, `inProgress`, `completed`, `notApplicable`.

Les opérations du projet ont des GUID indépendants, une clé de modèle
optionnelle, un libellé, une future clé de prompt et des références vers
des sources du workspace, jalons et contrôles éventuels. Ces références
ne dupliquent pas les objets métiers et ne créent pas de preuve implicite.
La complétion d'une étape n'arrête pas la mise à jour des documents
qu'elle référence.

## Contrats de service

`ProjectJourneyService` expose :

- `LoadTemplatesAsync` : lecture des modèles, sans écriture.
- `SaveTemplatesAsync` : enregistrement explicite et conditionnel d'une
  surcharge workspace, avec SHA-256 requis pour une mise à jour.
- `PreviewDefaultAsync` : modèle proposé par la complexité initiale, ou
  `null` pour Ultra-simple ; aucune écriture.
- `ReadAsync` : lecture d'un parcours existant et de son empreinte exacte,
  ou `null` si aucun parcours n'a été approuvé.
- `InitializeAsync` : matérialisation **explicite** avec GUID frais,
  `CreateNew` et absence d'écrasement d'une création concurrente.
- `SaveAsync` : remplacement conditionnel sur SHA-256 sous un handle
  protégé, puis `File.Replace` ; un éditeur externe ne peut pas
  être écrasé silencieusement par un état obsolète.

Les fichiers et manifestes de projet traversant des liens symboliques
ou sortant des répertoires Applications/Archives sont rejetés. Les
identifiants vides ou dupliqués, versions non supportées, clés ambiguës,
sources relatives contenant `..` et ordres d'étapes dupliqués sont
refusés. Un projet archivé est consultable mais son parcours ne peut
pas être modifié avant sa réouverture.

Les `SourcePaths` sont des chaînes workspace-relatives seulement :
leur interprétation métier, l'intégrité des GUID référencés, la
traçabilité des opérations et les liens UI seront développés dans
les prochains incréments. Ces seules références ne constituent ni
approbation ni preuve d'exécution.

## Tests Windows

`ProjectJourneySmokeTests` vérifie :
- Aucun fichier créé à la lecture ou au preview ; Ultra-simple inchangé.
- Modèles Simple/Moyen/Complexe et neuf étapes Complexe.
- Création opt-in, IDs non vides/uniques, relecture après redémarrage.
- Mise à jour d'état et réordonnancement sans réallocation de GUID.
- Refus d'une création concurrente, d'une révision périmée, d'un GUID
  en collision et de l'écrasement d'un fichier modifié à l'extérieur.
- Surcharge explicite des modèles et opt-in Ultra-simple.
- Conservation du JSON exact, des GUID et de la révision pendant
  archivage puis réouverture ; lecture seule à l'état archivé.

## Suite de l'EPIC #195

1. Ajouter édition guidée des étapes/opérations (ajouter, déplacer, ignorer)
   avec aperçu/diff, confirmation et contrôle de révision.
2. Relier les futurs prompts et contrôles sans dupliquer les tâches/jalons.
3. Ajouter un onglet WPF unique par projet, ouverture depuis palette/worktree,
   dark mode, clavier, DPI et chargement paresseux.
4. Qualifier visuellement sous Windows, les fermetures/archives et les
   performances sur gros workspaces avant toute publication 2.0.
