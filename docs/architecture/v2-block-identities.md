# Nodalis 2.0 — Identités persistantes des blocs Markdown (EPIC #193)

## Premier contrat implémenté

Une identité de document (`.nodalis-links.json`) et une identité de bloc sont deux choses différentes. Un critère d'acceptation ou un test peut vivre sous un titre Markdown dans un grand document, sans créer de fichier annexe.

Après une ligne de titre ATX de niveau 1 à 6, l'identifiant stable facultatif se présente ainsi :

```markdown
## Critère d'acceptation : consultation
<!-- nodalis:block:bd3bd971-8d2f-469c-8b02-46d85ea31d66 -->
La recherche doit afficher une liste de résultats.
```

- Le GUID, non le titre ou son numéro de ligne, porte l'identité métier. Renommer, déplacer la section ou déplacer le fichier conserve le GUID tant que le marqueur accompagne son bloc.
- Le marqueur est un commentaire HTML Markdown : lisible dans le fichier et invisible au rendu habituel. Il n'est pas un registre indépendant.
- Les documents 1.9 sans marqueur restent valides et ne sont **jamais** modifiés lors de l'analyse.
- `MarkdownBlockIdentityCodec.Inspect` analyse les titres hors blocs de code et signale les identifiants malformés ou dupliqués dans un fichier.
- `FindCrossDocumentCollisions` permet de signaler la réutilisation d'un identifiant par plusieurs documents inspectés ; la sélection des fichiers et l'invalidation du cache seront faites via l'index partagé.
- `InsertMarker` produit uniquement une **nouvelle chaîne en mémoire**, sur demande explicite. Il préserve les caractères, le contenu et le style de fin de ligne, refuse tout écrasement de marqueur et est idempotent pour la même affectation. Il n'écrit aucun fichier.

## Protection des données et suites

Ce premier lot n'installe pas de migration automatique, n'altère pas le Worktree et ne modifie ni le registre des liens 1.9 ni son format de persistance. Aucun numéro de schéma n'est modifié.

**À compléter sous #193** : service d'attribution intégrant le contrôle d'unicité workspace et la protection contre modification externe, index inverse incrémental, relations typées bloc ↔ bloc et document ↔ bloc, opérations transactionnelles, historique et transfert. Ne pas utiliser `InsertMarker` seul pour enregistrer un fichier : l'appelant doit valider le snapshot de départ, les collisions inter-documents et le commit atomique avant toute écriture.

La suite de tests `MarkdownBlockIdentitySmokeTests` couvre documents historiques, CRLF, idempotence, déplacement/renommage, titres de code, collisions et marqueurs invalides.
