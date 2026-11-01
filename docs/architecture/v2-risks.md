# Nodalis 2.0 — risques canoniques (EPIC #199)

Statut : **premier incrément fonctionnel sur `develop`**, sans release 2.0.
Ce lot pose l'identité durable des risques et la création explicite des fiches,
sans prétendre fournir déjà tout le cycle de qualification, mitigation et
réévaluation prévu par #199.

## 1. Accès et emplacement

Ouvrir le **Parcours** d'un projet, puis **Risques…** dans la barre d'actions.
Le registre sombre est une **vue ponctuelle du projet** (pas un rescannage
du workspace à chaque modification WPF). Il affiche les fichiers structurés
de `Analyse/Risques`. Les projets Complexes récents possèdent déjà
`Analyse/Risques/Risques.md` : c'est un **ancien tableau conservé**, et
non un risque individu automatiquement importé.

La lecture n'écrit aucun fichier et ne crée pas le dossier quand il est
absent. Les fichiers anciens ou non structurés restent intacts et sont
signalés comme documents ignorés. En présence d'un GUID ou d'une
référence dupliqué(e), toutes les fiches ambiguës sont écartées et
signalées ; aucune réattribution automatique d'identité.

## 2. Création d'une fiche RIS

**Nouveau risque…** demande un titre, puis une confirmation explicite
qui prévisualise la future référence. Après approbation, Nodalis écrit un
nouveau fichier `RIS-0001.md`, `RIS-0002.md`, etc. dans
`Analyse/Risques`. Le numéro attribué tient compte des fichiers
déjà présents, y compris un ancien fichier nommé `RIS-0001.md`,
pour éviter tout écrasement. Deux créations concurrentes utilisant
le même numéro provoquent une **collision explicite** ; aucun overwrite
ni nouveau numéro choisi silencieusement.

Un nouveau risque possède :
- `risk-id` : GUID stable, indépendant du nom du fichier et de son code ;
- `project-id` : GUID canonique du projet propriétaire ;
- `reference` : code utilisateur `RIS-0001` à `RIS-9999` ;
- un titre, une date UTC, une qualification `ToReview` et un
  cycle de vie `Open` ;
- deux évaluations **indépendantes** : `initial` et `residual`,
  toutes deux **non évaluées** (`null/null`) à la création.

Le dossier est créé **uniquement sur action utilisateur confirmée** et
uniquement dans un projet actif. Une archive est consultable mais non
éditable. Le document reste du Markdown libre pour le contexte,
les mesures et le plan de contingence. **Ouvrir la fiche** réutilise
l'éditeur Markdown existant, sans créer de faux onglets de Parcours.

## 3. Contrat des scores 1–3 × 1–3

Le moteur Core `ProjectRiskMatrix.Evaluate` utilise :
- `null/null` → **Unassessed**, jamais « faible » ;
- `1–2` → **Low** ;
- `3–4` → **Moderate** ;
- `6–9` → **High**.

Une seule valeur présente ou une valeur hors `1..3` est invalide ;
une lecture n'invente pas le second score. Les composantes initiale et
résiduelle n'interfèrent pas : finir une tâche ne peut pas abaisser
un score, fermer un incident ne valide pas un risque.

**Limitations actuelles importantes :** le formulaire WPF permet la
création et la consultation, mais pas encore une **revue humaine
historisée** des scores. La première évaluation validée, l'immutabilité
de cette évaluation, les réévaluations motivées, les mesures,
les rattachements aux réserves/tâches/incidents, les synthèses et la
clôture guidée restent des **tranches à développer**. La présence de
valeurs dans la fiche Markdown ne constitue pas un dossier de
qualification signé.

## 4. Sécurité et performances

- Vérification du manifeste `.project.json` et de l'appartenance du
  projet au workspace, sans traversée hors racine ou lien symbolique.
- Lecture de **`Analyse/Risques` uniquement**, non récursive,
  maximum 5 000 fichiers Markdown et 1 Mio par fiche.
- Rejet des identifiants étrangers au projet, des métadonnées
  manquantes/dupliquées, des valeurs invalides et de la création en archive.
- Nouvelle fiche créée par écriture atomique **sans remplacement** de
  fichier existant. Les sources historiques ne sont pas migrées.
- Catalogue recalculé seulement via **Recharger** ou après création.
  Aucune IA ni réseau nécessaire.

## 5. Tests automatiques et recette Windows

`ProjectRiskRegistrySmokeTests`, branché dans `Program.cs`,
vérifie la lecture non destructive d'un projet Complexe comprenant
déjà un tableau de risques, le refus d'injection dans le titre,
la matrice (dont `Unassessed`), la création guidée, les GUID stables,
la double création, les collisions, les risques d'un autre projet,
les en-têtes dupliqués et le refus des chemins extérieurs.

Recette visuelle **encore à faire** : risque créé puis rouvert,
onglets Markdown existants intacts, fenêtre étroite, DPI élevé, clavier,
projet Ultra-simple, archives en lecture seule et plusieurs milliers
de fiches sans gel ni saut de Worktree.

**Gates #202 et réserves :** ne pas fabriquer un lien vers un simple
`Risques.md` ou une ligne de tableau. Le rattachement à une réserve
attendra l'usage fiable du `risk-id` de fiche v2 et la qualification
des événements de relation. Aucun merge vers `main`, tag ni release 2.0.
