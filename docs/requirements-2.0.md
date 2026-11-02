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

## Traçabilité calculée — liens de test (02/11/2026)

La projection `ProjectRequirementTraceability.Build` joint le registre
canonique des exigences et **un instantané actualisé** de l'index de liens
partagé (v5) : aucune nouvelle base, aucune écriture, aucun scan pendant
le rendu WPF. Les critères ont quatre états **de couverture uniquement** :
`Unidentified`, `StaleSource`, `Uncovered`, `LinkedTest`.

Un critère est `LinkedTest` uniquement si son GUID et son document sont
uniques, si l'empreinte de la fiche concorde avec l'index, et si une
relation typée `vérifie` ou `teste` est issue d'un bloc de test identifié
et d'une source **du même projet**. Les blocs de test sont reconnus dans
les dossiers `Tests`/`Recette` ou par titre `TEST-`/`TST-` ; ce filtre
provisoire sera remplacé par le référentiel de tests de l'EPIC #200.
Les relations dupliquées vers un même bloc de test sont dédoublonnées.
Les sources documentaires sans bloc, hors projet, non typées, en
collision ou issues d'un index désynchronisé ne créent pas de couverture.

**Important :** `LinkedTest` signifie seulement « au moins un test est
lié ». Ce n'est **pas** « test exécuté », « résultat conforme »,
« preuve vérifiée », « exigence approuvée » ou « vérification acquise ».
La matrice n'altère jamais les trois états de la fiche. Les campagnes,
passages et preuves seront joints après leur implémentation (#200).
Les tests synthétiques couvrent les sources de l'autre projet, le GUID
reproduit, les SHA obsolètes, la ligne déplacée et les liens redondants.

## Consultation dans l'application (02/11/2026)

La palette propose désormais **Matrice de traçabilité des exigences**.
Le panneau charge le registre d'exigences et rafraîchit l'index partagé
sur un worker, puis joint les sources en mémoire. La recherche, les
filtres de couverture et la sélection ne provoquent aucun accès disque.
Les critères sont virtualisés, avec détails et diagnostics de source.
La navigation vers une fiche EXI réutilise l'ouverture documentaire sûre ;
la navigation vers un bloc de test utilise son GUID et revérifie son
identité dans le document au moment du clic. Les documents source ne
sont pas modifiés et aucun statut n'est mis à jour.

Le détail affiche explicitement que l'exécution et la conformité ne
sont pas connues tant que les campagnes de tests (#200) ne sont pas
intégrées. La qualification visuelle en WPF/dark mode reste nécessaire.
