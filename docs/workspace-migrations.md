# Migrations de workspace Nodalis

## Objectif

Nodalis versionne explicitement le format du workspace avec la propriété
`schemaVersion` de `.workspace.json`. Une évolution qui exige de modifier les
données persistées doit fournir une migration séquentielle vers la version
suivante.

Le schéma courant reste **1**. Les anciens workspaces créés avant
l'introduction explicite de `schemaVersion` sont considérés comme **schéma 0**
et peuvent être migrés vers le schéma 1.

## Règles de sécurité

Une migration suit toujours les étapes suivantes :

1. **Préflight sans écriture** : lecture de la version, construction de la chaîne
   de migrations, vérification de la destination de sauvegarde et refus des
   workspaces plus récents que l'application.
2. **Approbation explicite** : Nodalis n'exécute pas une migration requise sans
   confirmation de l'utilisateur.
3. **Sauvegarde vérifiée** : une archive ZIP standard est créée hors du
   workspace avant la première modification.
4. **Staging** : le workspace complet, y compris les dossiers vides, est copié
   dans un dossier frère temporaire.
5. **Migrations séquentielles** : chaque étape doit produire exactement la
   version attendue avant de passer à la suivante.
6. **Validation finale** : le staging doit pouvoir être relu par le
   `FileSystemWorkspaceStore` du schéma courant.
7. **Remplacement** : le dossier original n'est remplacé qu'après validation du
   staging. En cas d'échec avant ce point, l'original reste intact.
8. **Rapport** : les étapes et fichiers modifiés sont retournés au caller et un
   rapport JSON est écrit à côté des sauvegardes lorsque cela est possible.

Les liens symboliques et jonctions sont refusés par le préflight de migration
afin d'éviter qu'une copie récursive sorte du périmètre du workspace.

## Sauvegardes de migration

Par défaut, pour un workspace :

`C:\Notes\MonWorkspace`

les sauvegardes de migration sont écrites dans un dossier frère :

`C:\Notes\.MonWorkspace.nodalis-migration-backups`

Le service conserve au maximum les 10 sauvegardes de migration les plus
récentes pour ce workspace. Les archives utilisent le même format ZIP vérifié
que les sauvegardes Nodalis normales et restent restaurables séparément.

## Chaîne de migrations supportée

### Schéma 0 → 1 — workspace legacy non versionné

Un manifest legacy peut ne pas contenir `schemaVersion`. Cette absence est
interprétée comme le schéma 0.

La migration :

- ajoute `"schemaVersion": 1` dans `.workspace.json` ;
- conserve les propriétés JSON inconnues déjà présentes ;
- ne réécrit pas les documents Markdown ;
- requiert une sauvegarde vérifiée avant exécution ;
- est idempotente : un workspace déjà en schéma 1 ne produit aucune écriture.

## Ajouter une migration future

Une migration future doit :

- déclarer exactement une version source et une version cible supérieure ;
- être déterministe et idempotente ;
- préciser son niveau de risque et sa politique de sauvegarde ;
- travailler uniquement dans le staging fourni ;
- retourner la liste lisible des changements effectués ;
- disposer d'un smoke test partant de chaque version de schéma supportée ;
- ne jamais supprimer silencieusement des données ambiguës.

Une version de Nodalis ne doit jamais tenter de migrer un workspace dont
`schemaVersion` est supérieur au schéma qu'elle connaît.
