# Nodalis v1.9 — Récupération du premier import DOCX (#165)

## Principe

Nodalis écrit un **journal d'anticipation (write-ahead)** dans le workspace avant
de créer un projet ou de modifier ses fichiers. Cette sécurité fonctionne sans
NuGet supplémentaire, IA, service externe, ni accès réseau.

Chaque transaction initiale a son répertoire `Imports/.nodalis-first-imports/<importId>/`
avec un `journal.json`. Pour un projet **existant**, le journal contient en plus
une copie binaire `project-manifest.bak` du manifeste `.project.json`.

Le journal contient les chemins relatifs des Markdown, images intégrées et
copies DOCX prévus, leurs SHA-256 et la destination de la provenance. Le hash
du manifeste projet est enregistré **avant son remplacement**, et celui de la
provenance **avant sa création**. Les images sont écrites dans un fichier
temporaire voisin puis publiées par renommage atomique après vérification.

Le verrou `Imports/.nodalis-first-imports/.lock` interdit le lancement
simultané de deux premiers imports ou d'une récupération pendant un import.
Le journal marque `Completed` seulement après la validation de tous les
fichiers réellement enregistrés.

## Au démarrage de l'assistant Word

1. Utiliser la palette de commandes **Récupérer un import DOCX interrompu**
   sans choisir de nouvelle source ; ou sélectionner **Importer un document Word**.
2. Si un import antérieur est interrompu, Nodalis avertit **avant** l'analyse
   de la nouvelle source. Fermer et sauvegarder les onglets des projets
   concernés avant toute tentative de récupération.
3. Confirmer la récupération : une transaction complète dont la provenance
   et tous les fichiers sont identiques est marquée terminée ; une transaction
   incomplète revient à l'état précédent lorsque les sauvegardes et empreintes
   sont vérifiables.
4. Si un fichier a été modifié depuis l'interruption, la récupération s'arrête
   **sans supprimer ni écraser cette modification**. Conserver une copie
   des données divergentes, puis procéder à une vérification manuelle avant
   de recommencer.

**Un projet créé pendant une transaction interrompue reste en place** si une
reprise doit retirer seulement les fichiers DOCX importés : son squelette,
sa fiche de maintenance et ses éventuelles notes humaines ne sont pas effacés
par une récupération brutale. Cette trace doit être inspectée ou archivée
explicitement si elle n'est plus utile.

Les journaux historiques Completed/RolledBack sont conservés pour diagnostic.
Un arrêt avant la préparation définitive du journal peut laisser une sauvegarde
isolée ou un temporaire. Ces artefacts ne justifient pas la suppression du
workspace ni du dossier projet.

## Limites avant la release 1.9

- Les tests automatisés simulent le redémarrage en libérant le verrou système
  **sans** appeler les méthodes de complétion/rollback, et vérifient des
  collisions et éditions externes.
- Une qualification manuelle sur un véritable arrêt forcé de Nodalis, avec
  inspection des journaux et fichiers temporaires, reste nécessaire (#171).
- Les protections de concurrence sont optimistes ; elles n'empêchent pas un
  processus externe malveillant de modifier un fichier pendant les quelques
  instructions séparant une ultime vérification d'une opération disque.
- Ne jamais supprimer manuellement un journal `Prepared` ou une sauvegarde
  avant d'avoir sauvegardé et inspecté les fichiers du projet.

Voir [checklist de sortie](release-checklist-1.9.md) et
[qualification utilisateur](docx-1.9-manual-qualification.md).
