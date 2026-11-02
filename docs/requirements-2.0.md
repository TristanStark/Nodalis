# Exigences — Nodalis 2.0 (premier incrément)

Les exigences sont des **fichiers Markdown indépendants** dans
`Documentation/Exigences/EXI-0001.md`, à l'intérieur d'un projet.
Chaque exigence possède un GUID persistant distinct de sa référence lisible.
La lecture ne crée pas de fichiers ni ne migre les anciens documents.

## État initial et sémantique

Le format portable `nodalis-requirement-schema: 1` utilise un front matter
et quatre champs d'identité (GUID de l'exigence, GUID du projet, référence
EXI-xxxx, titre) ainsi que la date de création. Le titre est encodé comme
une chaîne JSON pour éviter l'injection de champs YAML.

Les quatre dimensions sont distinctes :
- `kind` : classification, initialement `Unclassified` ;
- `validation` : décision métier, initialement `Draft` ;
- `implementation` : réalisation, initialement `NotStarted` ;
- `verification` : preuve fonctionnelle, initialement `NotVerified`.

**Aucune présence de lien, tâche, test ou fichier ne modifie ces états**.
La création n'est ni une approbation, ni une réalisation, ni une vérification.
Les états `Approved` et `Verified` ne sont pas proposés par ce premier
incrément : les transitions et les décisions humaines historisées
arriveront avec les incréments suivants.

## Création et sécurité

Le service `ProjectRequirementRegistryService` fournit `ListAsync`
(lecture seule) et `CreateAsync` (création explicite).

- Les dossiers ne sont créés que lors d'une création approuvée.
- Un projet archivé ou hors workspace ne peut pas recevoir d'exigences.
- Aucune réécriture automatique d'un ancien document.
- Le prochain code est proposé en lecture ; l'interface pourra figer
  un `expectedReference` entre prévisualisation et validation.
- Publication atomique d'un nouveau fichier, sans écraser un homonyme.
- Rejet des dossiers liés/jonctions, sources non UTF-8, GUID/références
  ambigus, cartes hors projet, front matter invalide ou incompatible.
- Le registre est recalculé depuis les sources Markdown, pas stocké
  dans une deuxième base ni reconstruit à chaque dessin WPF.

La section « Critères d'acceptation » reste vide à la création.
Un critère réel devra recevoir un GUID de bloc stable via la commande
existante « Identifier le titre actuel » ; aucun critère fictif n'est
comptabilisé comme couvert.

## Qualification actuelle

Tests synthétiques : projet principal et autre projet, ancien fichier
réservant une référence, aller-retour format/parse, doublons de GUID,
collisions de références, mauvais propriétaire, annulation, état inconnu,
injection de champs YAML et confirmation de l'absence de mutation.

La Command Palette propose **Créer une exigence**. La commande demande
un titre, affiche le nom du projet, la référence et le chemin prévus,
puis exige une confirmation avant écriture. Si le registre évolue entre
l'aperçu et la validation, l'action échoue plutôt que d'attribuer une
nouvelle référence en silence. La fiche créée s'ouvre dans l'éditeur.

**À venir (EPIC #198)** : critères `CA-xx` identifiés, qualification humaine avec journal
append-only, liens critères→tests et matrice de traçabilité distinguant
couverture, exécution, conformité et décision.

Aucun modèle d'IA ni accès réseau requis.

La lecture du registre et la création sont exécutées sur un worker :
l'énumération de fichiers et le parsing du Markdown ne bloquent pas
le Dispatcher WPF. Si l'utilisateur navigue ailleurs pendant la création,
le nouveau document reste enregistré au projet approuvé sans détourner
le focus vers un autre projet.

## Critères d'acceptation — projection en lecture seule

`ProjectRequirementCriterionCodec.Inspect` examine les titres de niveau 3
situés dans `## Critères d'acceptation` (pas ailleurs) :
`### CA-01 — Résultat attendu` suivi immédiatement de
`<!-- nodalis:block:GUID -->`. Les titres dans des blocs de code
Markdown sont ignorés. Les marqueurs utilisent le codec de blocs
Nodalis 2.0 existant ; le texte d'un critère peut être renommé sans
perdre son GUID.

Un critère sans identifiant, sans libellé ou avec un numéro local
répété est signalé et **n'est pas identifié localement sans ambiguïté**.
Les collisions de GUID entre fichiers doivent encore être vérifiées
par l'index partagé avant toute liaison à un test. La présence d'un
critère ne signifie pas qu'il est couvert, exécuté ou conforme.

La projection `ProjectRequirementEntry.Criteria` est dérivée du
Markdown lors de la lecture, sans écrire de registre parallèle.
Les diagnostics de critères sont séparés des diagnostics de fichiers
invalides. Les smoke tests couvrent les doublons, titres renommés,
sections externes, marqueurs malformés et blocs de code.
