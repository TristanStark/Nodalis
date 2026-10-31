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

 
## Navigation et relations entrantes vers les blocs (phase suivante #206)

La palette WPF propose désormais **Atteindre un bloc par ID**. Après résolution par `BlockIndexQuery`, Nodalis vérifie que le document ouvert est bien celui du bloc puis relit la position du marqueur dans le texte de l'éditeur, sur un worker. Une collision ou une disparition est affichée ; aucun positionnement arbitraire, aucune écriture et aucun nouveau nœud dans le Worktree.

Les GUID d'objets documentaires et de blocs partagent un espace d'identité : un même GUID ne peut pas légitimement représenter à la fois un document et un bloc. Les nouvelles attributions refusent cette collision et les lectures existantes la signalent comme ambiguë.

`BlockRelationIndexQuery.GetIncoming` construit une projection des relations typées `> Relation: ... | Cible: ... | ID: ...` ciblant un GUID de bloc. Elle utilise exclusivement les relations et cibles de l'index partagé, sans nouvelle source métier, et ne retourne des lignes que lorsque le bloc et le document source sont uniques. La liste des types connus s'enrichit des verbes du Parcours 2.0 (provient de, vérifie, concerne, succède à, corrige, revalide) ; les types libres existants restent autorisés.

**Restant** : associer les relations sortantes à un bloc source plutôt qu'au seul document, ajouter la sélection de blocs dans les formulaires de relation, étendre l'exploration aux blocs, et qualifier l'interface sur Windows.


## Première attribution dans l'éditeur (incrément #206)

La palette propose également **Identifier le titre courant (2.0)**. L'utilisateur place son curseur sous un titre Markdown, reçoit un aperçu explicite du marqueur GUID à ajouter, puis confirme ou annule. L'éditeur insère uniquement la nouvelle ligne et réutilise le mécanisme existant de sauvegarde, contrôle de modifications externes, undo et indexation. Les documents existants ne sont jamais modifiés au simple chargement, et les projets en lecture seule restent intacts.

Cette commande ne remplace pas encore le futur dialogue détaillé de relation ou l'assistant de migration ; la qualification visuelle et les cas multi-onglets/édition externe restent ouverts dans #206.


## Identité du bloc source des relations — schéma v5 (#207)

Le registre dérivé des liens passe de v4 à **v5** et conserve pour une relation typée un champ optionnel `SourceBlockId`. Le calcul ne modifie jamais le Markdown : une relation appartient au *titre ATX directement précédent* uniquement si ce titre possède un GUID valide et non dupliqué. Un sous-titre sans marqueur interrompt cette attribution : la relation reste au niveau du document, sans héritage implicite du bloc parent. Un GUID dupliqué désactive l'identification des deux blocs sources concernés.

L'index v4 se reconstruit une seule fois en conservant ses IDs documentaires ; les actualisations suivantes réutilisent les relations des documents non modifiés. La cible reste le GUID explicite déjà inscrit dans la ligne Markdown.


## Relations bidirectionnelles et dialogue de sélection (#207)

La recherche de cibles dans le dialogue Relation accepte désormais documents, projets et blocs identifiés, par nom, chemin ou GUID, avec aperçu du type et de l'identifiant. L'ensemble est calculé depuis l'index partagé, et limité visuellement aux 100 premiers résultats pour éviter les listes WPF volumineuses. Les GUID en collision sont exclus. Après confirmation, Nodalis revérifie la cible avant insertion ; une modification ou une disparition annule l'opération.

Le panneau Contexte sait afficher les relations entrantes et sortantes du bloc consulté via « Atteindre un bloc par ID ». Les relations cassées ou ambiguës apparaissent comme telles ; le double-clic vers une cible bloc passe par la navigation avec revérification du marqueur dans le Markdown ouvert. Le document reste la source de vérité, et aucun lien n'est créé automatiquement.

Les nouveaux tests couvrent la migration v4→v5, l'ambiguïté entre catégories, les 175 blocs successifs d'un document volumineux, l'imbrication des titres et les sources sans identifiant.


## Confirmation de relation : garde source et modifications externes (#207)

Avant l’ouverture du sélecteur, la commande WPF fige le GUID du document source, son chemin, le texte et la sélection. Après validation de la cible et relecture de l’index, elle refuse toute insertion si la source, le curseur ou la sélection diffèrent du snapshot. La session documentaire existante compare également sa révision disque avant de modifier l’éditeur ; le mécanisme d’autosave détecte encore les conflits entre cette vérification et l’écriture. Cette protection évite les mauvaises attributions lors d’un changement d’onglet, d’une actualisation extérieure ou d’une insertion décalée. Elle **ne constitue pas** un verrou global entre plusieurs instances de Nodalis : la sérialisation des écritures inter-processus reste à étudier.
