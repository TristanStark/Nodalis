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

**Limitations actuelles importantes :** les évaluations humaine
initiale/résiduelle et l'historique append-only sont désormais
intégrés, mais **les décisions de qualification métier, les mesures,
les rattachements aux réserves/tâches/incidents, les synthèses et
la clôture guidée** restent à développer. Les avis sont audités dans
le Markdown, sans signature cryptographique extérieure.

## 4. Qualification humaine et réévaluation (incrément suivant)

Le bouton **Qualifier l'initiale…** apparaît sur une fiche RIS non
encore évaluée. La personne choisit explicitement **probabilité et
impact 1..3**, puis renseigne son nom et une justification motivée.
Le dialogue prévisualise le score et demande **une seconde
confirmation** avant l'écriture. La première évaluation ainsi
validée devient **figée** : impossible de la remplacer dans Nodalis.

Après cet avis initial, le bouton **Réévaluer la résiduelle…** permet
autant de qualifications humaines successives que le contrat
d'historique le permet (maximum 256, dont la première évaluation).
Chaque nouvelle évaluation est indépendante et n'altère jamais
l'initiale. La variation du score ne découle d'aucune tâche ou
incident clos. Le cycle de vie et la qualification métier restent
deux dimensions indépendantes : approuver une criticité ne clôt ni
ne valide automatiquement tout le risque.

**Persistance :** la fiche Markdown reste l'unique source canonique.
Nodalis ne change que les deux champs YAML correspondant à
l'évaluation approuvée ; un journal de commentaires HTML à la fin
du fichier contient les événements JSON avec GUID d'avis, GUID
du risque, type d'avis, valeurs, auteur, motif et horodatage UTC.
L'historique est **append-only au niveau de l'interface et du
service**. La lecture recalcule les scores à partir des événements
et refuse un YAML incohérent, une chronologie invalide, un doublon
ou une première évaluation modifiée. L'utilisateur peut continuer
à modifier librement les sections de contexte et de mesures **avant
le bloc historique**.

La sauvegarde exige le **SHA-256 exact** de la fiche affichée,
vérifie à nouveau l'appartenance au projet et son GUID, puis écrit
atomiquement sous contrôle de révision via
`TextDocumentSession`. Une modification externe nécessite une
relecture explicite ; aucun écrasement ni fusion silencieuse.
Un risque au YAML évalué mais sans avis historisé est signalé
comme non qualifiable, jamais présenté comme validé.

> Limite de confiance : ce journal Markdown n'est pas une
> signature cryptographique extérieure. Un utilisateur qui édite
> directement les fichiers peut les falsifier intentionnellement.
> Nodalis protège les modifications réalisées par ses commandes et
> détecte les incohérences structurelles, mais ne garantit pas une
> preuve d'authenticité contre un éditeur de fichiers malveillant.

## 5. Sécurité et performances

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

## 6. Tests automatiques et recette Windows

`ProjectRiskRegistrySmokeTests`, branché dans `Program.cs`,
vérifie la lecture non destructive d'un projet Complexe comprenant
déjà un tableau de risques, le refus d'injection dans le titre,
la matrice (dont `Unassessed`), la création guidée, les GUID stables,
la double création, les collisions, les risques d'un autre projet,
les en-têtes dupliqués et le refus des chemins extérieurs.

La suite `ProjectRiskReviewSmokeTests` vérifie également la
première évaluation figée, les réévaluations résiduelles avec
horodatage et auteur, la conservation des commentaires libres,
les modifications concurrentes, le refus des évaluations
non historisées et le filtrage des sources d'un autre projet.

Recette visuelle **encore à faire** : risque créé puis rouvert,
onglets Markdown existants intacts, fenêtre étroite, DPI élevé, clavier,
projet Ultra-simple, archives en lecture seule et plusieurs milliers
de fiches sans gel ni saut de Worktree.

**Gates #202 et réserves :** ne pas fabriquer un lien vers un simple
`Risques.md` ou une ligne de tableau. Le rattachement à une réserve
attendra l'usage fiable du `risk-id` de fiche v2 et la qualification
des événements de relation. Aucun merge vers `main`, tag ni release 2.0.
