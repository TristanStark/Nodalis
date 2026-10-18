# Vues enregistrées — format et stockage (v1.5)

**Disponible dans le moteur sur `develop` :** format de définition JSON
local v1, sans interface de création finalisée à ce stade.

## Deux emplacements volontairement distincts

| Visibilité | Chemin | Sauvegarde et partage |
| --- | --- | --- |
| Workspace | `<workspace>/.nodalis-views.json` | Portable, à inclure dans les sauvegardes du workspace |
| Privée | `%LOCALAPPDATA%/Nodalis/views/<workspace-guid>/views-private.json` | Préférence locale, **jamais** copiée implicitement dans le workspace |

Ces fichiers sont **créés uniquement lors de la première sauvegarde
volontaire d'une vue**. Leurs absences n'empêchent aucun projet, y compris
Ultra-simple, de fonctionner normalement.

## Format versionné

Le document porte `schemaVersion: 1` et une collection `views`.
Chaque définition contient `id` (GUID), `name`, `query`, `layout`
(`List`, `Table`, `Cards`), `columns`, `pinned` et `order`.

La requête contient les types d'items, le périmètre, les conditions
typées ET/OU, le tri, le regroupement, le décalage et la taille de page.
**Aucun résultat projeté, fragment Markdown, tâche copiée, vecteur ni
index de recherche n'est stocké** dans ce fichier.

Exemple illustratif :

~~~json
{
  "schemaVersion": 1,
  "views": [
    {
      "id": "17a62fd7-ae8f-4c53-8aa9-45c66db80b01",
      "name": "Mes urgences",
      "query": {
        "kinds": ["Task"],
        "sortBy": "date",
        "limit": 100
      },
      "layout": "Table",
      "columns": ["title", "date", "priority", "project"],
      "pinned": true,
      "order": 0
    }
  ]
}
~~~

## Protection contre les pertes de données

- JSON lisible (UTF-8), schéma indépendant du manifeste du workspace.
- Écriture via fichier temporaire et remplacement atomique au même emplacement.
- Empreinte SHA-256 du fichier pour détecter un changement externe avant
  de sauver (conflit explicite, aucune écriture silencieuse).
- Fichier corrompu : erreur visible, pas de réinitialisation destructive.
- Version de schéma **future** : lecture limitée en mode consultation,
  écriture refusée.
- Limites : 1 Mo et 1 000 vues par emplacement, noms de 120 caractères.
- Les données Markdown/JSON canoniques restent indépendantes des vues.
- Une nouvelle vue n'entraîne pas la création de tâches ou documents copiés.

Voir aussi [modèle de lecture et requêtes](views-core-1.5.md).
