# Nodalis 1.1 — Guide de l'Inbox

Fonction ajoutée après la 1.0, sur Windows, locale et sans abonnement
IA. **Une capture est toujours déclenchée volontairement.** Nodalis ne
surveille ni le presse-papiers ni les fichiers.

## 1. Capturer

Par défaut, **Ctrl+Shift+I** ouvre « Nouvelle capture Inbox », y compris
si Nodalis est minimisé. Le raccourci est configurable dans les
préférences ; Windows peut refuser une combinaison déjà réservée. Dans
ce cas, utilisez le menu **Capturer dans Inbox** ou la palette.

Saisissez du texte, une URL HTTP(S), ou cliquez explicitement sur
« Importer le presse-papiers » ou « Choisir des fichiers ». Tant que
vous n'avez pas cliqué sur **Enregistrer**, aucune capture n'est
persistée. **Annuler** n'enregistre rien.

Une empreinte du type, du texte et des octets des pièces jointes permet
de signaler un **doublon probable** sur les 7 derniers jours, y compris
dans la corbeille. Le choix **Oui** conserve volontairement le doublon ;
**Non** garde la fenêtre ouverte sans enregistrer.

Limites : dix fichiers au maximum, 25 Mio chacun et 2 Mio de texte
UTF-8. Les scripts, exécutables et raccourcis Windows ne sont pas
acceptés comme pièces jointes. Les images du presse-papiers sont
copiées en PNG seulement après l'import explicite.

## 2. Trier

Ouvrez **Inbox** depuis l'application. La recherche et les filtres
affichent les entrées en attente, traitées ou archivées. Plusieurs
entrées peuvent changer d'état ensemble ; une seule entrée peut être
convertie à la fois. L'aperçu présente le texte et les avertissements
relatifs aux pièces jointes.

La **Corbeille Inbox** est distincte de la corbeille des documents.
Vous pouvez y retrouver et restaurer une capture supprimée. Si son
identifiant existe déjà, la restauration refuse d'écraser l'entrée
existante.

## 3. Convertir en contenu Nodalis

Sélectionnez une capture, cliquez sur **Convertir…**, puis choisissez
Global, une application ou un projet. Le panneau de droite prévisualise
la cible et le fragment Markdown sans créer de fichier.

| Conversion | Résultat natif |
| --- | --- |
| Notes rapides | Ajout à `Notes rapides.md` du périmètre |
| Nouveau document | Création d'un fichier `.md` sans overwrite |
| Ajouter à un document | Ajout au Markdown existant, dont `Fiche de Maintenance.md` |
| Tâche | Nouvelle checkbox `- [ ]` dans `Tâches.md` |

Les cibles sont immédiatement exploitables par la recherche et les
vues existantes de Nodalis. L'entrée Inbox d'origine reste consultable.

Le marqueur `<!-- nodalis-inbox:<guid> -->` évite de répéter une
conversion déjà publiée. Si une conversion s'est interrompue, le
dialogue récupère son mode et sa destination et refuse un changement
de cible au réessai. Les modifications externes des documents sont
protégées par un contrôle de révision.

L'ajout à une fiche de maintenance reste **un ajout Markdown** :
l'intervention structurée est prévue pour une version ultérieure.

## 4. Où se trouvent les données ?

La première capture crée seulement alors le dossier `Inbox/` :

```text
Workspace/
  .workspace.json
  Notes rapides.md
  Inbox/
    Items/
      <id>/
        item.json
        capture.md
        attachments/                 # optionnel
        conversion.json              # uniquement si conversion demandée
    .staging/
      .capture.lock                  # coordination locale
    Corbeille/
    Quarantaine/
```

`item.json` comporte l'identité, l'état, le type, la date,
`fingerprintSha256` (optionnel pour les anciennes captures), la
provenance et une éventuelle cible convertie. La vérité textuelle est
`capture.md` ; les liens de pièces jointes sont relatifs et
contrôlés. Le format du workspace **reste en schéma 1** et les notes
rapides de la 1.0 ne changent pas.

## 5. Dépannage / récupération

| Problème | Que faire ? |
| --- | --- |
| Ctrl+Shift+I ne fonctionne pas | Vérifier que Nodalis est ouvert et en écriture ; changer la combinaison dans Préférences ; utiliser le menu comme solution de secours |
| Doublon affiché | Vérifier la date et l'état ; choisir Non pour ne rien écrire, ou Oui pour conserver la seconde capture |
| Fichier trop volumineux / extension rejetée | Choisir un document compatible ne dépassant pas 25 Mio |
| Écriture refusée / disque plein | Le texte reste dans la fenêtre ; libérer de l'espace ou restaurer les droits, puis réessayer |
| Pièce jointe disparue | Réapprovisionner la source si elle n'a pas encore été capturée ; sinon vérifier les avertissements et la Quarantaine |
| Conversion interrompue | Réouvrir Convertir ; reprendre le même mode et la même cible préremplis |
| Contenu converti introuvable | Lire le chemin de destination de l'entrée traitée et vérifier si le document a été déplacé |
| Capture supprimée | Ouvrir Corbeille Inbox puis Restaurer |
| Capture isolée | Lire `Inbox/Quarantaine/<id>/diagnostic.txt`, sauvegarder la source et utiliser la procédure de récupération documentée ; ne pas supprimer le dossier par défaut |

Pour les conflits d'édition, les backups et le rollback de l'application,
consultez `troubleshooting.md`, `backups.md` et
`update-recovery.md`.

**Confidentialité :** toutes les opérations se font sur le workspace
local. Aucun texte de capture n'est transmis à un service réseau ou à
une IA. Les exemples et données utilisés dans les tests sont fictifs.
