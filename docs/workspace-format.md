# Format du workspace Nodalis — schéma 1

## Principes

Le workspace est un **répertoire normal**. Le contenu métier doit rester
compréhensible et récupérable sans Nodalis.

- Markdown pour les notes et documents éditables ;
- JSON pour les métadonnées structurelles ;
- GUID immuables pour l'identité ;
- noms de dossiers humains pour la navigation hors Nodalis ;
- aucun historique caché.

## Exemple complet

```text
PRISE DE NOTE/
├── .workspace.json
├── Notes rapides.md
├── Glossaire.md
├── Attachments/
├── Templates/
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
        │       ├── Notes rapides.md
        │       └── Projets/
        │           └── Projet Sauce/
        │               └── .project.json
        └── Projets/
            └── Projet Patate/
                ├── .project.json
                ├── Notes rapides.md
                ├── Glossaire.md
                ├── Jalons/
                │   └── Jalons.md
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

## Identité et liens

Le nom et le chemin ne constituent **jamais** l'identité d'une entité. Les GUID
sont stables après création. Cela permet à l'index de conserver la cible des
liens lorsque l'utilisateur renomme ou déplace un élément.

La syntaxe lisible `[[Nom du document]]` sera résolue par l'index Nodalis vers
l'identité stable correspondante.

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
