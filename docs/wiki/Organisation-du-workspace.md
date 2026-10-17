# Organisation du workspace

## Un dossier ordinaire, une structure métier

Le **workspace** est un répertoire normal sur disque. Nodalis y organise un niveau Global, des Applications, des Modules facultatifs, des Projets et des Sous-projets. Un projet appartient à une seule application. Un sous-projet est un projet complet, doté de sa propre organisation.

Les notes rapides et glossaires existent aux scopes **Global**, **Application** et **Projet**. Les modules n'ajoutent pas de quatrième scope.

~~~text
Mon workspace/
├── .workspace.json
├── Notes rapides.md
├── Glossaire.md
├── Templates/
├── Attachments/
├── Imports/Sources/
├── Corbeille/
└── Applications/
    └── Application A/
        ├── .application.json
        ├── Notes rapides.md
        ├── Glossaire.md
        ├── Modules/
        └── Projets/
            └── Projet B/
                ├── .project.json
                ├── Jalons.md
                ├── Glossaire.md
                ├── Notes rapides.md
                ├── Technique/
                └── Tests/
~~~

Cette arborescence illustre un **projet non Ultra-simple**. Les noms, sous-sections et documents peuvent être personnalisés.

## Manifests et identité

Les fichiers `.workspace.json`, `.application.json`, `.module.json` et `.project.json` contiennent les métadonnées structurelles. Les entités possèdent des identifiants stables : un renommage ne signifie pas la création d'une nouvelle entité.

Les fichiers Markdown contiennent le contenu métier, les tâches, les jalons, les décisions et les relations explicites. Les fichiers JSON ne sont pas des substituts au Markdown.

## Le fichier à ne pas supprimer

`.nodalis-links.json` contient notamment `targets` : le registre des GUID de documents et de leurs alias. **Il ne doit pas être supprimé comme un simple cache.** Ses parties `references` et `relations` sont dérivées, mais pas la collection d'identités `targets`.

## Modifier la structure

Privilégiez les commandes de **Structure du projet** pour ajouter, déplacer, réordonner ou renommer des sections. Les profils définissent la création initiale, pas un modèle immuable.

## Compatibilité

Le workspace Nodalis 1.0 utilise le **schéma 1**. Une version trop ancienne peut proposer une consultation Markdown en lecture seule, sans écrire dans un schéma plus récent.

**Références :** [Format du workspace](https://github.com/TristanStark/Nodalis/blob/develop/docs/workspace-format.md) · [Contrats de données 1.0](https://github.com/TristanStark/Nodalis/blob/develop/docs/data-contracts-v1.md).
