# Nodalis 2.0 — jalons, tâches miroirs et cohérence des dates

Suivi : [#208](https://github.com/TristanStark/Nodalis/issues/208),
roadmap [#192](https://github.com/TristanStark/Nodalis/issues/192).
Changements limités à `develop`.

## Dates des jalons

L'éditeur sérialise les jalons au format canonique `dd/MM/yyyy` ;
les collages avec `d/M/yyyy` et les anciennes lignes `yyyy-MM-dd`
restent acceptés. Le lecteur, le Health Check et la couverture des
jalons utilisent le **même** contrat
`Nodalis.Core.Milestones.MilestoneDateCodec`, ce qui supprime les
faux `MILESTONE_DATE_MISSING` lorsque la date est réellement valide.
Une date vide ou impossible reste signalée.

Les dates des **tâches**, en revanche, demeurent au format ISO
`Échéance: yyyy-MM-dd`, attendu par le parseur des métadonnées
de tâche ; cela ne doit pas être modifié par cette correction.

## Synchronisation d'une tâche par jalon

Après chaque **création** ou **modification** dans le dialogue Jalons,
le service écrit une tâche dans le `Tâches.md` racine du projet actif.
La clé de liaison est un `nodalis:task:GUID` persistant, dérivé de
l'identifiant stable du jalon par SHA-256 et un espace de noms versionné.
Le GUID ne dépend donc ni du nom, ni de la date, ni de la position dans le
Markdown. Enregistrer le même jalon plusieurs fois ne crée **jamais**
plusieurs tâches miroirs.

La tâche porte le nom `Jalon : …`, reprend la date cible dans
`Échéance` et reflète le statut terminé/non terminé dans la checkbox.
Les métadonnées de tâche ajoutées par l'utilisateur (responsable,
priorité, tags) restent en place ; l'échéance est toujours pilotée
par le jalon. Les tâches manuelles sans la clé réservée ne sont
ni converties ni modifiées automatiquement. En présence d'une tâche
manuelle ambiguë portant déjà le nom réservé, la création miroir est
refusée et la situation doit être qualifiée.

**Important** : `Jalons.md` et `Tâches.md` sont deux documents Markdown
distincts. Leur sauvegarde ne constitue pas une transaction atomique
multi-fichiers. Si le jalon a été écrit mais que `Tâches.md` ne peut
pas être mis à jour, l'interface avertit explicitement que **le jalon
est déjà enregistré**. Reprendre l'édition de ce jalon après correction
du fichier de tâches relance une synchronisation idempotente. Ne pas
recréer le jalon pour tenter de récupérer sa tâche.

Le retrait d'un jalon ne supprime pas automatiquement ses anciennes
tâches (préservation de l'historique) : il faut qualifier leur sort
séparément. La création de tâches pour des jalons historiques n'est
pas une migration silencieuse : chaque jalon existant doit être
réenregistré une fois, ou faire l'objet d'un futur assistant de
rattrapage explicite.

## Glossaires hérités et contrôle de cohérence

La couverture `FREQUENT_TERM_MISSING_GLOSSARY` considère maintenant
les définitions de trois niveaux **en lecture seule** :

1. Glossaire du projet, y compris les termes et alias formalisés.
2. Glossaire de l'application **propriétaire** du projet.
3. Glossaire global à la racine du workspace.

Les termes de titre, les synonymes et acronymes de l'éditeur canonique
ainsi que les anciennes définitions compactes sont pris en compte. Les
glossaires des **autres applications** ne masquent jamais l'alerte.
Le contrôle ne crée aucune entrée, aucun fichier ni duplicata local.

## Qualification manuelle Windows

Sur un **workspace synthétique**, créer un jalon avec date
`03/11/2026`, le modifier, vérifier que sa seule tâche liée porte
`Échéance: 2026-11-03` puis la nouvelle échéance après modification.
Vérifier que les modifications de responsable et priorité de cette
tâche sont conservées ; vérifier une tâche manuelle homonyme, un projet
archivé et un autre projet. Tester l'état terminé puis la réouverture.

Créer un terme uniquement dans `Glossaire.md` global, puis dans celui
de l'application, sans copie dans le projet : l'alerte de terme manquant
doit disparaître. Répéter avec un terme défini seulement dans une
autre application : l'alerte doit persister.

Le smoke test synthétique vérifie les GUID, les formats, les dates,
la relecture, le caractère idempotent et les garanties de conservation.
Validation ergonomique WPF/dark mode/DPI et CI Windows à confirmer avant
la release.
