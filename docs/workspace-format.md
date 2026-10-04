# Format du workspace Nodalis — schema 1

## Racine

```text
Workspace/
├── .workspace.json
├── Notes rapides.md
├── Glossaire.md
├── Applications/
├── Attachments/
└── Templates/
```

`.workspace.json` contient l'identité stable du workspace et la version du
schéma.

Exemple :

```json
{
  "id": "7ad1733d-79e0-4bad-9fb8-126757ed6ed0",
  "name": "PRISE DE NOTE",
  "schemaVersion": 1,
  "createdUtc": "2026-10-04T10:00:00+00:00"
}
```

## Règles

1. Les noms affichés ne sont jamais utilisés comme identité métier.
2. Les applications, modules, projets, sections et documents structurés auront
   un GUID stable.
3. Les chemins physiques suivent autant que possible la hiérarchie logique.
4. Les noms incompatibles avec Windows sont normalisés.
5. En cas de collision de dossier, Nodalis propose un suffixe numérique plutôt
   que d'écraser un élément existant.
6. Les fichiers Markdown restent éditables avec un éditeur externe.
7. Aucun historique caché n'est stocké dans le workspace.

## Évolution du schéma

`schemaVersion` est incrémenté uniquement lorsqu'une évolution nécessite une
migration. Une version de Nodalis ne doit jamais sauvegarder silencieusement un
workspace dont le schéma est plus récent que celui qu'elle comprend.
