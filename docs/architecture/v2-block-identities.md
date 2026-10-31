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

## Projection incrémentale de l'index (second incrément #193 / #206)

Le schéma de l'index de liens dérivé est passé de 3 à 4. Les nouvelles listes `Blocks` et `BlockIssues` complètent les références et relations existantes sans changer les sources Markdown.

- Chaque bloc indexé associe `Id` (GUID du marqueur) à `DocumentId` (GUID du document déjà indexé) et à son titre, niveau, position et ligne courants.
- Un renommage/déplacement de fichier ne change pas `DocumentId` : la résolution cherche toujours le chemin courant dans `Targets`. Une modification de titre impose le recalcul du bloc uniquement pour le fichier modifié.
- Lors d'une actualisation à chaud, les blocs et diagnostics des fichiers inchangés sont réutilisés sans reparser le Markdown ; une migration d'index v3 vers v4 reparcourt volontairement les sources une fois.
- Une collision de GUID entre blocs (même document ou documents distincts) retourne **Ambiguous**, jamais le premier bloc trouvé ; les marqueurs malformés/dupliqués restent diagnostiqués dans le JSON reconstructible.
- `BlockIndexQuery.Resolve` lit uniquement l'instantané de l'index ; aucune lecture réseau ni accès disque. Un bloc dont le document propriétaire manque n'est pas déclaré résolu.

**Limite** : le service d'écriture transactionnelle, le contrôle de la présence unique d'un ID avant insertion et l'UI de navigation sont encore à développer. Le nouveau schéma n'est pas un stockage métier autonome : il se reconstruit depuis les sources.

**Compatibilité vérifiée en CI** : le contrat de persistance explicite les nouvelles projections dérivées et la migration d'un index v3 (sans `blocks`/`blockIssues`) vers v4. Les identifiants de documents et les alias préexistants restent dans le registre d'identités.

## Service d'attribution explicite (incrément #206)

`WorkspaceBlockIdentityService` ajoute une paire d'opérations : `PrepareAsync` génère un aperçu immuable sans écrire ; `CommitApprovedAsync` applique uniquement cet aperçu explicitement accepté.

Avant l'écriture, le service revérifie l'empreinte de la source, reconstruit le Markdown attendu (une proposition falsifiée est rejetée) et contrôle l'unicité de l'ID dans l'index actualisé. Les appels concurrents du même processus sont sérialisés par workspace et le fichier est sauvegardé via `TextDocumentSession` et `AtomicFileWriter`. Après la sauvegarde, l'index reconstruit la projection du document concerné. Une répétition de la même attribution est un no-op.

**Limite** : les autres processus non coopératifs et les modifications externes intervenant dans la fenêtre entre la vérification globale et la sauvegarde nécessiteront un protocole de verrouillage inter-processus pour la qualification multi-instance. La nouvelle API n'est pas encore exposée dans l'interface WPF ; aucun document n'est modifié automatiquement au chargement.
