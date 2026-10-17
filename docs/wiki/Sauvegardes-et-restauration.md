# Sauvegardes et restauration

## Principe

Nodalis crée des sauvegardes **ZIP standard** du workspace, indépendamment du dossier contenant l'application. Les documents Markdown et manifests JSON restent les sources de vérité.

## Créer une sauvegarde

Dans le gestionnaire **Sauvegardes**, choisissez une destination **hors du workspace**. Les sauvegardes peuvent être :

- **manuelles** ;
- **automatiques**, avec cadence configurable de **15 minutes à 7 jours** ;
- conservées selon une **rétention de 1 à 100 archives**.

Une archive temporaire est construite et entièrement validée avant de prendre son nom définitif.

## Ce que Nodalis contrôle

La validation vérifie notamment :

- la validité du ZIP et de ses entrées ;
- l'absence de traversée de chemin et de noms dangereux ;
- la présence de `.workspace.json` et du manifest de sauvegarde ;
- l'identifiant et le schéma du workspace ;
- le nombre de fichiers attendu.

La destination ne peut pas être égale au workspace ni située à l'intérieur de celui-ci, pour éviter les sauvegardes récursives.

## Restaurer

Une restauration doit viser un **dossier distinct et vide**. Nodalis valide l'archive, extrait son contenu dans une zone temporaire puis vérifie le workspace restauré avant finalisation.

Il **n'écrase pas directement le workspace ouvert**. Une archive issue d'un workspace différent peut être refusée depuis le gestionnaire courant.

## Ce qui ne remplace pas une sauvegarde

La **Corbeille** aide à récupérer les suppressions ordinaires, mais ne remplace pas un backup externe. Son vidage est définitif.

Les **préférences locales** du poste ne font pas partie du contenu métier du workspace.

**Référence :** [Spécification des sauvegardes](https://github.com/TristanStark/Nodalis/blob/develop/docs/backups.md).
