# Sauvegardes du workspace

Nodalis peut créer des sauvegardes locales du workspace sous forme d'archives
ZIP standards. Les sauvegardes restent lisibles avec n'importe quel outil ZIP
et ne deviennent jamais une seconde source de vérité métier.

## Destination

Le dossier de sauvegarde est une préférence locale au poste.

- il doit être situé **hors du workspace** ;
- Nodalis refuse une destination égale au workspace ou située sous celui-ci ;
- cette règle empêche une archive de capturer récursivement ses propres
  sauvegardes ;
- la destination peut se trouver sur un autre disque ou dans un dossier
  synchronisé par un outil externe, sans que Nodalis dépende de cet outil.

## Création

Une sauvegarde peut être créée manuellement depuis **Sauvegardes** ou
automatiquement selon une cadence configurable de 15 minutes à 7 jours.

La création se fait hors du thread d'interface. Nodalis :

1. inventorie les fichiers stables du workspace ;
2. écrit une archive temporaire dans le dossier de sauvegarde ;
3. ajoute un manifest `.nodalis-backup.json` ;
4. relit et valide entièrement l'archive ;
5. renomme l'archive temporaire vers son nom définitif uniquement si elle est
   valide ;
6. applique la rétention configurée (1 à 100 archives).

Les fichiers temporaires d'écriture atomique et les répertoires d'import en
staging ne sont pas archivés.

## Format ZIP

Les noms produits suivent le format :

```text
Nodalis-<workspace-guid>-YYYYMMDD-HHMMSSfff.zip
```

L'archive contient le workspace à sa racine, notamment `.workspace.json`,
ainsi qu'un manifest de sauvegarde :

```json
{
  "schemaVersion": 1,
  "workspaceId": "7ad1733d-79e0-4bad-9fb8-126757ed6ed0",
  "workspaceName": "PRISE DE NOTE",
  "workspaceSchemaVersion": 1,
  "createdUtc": "2026-10-05T04:35:00+00:00",
  "fileCount": 42
}
```

## Validation

Avant d'être déclarée valide, une archive est relue intégralement. Nodalis
vérifie notamment :

- la structure ZIP et l'intégrité de chaque entrée compressée ;
- l'absence de chemins absolus, doublons ou traversées `..` ;
- la présence du manifest de sauvegarde et de `.workspace.json` ;
- la concordance du GUID et de la version de schéma du workspace ;
- le nombre de fichiers annoncé par le manifest.

La fenêtre de sauvegardes affiche la date, la taille et le statut de validation
de chaque archive du workspace courant.

## Restauration

La restauration ne remplace jamais le workspace actuellement ouvert.

Nodalis exige un **dossier distinct et vide**, valide l'archive avant
extraction, extrait d'abord dans un répertoire de staging privé, valide le
workspace restauré puis déplace ce staging vers la destination finale.

Si la destination contient déjà un fichier ou un autre workspace, l'opération
est refusée sans écrasement. Une archive appartenant à un autre GUID de
workspace est également refusée depuis le gestionnaire du workspace courant.
