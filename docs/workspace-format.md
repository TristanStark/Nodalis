# Format du workspace Nodalis — schéma 1

## Principes

Le workspace est un **répertoire normal**. Le contenu métier doit rester
compréhensible et récupérable sans Nodalis.

- Markdown pour les notes et documents éditables ;
- JSON pour les métadonnées structurelles ;
- GUID immuables pour l'identité ;
- noms de dossiers humains pour la navigation hors Nodalis ;
- aucun historique caché.

Le contrat de persistance stable pour Nodalis 1.0 est détaillé dans
[data-contracts-v1.md](data-contracts-v1.md). Ce document d'arborescence reste
la vue pratique du format ; le registre 1.0 tranche l'autorité des données,
les versions de schéma et les invariants d'identité.

## Exemple complet

```text
PRISE DE NOTE/
├── .workspace.json
├── Notes rapides.md
├── Glossaire.md
├── Attachments/
├── Imports/
│   └── Sources/
├── Templates/
├── Corbeille/
│   └── 20261005-041500123-0123456789abcdef0123456789abcdef/
│       ├── .trash.json
│       └── Note supprimable.md
└── Applications/
    └── Application A/
        ├── .application.json
        ├── Notes rapides.md
        ├── Glossaire.md
        ├── Documentation/
        │   ├── Technique/
        │   │   └── Architecture.md
        │   └── Fonctionnelle/
        │       └── Présentation.md
        ├── Modules/
        │   └── Module A1/
        │       ├── .module.json
        │       └── Projets/
        │           └── Projet Sauce/
        │               └── .project.json
        └── Projets/
            └── Projet Patate/
                ├── .project.json
                ├── Notes rapides.md
                ├── Glossaire.md
                ├── Jalons.md
                ├── Technique/
                │   ├── recette.md
                │   ├── cuisson.md
                │   └── serving.md
                ├── Tests/
                │   ├── Unitaires/
                │   ├── Fonctionnels/
                │   └── Intégration/
                ├── Réunions/
                │   └── 2026-10-04 - Réunion.md
                └── Sous-projets/
                    └── Projet Patate - Migration/
                        └── .project.json
```

Cette arborescence est une convention par défaut, pas une structure rigide :
les sections d'un projet restent personnalisables.

## Sources importées

Les documents externes importés ne sont jamais modifiés en place.

Pour un DOCX, Nodalis copie d'abord le fichier dans `Imports/Sources/`, puis
ouvre et parse uniquement cette copie locale. Une erreur de parsing nettoie la
copie de travail créée pour l'opération ; le fichier source reste inchangé.

## Propriétés des documents Markdown

Un document Markdown peut commencer par un **front matter optionnel**. Ces
propriétés restent du texte lisible et portable : aucun fichier annexe ni index
opaque n'est nécessaire pour les comprendre.

Exemple :

```markdown
---
status: active
owner: Alice
version: 1.2
environment: production
type: specification
tags: api, backend
reviewer: Bob
---
# Architecture API

Contenu du document.
```

Les clés standard reconnues par l'interface sont `status`, `owner`, `version`,
`environment`, `type` et `tags`. Des propriétés personnalisées peuvent être
ajoutées librement avec une clé composée de lettres, chiffres, tirets,
underscores ou points. Les clés inconnues sont tolérées par le parseur et
restent accessibles à Nodalis.

Le front matter n'est **jamais obligatoire** : une note contenant uniquement du
Markdown reste parfaitement valide. La suppression de toutes les propriétés
supprime simplement le bloc de front matter sans modifier le corps du document.

Les propriétés sont recherchables localement avec la syntaxe suivante :

- `status:active`, `owner:alice`, `type:specification` pour les propriétés standard ;
- `tag:api` ou `tags:api` pour tester un tag individuel ;
- `@reviewer:alice` pour une propriété personnalisée.

## Métadonnées des tâches Markdown

Une tâche reste avant tout une checkbox Markdown. La case `[ ]` ou `[x]` est
la **source de vérité** de son état terminé/ouvert. Nodalis peut compléter la
ligne avec des métadonnées lisibles séparées par `|` :

```markdown
- [ ] Préparer la recette | Responsable: Alice | Échéance: 2026-10-10 | Priorité: Haute | Statut: En cours | Tags: api, recette
```

Champs reconnus :

- `Responsable` / `Owner` : texte libre ;
- `Échéance` / `Due` : date ISO `AAAA-MM-JJ` ;
- `Priorité` / `Priority` : libellé léger (`Critique`, `Haute`, `Normale`, `Basse` ou valeur personnalisée) ;
- `Statut` / `Status` : libellé léger, sans workflow imposé ;
- `Tags` / `Tag` : liste séparée par des virgules.

Les métadonnées sont facultatives et peuvent être modifiées depuis la vue
consolidée des tâches. Nodalis calcule le retard uniquement pour une tâche
ouverte dont l'échéance est antérieure à la date du jour. Les filtres et tris
de la vue consolidée ne créent aucune donnée parallèle : ils relisent toujours
les checkboxes Markdown du workspace.

## Manifest du workspace

Fichier : `.workspace.json`

```json
{
  "id": "7ad1733d-79e0-4bad-9fb8-126757ed6ed0",
  "name": "PRISE DE NOTE",
  "schemaVersion": 1,
  "createdUtc": "2026-10-04T10:00:00+00:00"
}
```

## Manifest d'application

Fichier : `.application.json`

```json
{
  "id": "42e15594-93d4-4318-9d71-5ebccb65c55c",
  "name": "Application A",
  "createdUtc": "2026-10-04T10:05:00+00:00"
}
```

## Manifest de module

Fichier : `.module.json`

```json
{
  "id": "4ce96f6c-c26b-4720-bc2e-975667a75430",
  "applicationId": "42e15594-93d4-4318-9d71-5ebccb65c55c",
  "parentModuleId": null,
  "name": "Module A1",
  "createdUtc": "2026-10-04T10:10:00+00:00"
}
```

Un module imbriqué renseigne `parentModuleId`.

## Manifest de projet

Fichier : `.project.json`

```json
{
  "id": "f323777b-18b6-486e-9bdb-5d33bc42611c",
  "name": "Projet Patate",
  "applicationId": "42e15594-93d4-4318-9d71-5ebccb65c55c",
  "moduleId": null,
  "parentProjectId": null,
  "initialComplexity": "medium",
  "sections": [
    {
      "id": "49f0712f-04a6-42c7-9b6c-7466695dddbd",
      "name": "Jalons",
      "order": 10,
      "isSingleton": true,
      "templateKey": "milestones"
    },
    {
      "id": "712282eb-dd29-47d4-976a-b8df990bcebd",
      "name": "Technique",
      "order": 20,
      "isSingleton": false,
      "templateKey": null
    },
    {
      "id": "170db2be-7781-437e-b95a-c5263ffd57ca",
      "name": "Glossaire",
      "order": 30,
      "isSingleton": true,
      "templateKey": "glossary"
    },
    {
      "id": "30435b21-74d2-44c6-b6e4-9e91ddd38574",
      "name": "Tests",
      "order": 40,
      "isSingleton": false,
      "templateKey": null
    }
  ],
  "createdUtc": "2026-10-04T10:15:00+00:00"
}
```

Un sous-projet renseigne `parentProjectId`. Il reste un projet complet avec
ses propres jalons, tests, technique, glossaire et sections.

## Corbeille Nodalis

Une suppression standard ne détruit pas immédiatement son contenu. Nodalis
déplace le document, projet, module ou application dans `Corbeille/` en
conservant le fichier ou le sous-arbre original intact.

Chaque entrée possède un manifest `.trash.json` lisible :

```json
{
  "entryId": "01234567-89ab-cdef-0123-456789abcdef",
  "kind": "document",
  "displayName": "Note supprimable",
  "originalRelativePath": "Applications/Application A/Note supprimable.md",
  "payloadName": "Note supprimable.md",
  "deletedUtc": "2026-10-05T04:15:00+00:00",
  "itemId": "fedcba98-7654-3210-fedc-ba9876543210"
}
```

`itemId` conserve l'identifiant stable de l'élément lorsqu'il en possède un.
Pour les applications, modules et projets, le payload contient également leur
manifest d'origine et donc leur GUID d'identité.

La restauration vise toujours `originalRelativePath`. Si ce chemin est déjà
occupé, Nodalis **refuse la restauration avant tout déplacement** : aucun
fichier existant n'est écrasé et l'entrée reste dans la corbeille. Le vidage de
la corbeille est une action explicite et définitive.

`Corbeille/` est un espace de récupération, pas une source métier active :
navigation, recherche et index dérivés doivent ignorer son contenu.

## Identité et liens

Le nom et le chemin ne constituent **jamais** l'identité d'une entité. Les GUID
sont stables après création. Cela permet à l'index de conserver la cible des
liens lorsque l'utilisateur renomme ou déplace un élément.

La syntaxe lisible `[[Nom du document]]` sera résolue par l'index Nodalis vers
l'identité stable correspondante.

Dans le schéma 1, `.nodalis-links.json` est un format hybride : `targets`
conserve l'identité technique et les alias des documents, tandis que
`references` et `relations` sont dérivés et reconstruisibles. Le fichier ne doit
donc pas être supprimé comme un cache ordinaire si l'on veut préserver les GUID
de documents et leurs alias. Voir `docs/data-contracts-v1.md` pour le contrat
figé 1.0.

## Relations typées

Une relation métier est stockée **dans le Markdown source**, sur une ligne de
blockquote lisible. L'ID est la source d'identité ; le libellé humain permet de
comprendre le fichier sans Nodalis.

Exemple :

```markdown
> Relation: dépend de | Cible: Architecture API | ID: 42e15594-93d4-4318-9d71-5ebccb65c55c
```

Types proposés initialement par l'interface :

- `dépend de` ;
- `remplace` ;
- `implémente` ;
- `teste` ;
- `documente` ;
- `bloque` ;
- `est lié à`.

Le vocabulaire n'est pas fermé : un type inconnu reste indexé et affiché. La
cible est résolue par GUID, donc un renommage ou déplacement ne casse pas la
relation. Si l'ID est invalide ou ne correspond plus à une cible, la relation
reste visible comme relation cassée au lieu d'être redirigée silencieusement.

L'index de relations est dérivé et reconstruisible. Il n'est jamais une source
de vérité métier.

## Renommage intelligent des liens

Lorsqu'un élément indexé est renommé, son GUID reste stable et son ancien nom
reste disponible comme alias. Les liens existants continuent donc de résoudre
même si le Markdown n'est pas modifié.

Nodalis peut en plus proposer une **réécriture explicite** des liens textuels
`[[Ancien nom]]` vers `[[Nouveau nom]]` :

- les fichiers et lignes concernés sont détectés avant le renommage ;
- un diff avant/après est présenté à l'utilisateur ;
- chaque fichier peut être inclus ou exclu ;
- refuser la réécriture ne casse pas les liens grâce aux IDs et alias ;
- les fichiers sélectionnés sont vérifiés par hash avant écriture ;
- une erreur pendant une réécriture multi-fichier déclenche le rollback des
  fichiers déjà modifiés.

Aucune réécriture textuelle n'est effectuée silencieusement.

## Noms Windows et collisions

Les segments de chemin sont normalisés avant création :

- caractères invalides remplacés ;
- espaces et points terminaux supprimés ;
- noms réservés Windows tels que `CON`, `PRN`, `COM1` protégés ;
- une collision produit un nom du type `Projet (2)` au lieu d'écraser
  silencieusement un élément existant.

La logique est centralisée dans `WindowsPathRules`.

## Écriture des métadonnées

Les manifests JSON sont écrits dans un fichier temporaire situé dans le même
répertoire puis remplacent la destination. L'objectif est d'éviter qu'une
interruption en plein enregistrement ne laisse un JSON partiellement écrit.

## Évolution du schéma

`schemaVersion` est incrémenté uniquement lorsqu'une évolution nécessite une
migration. Une version de Nodalis refuse de sauvegarder silencieusement un
workspace dont le schéma est plus récent que celui qu'elle comprend.

Le préflight, les sauvegardes obligatoires, le staging transactionnel, les
rapports et la chaîne de migrations supportée sont documentés dans
[workspace-migrations.md](workspace-migrations.md).

La politique lecture/écriture, la matrice de versions et le fallback de
consultation des workspaces plus récents sont documentés dans
[workspace-compatibility.md](workspace-compatibility.md).
