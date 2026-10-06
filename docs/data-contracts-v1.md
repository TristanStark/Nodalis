# Contrats de données Nodalis 1.0

Statut : **figé pour le format 1.0**  
Schéma workspace : **1**

Ce document définit ce qui est persistant et contractuel dans Nodalis 1.0. Il
complète workspace-format.md avec les règles d'autorité, d'identité et de
renommage. Il ne définit pas encore les mécanismes de migration (#70) ni la
politique d'ouverture d'une version future (#71).

## 1. Règle de versioning

Le champ schemaVersion de .workspace.json est l'enveloppe de compatibilité des
données métier du workspace.

Les manifests application, module, projet et les sections n'ont pas leur propre
champ schemaVersion : leur forme est gouvernée par la version du workspace.
Ajouter un champ obligatoire, retirer un champ, changer sa sémantique ou changer
une syntaxe Markdown canonique nécessite donc une évolution du schéma workspace.

Certains formats techniques ont leur version indépendante :

| Contrat | Version 1.0 |
| --- | ---: |
| Workspace métier | 1 |
| Index de liens .nodalis-links.json | 3 |
| Manifest de backup .nodalis-backup.json | 1 |
| Préférences utilisateur | 1 |

## 2. Classes de données

### Données métier canoniques

Elles constituent la source de vérité et ne sont jamais considérées comme un
cache jetable :

- .workspace.json ;
- .application.json ;
- .module.json ;
- .project.json et ses sections ;
- documents Markdown ;
- front matter Markdown ;
- tâches checkbox et leurs métadonnées ;
- jalons ;
- décisions ;
- relations typées écrites dans le Markdown.

### Registre d'identité technique

.nodalis-links.json contient un cas particulier : la collection targets
conserve les GUID techniques des documents ainsi que leurs alias de renommage.

Elle ne doit **pas** être traitée comme un simple cache. Les références et
relations indexées dans ce même fichier sont reconstructibles, mais supprimer
la collection targets peut réattribuer des GUID aux documents et perdre des
alias.

Cette distinction est volontairement figée pour le schéma 1. Une éventuelle
séparation physique de l'identité et des index relève d'une migration de schéma.

### Caches dérivés

Les collections references et relations de .nodalis-links.json sont dérivées du
Markdown et peuvent être reconstruites. En cas de désaccord, le Markdown
canonique gagne toujours.

### Données de récupération

La corbeille et les backups ne sont pas des données métier actives, mais ce ne
sont pas des caches : ils doivent être conservés pour permettre une restauration
fidèle.

### Préférences utilisateur

%LOCALAPPDATA%/Nodalis/preferences.json appartient à l'utilisateur et à la
machine. Ce fichier ne fait pas partie du workspace, ne doit pas être utilisé
comme source de vérité métier et ne doit pas être nécessaire pour ouvrir un
workspace valide.

## 3. Manifests structurels

### .workspace.json

| Champ | Type | Obligatoire | Invariant |
| --- | --- | --- | --- |
| id | GUID | oui | créé une fois, jamais remplacé lors d'un renommage |
| name | texte | oui | libellé humain modifiable |
| schemaVersion | entier | oui | vaut 1 pour ce contrat |
| createdUtc | date/heure | oui | instant de création, UTC recommandé |

Le GUID du workspace définit son identité. Changer le nom ou le dossier racine
ne crée pas un nouveau workspace.

### .application.json

| Champ | Type | Obligatoire | Invariant |
| --- | --- | --- | --- |
| id | GUID | oui | identité immuable |
| name | texte | oui | nom affiché, modifiable |
| createdUtc | date/heure | oui | date de création |

La version du schéma est héritée de .workspace.json.

### .module.json

| Champ | Type | Obligatoire | Invariant |
| --- | --- | --- | --- |
| id | GUID | oui | identité immuable |
| applicationId | GUID | oui | application propriétaire |
| parentModuleId | GUID ou null | non | parent logique pour un module imbriqué |
| name | texte | oui | nom affiché, modifiable |
| createdUtc | date/heure | oui | date de création |

Un déplacement ou renommage ne change ni id ni applicationId. Un changement
réel de parent doit mettre à jour parentModuleId, pas recréer le module.

### .project.json

| Champ | Type | Obligatoire | Invariant |
| --- | --- | --- | --- |
| id | GUID | oui | identité immuable |
| name | texte | oui | nom affiché, modifiable |
| applicationId | GUID | oui | application propriétaire |
| moduleId | GUID ou null | non | module propriétaire éventuel |
| parentProjectId | GUID ou null | non | parent pour un sous-projet |
| initialComplexity | enum | oui | complexité choisie à la création |
| sections | tableau | oui | structure de sections du projet |
| createdUtc | date/heure | oui | date de création |

Un sous-projet reste un projet complet. Son parentProjectId exprime la relation
de parenté indépendamment du nom du dossier.

### Sections de projet

Chaque entrée sections contient :

| Champ | Type | Obligatoire | Invariant |
| --- | --- | --- | --- |
| id | GUID | oui | identité de section immuable |
| name | texte | oui | librement renommable |
| order | entier | oui | ordre d'affichage |
| isSingleton | booléen | oui | contrainte d'usage de la section |
| templateKey | texte ou null | non | rôle/template initial conservé |

Les rôles minimaux Jalons, Technique, Glossaire et Tests sont identifiés par la
politique de section du projet. Un renommage d'une section ne doit pas créer un
nouvel id.

Dans le format 1.0, **Jalons et Glossaire sont des rôles logiques mais pas des
dossiers physiques** : leur contenu canonique est stocké directement à la racine
du projet dans `Jalons.md` et `Glossaire.md`. Technique, Tests et les autres
sections restent des dossiers sauf évolution explicite de leur contrat.

## 4. Documents Markdown

Le texte Markdown est la donnée canonique. Un document sans front matter est
valide. Nodalis doit préserver autant que possible les retours à la ligne et le
contenu inconnu qu'il ne comprend pas.

### Front matter

Le sous-ensemble portable est délimité par une ligne --- en première ligne et
une seconde ligne --- de fermeture. Chaque propriété supportée suit la forme :

    clé: valeur

Clés standard :

- status ;
- owner ;
- version ;
- environment ;
- type ;
- tags.

Les clés inconnues restent autorisées. Les noms de clés sont traités sans
sensibilité à la casse par le parseur. tags accepte une liste séparée par des
virgules, avec ou sans crochets.

Le front matter n'est pas un registre d'identité document dans le schéma 1.

### Tâches

La source de vérité de l'état est la checkbox :

    - [ ] Tâche ouverte
    - [x] Tâche terminée

Les métadonnées facultatives sont des segments lisibles séparés par une barre
verticale :

    - [ ] Préparer la recette | Responsable: Alice | Échéance: 2026-10-10 | Priorité: Haute | Statut: En cours | Tags: api, recette

Champs reconnus : Responsable/Owner, Échéance/Due, Priorité/Priority,
Statut/Status et Tags/Tag.

L'identifiant TaskItem.Id est un identifiant dérivé d'occurrence utilisé par
l'application ; il n'est pas une donnée métier persistée séparément. Une vue
tâches, calendrier ou kanban ne devient jamais une seconde source de vérité.

### Jalons

Les jalons restent dans le document racine `Jalons.md` du projet. Les objets
MilestoneItem sont des projections de lecture. Le nom, la date cible, le statut,
la description, le lien et les dépendances doivent pouvoir être retrouvés depuis
le Markdown source.

Une dépendance non résolue reste visible comme avertissement ; Nodalis ne doit
pas la rediriger silencieusement.

### Décisions

Une décision est un document Markdown. Les propriétés de vue telles que statut
de cycle de vie, décision, contexte, justification, impacts, source et liens
sont lues depuis ce document. DecisionRecord est une projection et non un
fichier de données parallèle.

### Relations typées

Forme canonique :

    > Relation: dépend de | Cible: Architecture API | ID: 42e15594-93d4-4318-9d71-5ebccb65c55c

Règles :

- le type est ouvert : une valeur inconnue reste valide et visible ;
- ID est l'identité cible persistée ;
- Cible est un libellé humain descriptif ;
- un ID invalide ou introuvable produit une relation cassée visible ;
- un renommage peut réécrire le libellé après validation utilisateur, sans
  changer l'ID cible.

## 5. Liens et identité des documents

Les applications, modules, projets et sections ont des GUID dans leurs données
canoniques. Les documents Markdown du schéma 1 n'embarquent pas leur propre
GUID.

Le registre targets de .nodalis-links.json conserve donc pour chaque cible :

- id ;
- kind ;
- displayName ;
- qualifiedName ;
- relativePath ;
- scopeIdentity ;
- localRelativePath ;
- contentHash ;
- aliases.

Lors d'un rafraîchissement, Nodalis tente de réassocier un document à l'identité
existante par chemin, portée stable et hash de contenu. Un renommage ou
déplacement reconnu conserve l'ID et ajoute les anciens noms comme alias.

**Invariant important du schéma 1 :** .nodalis-links.json n'est pas entièrement
jetable. Les références et relations sont reconstructibles, mais les GUID de
documents et les alias dépendent du registre targets existant.

## 6. Corbeille

Chaque entrée de Corbeille possède .trash.json :

| Champ | Type | Rôle |
| --- | --- | --- |
| entryId | GUID | identité de l'opération de suppression |
| kind | enum | type d'élément supprimé |
| displayName | texte | libellé utilisateur |
| originalRelativePath | chemin relatif | destination de restauration |
| payloadName | texte | nom du payload stocké |
| deletedUtc | date/heure | date de suppression |
| itemId | GUID ou null | identité métier de l'élément si disponible |

Le payload original reste intact. Une restauration ne doit jamais écraser une
cible existante ; une collision laisse l'entrée dans la corbeille.

La corbeille est exclue de la navigation métier, des recherches et des index
dérivés.

## 7. Backups

Le backup est un ZIP contenant une copie du workspace et un manifest
.nodalis-backup.json version 1.

Le manifest contient au minimum :

- schemaVersion ;
- workspaceId ;
- workspaceName ;
- workspaceSchemaVersion ;
- createdUtc ;
- fileCount.

workspaceId doit correspondre au workspace restauré. workspaceSchemaVersion
indique le format métier contenu dans l'archive. Un backup n'est pas un cache
et sa validation doit précéder toute restauration.

## 8. Préférences utilisateur

preferences.json possède son propre schemaVersion 1 et peut contenir notamment :

- chemin du dernier workspace ;
- état d'expansion et dimensions de panneaux ;
- préférences éditeur ;
- configuration de backup ;
- configuration IA locale ;
- récents, recherches récentes, favoris et bookmarks ;
- onglets ouverts et raccourcis.

Ces valeurs peuvent être normalisées ou bornées au chargement. Leur absence ou
leur perte ne doit pas rendre le workspace invalide.

### Compatibilité avec l'ancien layout Jalons/Glossaire

Les anciens chemins `Jalons/Jalons.md` et `Glossaire/Glossaire.md` sont
reconnus pour permettre une migration sans perte. Nodalis peut déplacer
automatiquement le document à la racine et supprimer l'ancien dossier uniquement
si ce dossier ne contient aucun autre élément utile. Si du contenu supplémentaire
est présent, il est conservé, le document historique reste utilisé et un
diagnostic doit signaler qu'une décision utilisateur est nécessaire.

## 9. Invariants de renommage et déplacement

1. Renommer un workspace, une application, un module, un projet ou une section
   ne remplace jamais son GUID.
2. Un chemin humain n'est pas l'identité d'un manifest.
3. Les liens textuels peuvent être réécrits uniquement après validation
   explicite lorsque Nodalis propose un diff.
4. Les alias du registre de liens permettent de continuer à résoudre un ancien
   nom après un renommage reconnu.
5. Les relations typées conservent le GUID cible même si le libellé change.
6. Aucun renommage ne doit modifier silencieusement le contenu Markdown sans
   passer par le mécanisme de réécriture prévu.

## 10. Invariants de persistance

- JSON structurel écrit atomiquement ;
- Markdown métier reste lisible hors Nodalis ;
- chemins persistés internes au workspace sont relatifs lorsque le contrat le
  prévoit ;
- aucun index dérivé ne peut remplacer sa source Markdown ;
- aucune préférence utilisateur ne devient nécessaire pour interpréter le
  workspace ;
- une donnée inconnue mais tolérée par un format ouvert ne doit pas être
  supprimée arbitrairement ;
- le schéma 1 est la référence de compatibilité à partir de laquelle #70 devra
  définir les migrations futures.

## 11. Registre machine

Le code expose les mêmes classifications via
Nodalis.Core.Contracts.PersistenceContractCatalog. Les tests de contrat
vérifient que les clés sont uniques, que le schéma workspace figé reste 1 et
que les catégories canonique, identité, cache, récupération et préférence sont
toutes représentées.

Le registre et ce document doivent évoluer ensemble lors de toute future
modification de format.
