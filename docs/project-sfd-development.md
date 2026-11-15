# Projet avec spec (SFD) — première tranche de développement

Référence : roadmap #212, profil #213, structure #214, modèles #215.

## Livré dans cette tranche

- Le manifeste `.project.json` enregistre `kind: withSpecifications` et `kindSchemaVersion: 1`. L'absence de `kind` dans les anciens projets conserve `standard`. Les niveaux Simple/Moyen/Complexe/Ultra-simple restent inchangés.
- La fenêtre **Nouveau projet** propose **Projet classique** ou **Projet avec spec (SFD)**. Le SFD présente un aperçu/confirmation avant création et ne confond pas la famille documentaire avec une complexité 2.0.
- La création utilise la transaction de préparation existante (dossier temporaire puis renommage), pour projets et sous-projets.
- Les parcours classiques ne sont pas proposés ni initialisables pour le type SFD. L'édition des sections SFD n'hérite pas de l'obligation `Technique` propre au projet classique.
- Structure SFD initiale : SPECS FONCTIONNELLES/{PROCESSUS METIERS,FONCTIONNALITES}, SPECS TECHNIQUES/{dmep,APPLICATIONS HOSTS,APPLICATIONS OPEN,APPLICATIONS LINUX}, DOCUMENTATION/liste des règles métiers.md, QUALIFICATION/TESTS, MANAGEMENT/{Réunions,Décisions,Jalons.md,Tâches.md}, ainsi que Présentation.md, Notes rapides.md et Glossaire.md.
- Le catalogue RM est unique. Aucune fiche PM/ACT/EXU/EXS/EXR/FCT/EMO, ni condition/règle fictive, n'est créée au démarrage.

## Limites et travaux suivants

Cette tranche **n'implémente pas encore** les objets métier et leur traçabilité, les templates EXU/EXS/EXR/EMO, les quatre prompts, le contrat d'import JSON, la qualification métier ou le parcours SFD dédié. Ils relèvent des #214 à #222. Le rangement primaire des EMO et la correspondance des anciens dossiers « cas d'usage » restent à arbitrer : **aucun sous-type canonique EMO n'est inféré**.

Les fichiers et services d'importation DOCX → Markdown restent interdits. Aucune donnée ou capture de projet professionnel n'a été ajoutée.

## Qualification

Test de fumée ajouté : `ProjectSfdSmokeTests`, appelé depuis `Nodalis.SmokeTests`. Vérifier aussi la compilation Release, l'analyse de style et la qualification WPF sur Windows avant toute fusion sur `main`.

## Tranche #214/#215 — fiches métier identifiées

- Rôles explicites `PM`, `ACT`, `EXU`, `EXS`, `EXR` et `FCT` ; références séquentielles par type et GUID indépendants des chemins.
- Métadonnées de fiche `nodalis-sfd-schema: 1` et `parent-id` dans le Markdown ; statut de certitude `ToConfirm` par défaut.
- Validation du parent : ACT appartient à un PM ; EXU/EXS/EXR appartiennent à une ACT. Un processus ou une fonctionnalité ne nécessite pas d'objet métier parent.
- FCT possède exactement un mode `WithInterface` ou `WithoutInterface`. Aucun sous-type EMO utilisateur/système/règlementaire n'est créé.
- La création par le menu **Projet → Nouvel objet SFD…** ouvre un formulaire revu par l'utilisateur. Les parents proviennent des vrais documents du projet, jamais de dossiers générés artificiellement.
- Les dossiers d'activités et collections d'exigences apparaissent à la demande. Un dossier PM possède `ACTIVITES`, une ACT possède sa fiche `presentation activité.md`, et chaque exigence a son propre fichier Markdown.
- Le modèle de chaque exigence contient le patron **confirmé** de condition EXU/EXS/EXR. Seule EXU reçoit le patron de sous-condition, avec `[CANAL À CONFIRMER]`. Les conditions et règles restent non matérialisées à ce stade.
- Le service de lecture signale les métadonnées invalides, doublons GUID et références, et parents introuvables sans réparer le projet silencieusement.
- Le périmètre de stockage des EMO, la création des blocs C/SC identifiés et les liens N:N ACT/FCT restent des étapes suivantes. Ces sujets ne sont **pas** assimilés à une structure déduite de chemins de dossiers.

Tests ajoutés dans `SfdAuthoringSmokeTests` : parcours PM → ACT → EXU/EXS/EXR, FCT avec/sans IHM, parse/format, renommage d'un dossier parent, absence de règles fictives, refus des parents invalides et des identifiants dupliqués.

## Tranche complémentaire #214/#216 — relations ACT↔FCT

La commande **Projet → Associer activités / fonctionnalités SFD…** édite les associations plusieurs-à-plusieurs des GUID métier. Le registre canonique `.sfd-activity-functionality.json` vit à la racine du projet SFD ; les fiches ACT ne sont jamais recopiées sous une FCT. L'enregistrement est conditionné à la révision SHA-256 lue et refuse les écritures concurrentes, les doublons et les objets hors projet. Toute association/dissociation est explicite ; les activités manuelles ou hors périmètre peuvent rester sans FCT. La projection en lecture dans les fiches Markdown et la traçabilité exigences↔EMO restent à faire.

Tests : activité associée à deux FCT, FCT associée à deux ACT, liens stables après renommage, idempotence, type de parent invalide et refus de révision périmée.

## Tranche #216 — blocs C/SC et catalogue RM canonique

- `SfdConditionBlockCodec` encode les conditions C et sous-conditions SC par **marqueurs GUID versionnés** dans chaque fiche d'exigence, avec un titre lisible (`C-0001` et `SC-0001`). Le parseur refuse doublons, GUID invalides, SC orphelines et conteneurs incohérents.
- Le fichier unique `DOCUMENTATION/liste des règles métiers.md` reçoit des RM identifiées (`RM-0001`, GUID, degré de certitude et source éventuelle). Seule cette fiche possède les formulations des RM ; chaque SC conserve 0..N **GUID de références**.
- `SfdStructuredAuthoringService` : création humaine de C, création de SC sous une C, ajout de RM au catalogue, liaison SC→RM. Les écritures suivent les révisions SHA-256, sans écraser les modifications externes.
- Les patrons confirmés de conditions EXU/EXS/EXR et de SC-EXU sont vérifiés ; aucun patron obligatoire n'est imposé pour les SC-EXS/EXR. Les textes à trous ne sont jamais acceptés comme conditions effectivement renseignées.
- `SfdCompletionReviewService` : projection **lecture seule** des brouillons sans C, C sans SC, SC sans RM, GUID RM introuvables, FCT sans ACT et EXR sans source réglementaire applicable. Une ACT sans FCT est **informative et autorisée**, pas une erreur ni un lien à fabriquer.
- Les RM encore à confirmer, non utilisées ou sans source sont visibles en diagnostics ; les constats de complétude ne changent jamais les degrés de fiabilité/validation.
- Test régression : deux SC (EXU + EXS) partagent un même GUID RM sans copier la définition, cas incomplets, patrons de texte et refus de règles supposées confirmées sans source.

Les EMO restent un rôle canonique unique, sans localisation primaire décidée. Leur ajout physique demeure différé jusqu'à l'arbitrage métier explicite.

## Interface de rédaction SFD — lot #216

Le menu **Projet → Conditions, sous-conditions et RM SFD…** donne accès à un éditeur WPF compact avec sélection d'une exigence EXU/EXS/EXR, de sa condition C, de sa sous-condition SC et d'une règle RM du catalogue partagé. Les commandes explicites permettent d'ajouter une C, d'ajouter une SC, de créer une RM avec source/certitude, et de lier ou dissocier une RM de la SC sans toucher aux autres références.

Le panneau de complétude affiche des diagnostics typés `Information`, `Incomplete` ou `Invalid` ; aucun constat ne crée de contenu ni ne valide un document. Les projets archivés sont accessibles en lecture seule.

Les éditeurs de blocs et de règles conservent les fins de lignes Windows CRLF, n'écrasent pas les modifications concurrentes et refusent les sections RM préexistantes sans identité : une conversion manuelle explicite sera nécessaire dans ce cas. Les indications « aucune condition/sous-condition » des gabarits sont retirées lorsqu'un vrai bloc est enregistré.

**Restes à qualifier :** rendu WPF réel (DPI/clavier/focus), extraction structurée d'une source EXR via un champ dédié, opérations de déplacement/suppression transactionnelles, enrichissement de la matrice métier et EMO après arbitrage de rangement. Aucun import DOCX n'est activé.

## Lot #217 — parcours SFD isolé

- Le modèle `SfdJourneyDefaults` propose **cinq étapes**, les quatre premières liées respectivement aux clés de prompts `sfd-01`, `sfd-02`, `sfd-03`, `sfd-04`, la cinquième étant transversale aux réunions, décisions, tâches, jalons et tests.
- Le `SfdJourneyService` utilise les mêmes modèles portables `ProjectJourneyDocument` et opérations pures `ProjectJourneyEditor` que le moteur 2.0, **mais un fichier distinct `.sfd-journey.json`**. Aucun parcours n'est créé à la consultation ; l'initialisation exige un choix humain explicite.
- Les statuts sont saisis explicitement et sont **indépendants de la complétude C/SC/RM**, des approbations et des résultats de tests. Les GUID d'étape/opération survivent aux renommages et déplacements.
- Le menu **Projet → Parcours SFD…**, la commande générale « Ouvrir le parcours » et l'accès Worktree redirigent les projets de type SFD vers le parcours dédié sans activer le parcours classique.
- Dans le dialogue SFD, possibilité d'ajouter une étape/opération, renommer, déplacer, déclarer le statut et **lier plusieurs fichiers Markdown/TXT du workspace en une action**, sans copier les fichiers ni passer par une importation DOCX. Les références sont contrôlées et la sauvegarde est conditionnée au SHA-256 de la dernière lecture.
- Le modèle affiche le nombre d'objets existants et les résultats de complétude en **lecture seule** ; aucun jalon ni FCT/EMO n'est créé par le changement d'étape.
- Les clés `sfd-01..04` sont **réservées à la préparation spécialisée** (#218/#219). Tant que la définition complète du contrat SFD n'est pas intégrée, ce dialogue ne permet **pas** de produire un prompt de réponse/import incomplet ou de déclencher une IA.

Tests : parcours proposé sans écriture, cinq étapes et quatre clés distinctes, initialisation explicite et idempotence par refus du doublon, GUID conservés au renommage, liens multisélection, révisions périmées, absence de migration des projets 2.0.

## Lot #218/#219 — préparation de prompts et contrat SFD strict

- `SfdImportContract.Schema = nodalis.sfd.import.v1` ; modèle `SfdImportEnvelope` indépendant de l'ancien `nodalis.import.v1`. Un **JSON Schema complet** (`SfdImportContract.JsonSchema`) est commun au parseur et aux quatre instructions externes. Chaque proposition emploie une clé temporaire `clientKey` : aucun GUID projet ou chemin ne peut être inventé par l'IA.
- `SfdImportContract.Parse` valide version, étape, rôles autorisés (01 PM/ACT ; 02 EXU/EXS/EXR/RM ; 03 FCT ; 04 EMO), types et clés, champs inconnus, propriétés JSON dupliquées, certitudes et patrons confirmés C/SC. Aucune réponse n'est appliquée au projet. Les mises à jour portant `targetId` nécessiteront la résolution locale et la revue transactionnelle dans #220.
- `SfdPromptPreparationService` prépare les quatre prompts en respectant les dépendances ; il lit uniquement les sources **explicitement liées** à l'opération depuis l'espace de travail et uniquement les formats Markdown/TXT. Chaque extrait est borné, identifié avec chemin relatif, clé temporaire et SHA-256. Sources manquantes, trop volumineuses, non UTF-8, DOCX et binaires sont ignorés et signalés.
- Le dialogue **Parcours SFD** dispose des actions **Préparer ce prompt…** et **Préparer les 4 prompts…**. Un aperçu complet dans une fenêtre scrollable précède le bouton explicite **Tout copier**. Aucun envoi réseau ni import automatique.
- Chaque prompt embarque le même schéma de sortie exact et distingue PM/ACT, besoins EXU/EXS/EXR, FCT avec/sans IHM et EMO canonique unique ; les réponses de chaque étape sont indépendantes et restent des propositions à contrôler.
- La préparation en lot est volontairement **un ensemble de prompts distincts**, et non une enveloppe JSON commune aux quatre étapes. Les prérequis manquants sont des avertissements, jamais des créations factices.
- Test régression : 40 sources fictives en une opération, absence de conversion DOCX, quatre contrats en lot, parser du schéma avec EXU sans IHM, FCT sans IHM, EMO canonique, ancienne version/approbation/type invalide refusés.

**Hors périmètre de ce lot :** import transactionnel et création réelle depuis une réponse externe (#220), localisation primaire des EMO (#214 à arbitrer), consolidation des sources inter-étapes. Ne jamais activer une voie DOCX→Markdown.

## Lot #220 — revue SFD locale avant publication

La commande **Projet → Analyser une réponse IA SFD…** permet de coller un JSON `nodalis.sfd.import.v1` et de comparer les propositions par GUID, jamais par titre ou chemin supposé. La revue affiche les candidats de création, mise à jour ou liaison, les brouillons incomplets, références inconnues, collisions et EMO différées. Elle calcule un SHA-256 de la réponse, du plan et des sources existantes, y compris le catalogue partagé RM.

La fenêtre n'enregistre ni la réponse ni les choix, et ne possède actuellement **aucune commande « Appliquer »** : toute écriture nécessite encore le moteur transactionnel et la revue champ par champ de #220. Les noms/chemins d'objets ne sont pas attribués par une IA, les autres projets sont exclus, les EMO restent différées et aucun import DOCX n'est réactivé.

Tests synthétiques : PM/ACT candidats non matérialisés, mise à jour par GUID et empreinte, isolation inter-projets, titre similaire sans rapprochement automatique, EXU/EXR incomplètes, parent non résolu, RM existante par GUID, FCT sans IHM liée à ACT, EMO différée et invalidation après changement du document source.

## Lot pilote #220 — publication contrôlée et récupération d'un PM

- Le dialogue **Projet → Analyser une réponse IA SFD…** affiche désormais la description, les références de sources et les comptes C/SC/RM. Seule une proposition `businessProcess/create` au statut `NewCandidate` peut afficher **Publier ce PM…**, et seulement si le projet est éditable.
- Un clic demande une **confirmation humaine** qui rappelle le titre, la description et le périmètre exact : **un seul PM**. Aucune proposition ACT, exigence, FCT ou EMO n'est publiée dans cette tranche.
- `SfdApprovedProcessImportService` relit la réponse brute et le plan, vérifie projet/GUID/version/SHA, attribue un nouvel ID et la référence PM dans Nodalis, et enregistre une **intention durable** `.sfd-ai-import/<fingerprint>/intent.json` avant toute création documentaire.
- Le fichier Markdown approuvé est stagé en dehors des dossiers indexés, puis déplacé vers `SPECS FONCTIONNELLES/PROCESSUS METIERS`. Rejouer explicitement la même réponse + `clientKey` conserve l'identité et renvoie « déjà appliqué » ; une écriture manuelle postérieure provoque un conflit, sans écrasement.
- Les descriptions provenant d'une IA restent marquées **à qualifier** et le `SfdDocumentCard.Certainty` initial reste `ToConfirm` ; l'IA ne peut pas valider une obligation ou fabriquer des activités.
- Les tests `SfdApprovedProcessImportSmokeTests` vérifient la création isolée, l'idempotence, le journal antérieur aux écritures, la reprise après interruption simulée, le refus après modification humaine et le blocage des types de projets incompatibles.

**Reste dans #220** : révision champ par champ et rapprochement base/current/proposed, création atomique de lots mixtes PM→ACT→EXU/EXS/EXR→FCT, contenu C/SC/RM, liens N:N et mapping durable inter-lots. L'arbitrage de rangement des EMO est toujours ouvert. Le ban DOCX→Markdown reste intact.
