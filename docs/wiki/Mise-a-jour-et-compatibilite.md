# Mises à jour, rollback et compatibilité

## Séparation application / données

Le dossier de Nodalis contient l'exécutable. Le **workspace** contient les données métier. Les conserver séparés permet de mettre à jour le programme sans remplacer vos documents.

Avant toute migration importante, effectuez une [[Sauvegardes-et-restauration|sauvegarde du workspace]].

## Mise à jour locale

Le mécanisme de mise à jour 1.0 installe une version depuis une **archive locale** validée : identité produit, version, architecture Windows x64, fichiers attendus, tailles, empreintes SHA-256 et compatibilité du workspace.

L'utilitaire `Nodalis.Updater.exe` réalise la bascule, vérifie le démarrage de la nouvelle version et conserve une version précédente.

## Retour à la version précédente

La version précédente peut être conservée dans un dossier `.previous`. Si la nouvelle version échoue au test de démarrage, le mécanisme transactionnel tente un **rollback**.

Les journaux de transaction et dossiers de récupération permettent d'enquêter sur une interruption ; évitez de déplacer ou supprimer ces fichiers avant diagnostic.

## Schéma de workspace

La version 1.0 comprend le **schéma 1** :

| Workspace détecté | Comportement |
| --- | --- |
| Schéma 0 ancien | Migration protégée à proposer avant écriture |
| Schéma 1 | Lecture et écriture normales |
| Schéma supérieur à 1 | Consultation de secours Markdown, **lecture seule** |

Il ne faut **jamais modifier manuellement `schemaVersion`** pour forcer une ouverture.

## Conseils

- Vérifiez le SHA-256 de l'archive si disponible.
- Conservez une sauvegarde avant la première ouverture avec une nouvelle version majeure.
- En cas d'échec, consultez [[Depannage-et-FAQ]] et les diagnostics locaux.

**Références :** [Récupération des mises à jour](https://github.com/TristanStark/Nodalis/blob/develop/docs/update-recovery.md) · [Compatibilité](https://github.com/TristanStark/Nodalis/blob/develop/docs/workspace-compatibility.md).
