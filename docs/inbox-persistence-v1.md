# Inbox 1.1 — contrat de persistance v1

Issue : #99. Le workspace 1.0 et ses notes rapides ne sont **pas migrés**.

## Arborescence (créée uniquement à la première capture explicite)

```text
Workspace/
  .workspace.json
  Notes rapides.md             (inchangé)
  Inbox/
    Items/
      <GUID N>/
        item.json              (métadonnées canoniques)
        capture.md             (contenu canonique lisible)
        attachments/
          <GUID N>.<extension> (copie locale optionnelle)
    .staging/                  (captures non publiées)
    Corbeille/                 (éléments récupérables)
    Quarantaine/               (données corrompues préservées)
```

Chaque capture est indépendante ; le GUID reste stable lors d'un archivage,
d'une conversion, d'une suppression ou d'une restauration. Le format utilise
du Markdown ordinaire et un JSON lisible ; aucun index opaque ou appel réseau.

## Format de item.json

```json
{
  "schemaVersion": 1,
  "id": "a9950c59-5e62-41cd-a386-1f71be246add",
  "createdUtc": "2026-10-17T09:00:00+00:00",
  "kind": "text",
  "state": "pending",
  "origin": "manual",
  "convertedTargetRelativePath": null,
  "attachments": []
}
```

- kind : text, link, image, file.
- state : pending, processed, archived.
- origin : provenance facultative, purement descriptive.
- convertedTargetRelativePath : chemin **relatif au workspace**, facultatif ;
  il ne constitue pas une preuve que la cible existe toujours.
- attachments : liste de {originalFileName, relativePath, sizeBytes} ; les chemins
  sont relatifs au répertoire de l'élément et sous attachments/.
- Le texte n'est pas dupliqué dans item.json : il est conservé dans capture.md.
- Versions JSON inconnues : **isolation**, jamais conversion implicite.

## Atomicité et récupération

1. Créer un répertoire unique dans .staging et copier seulement les fichiers
   explicitement choisis par l'utilisateur, au maximum 10 fichiers de 25 MiB.
2. Écrire capture.md et item.json à l'aide du mécanisme d'écriture atomique
   de Nodalis (écriture temporaire + publication).
3. Publier le répertoire terminé par un renommage au sein du même volume.
4. Au premier chargement, reprendre les dossiers .staging complets ; déplacer
   les incomplets ou malformés en Quarantaine, avec diagnostic.txt et un
   avertissement destiné à l'interface / au journal.
5. Une pièce jointe devenue introuvable ne fait pas disparaître la capture :
   elle produit un avertissement et le Markdown reste accessible.

Les mises à jour d'état modifient atomiquement item.json, pas capture.md.
La suppression déplace le dossier complet dans Inbox/Corbeille ; une
restauration refuse tout écrasement en cas de collision. Les données isolées
restent inspectables et récupérables manuellement, sans suppression automatique.

## Sécurité et compatibilité

- Aucune lecture périodique du presse-papiers ; le service ne s'exécute que
  sur demande. Le raccourci global et l'interface sont les issues suivantes.
- Références de pièces jointes et de conversion : chemins relatifs stricts,
  refus des chemins absolus, segments .., points ambigus et liens
  symboliques lors des lectures/copies gérées.
- Identité GUID, version, type, état et manifeste validés à la lecture.
- Le schéma de .workspace.json reste **1** et la 1.0 ignore ce dossier
  optionnel sans modifier ses fichiers métier.
- Pas d'OCR, d'IA, de package NuGet ni de synchronisation réseau.
- En cas de panne de disque ou de droits d'accès, les exceptions restent
  remontées au demandeur et ne sont pas masquées.

## Qualification

`InboxPersistenceSmokeTests` vérifie la création différée, la reprise,
la modification d'état, l'isolation des données cassées, les pièces jointes,
la conservation des notes rapides et la corbeille/restauration.

Implémentations UI, raccourci Win32, filtres, conversion idempotente et
journaux intégrés à la vue sont planifiés dans #100 à #105.
