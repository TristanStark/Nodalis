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
