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
