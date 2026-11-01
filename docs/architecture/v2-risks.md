# Nodalis 2.0 — risques canoniques (EPIC #199)

Statut : **développement incrémental sur `develop`**, sans release 2.0.
Identité portable, création des fiches RIS, évaluations humaines historisées,
qualification métier indépendante, plans de traitement versionnés,
suivi humain des mesures et registre filtrable sont implémentés. Les
liens métier avec des tâches à identité persistante, la collecte formalisée
des preuves et la clôture guidée restent à développer.

## 1. Accès et emplacement

Ouvrir le **Parcours** d'un projet, puis **Risques…** dans la barre d'actions.
Le registre sombre est une **vue ponctuelle du projet** (pas un rescannage
du workspace à chaque modification WPF). Il affiche les fichiers structurés
de `Analyse/Risques`. Les filtres « sans évaluation initiale »,
« résiduel non évalué », criticité (initiale **ou** résiduelle),
surveillance, clôture et qualification à faire sont **purement mémoire**.
La recherche par code RIS/titre ne déclenche aucun accès disque. Les projets Complexes récents possèdent déjà
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

**Limitations actuelles importantes :** les évaluations humaines,
la qualification métier et leurs historiques append-only sont intégrés,
mais **la liaison sûre des mesures aux tâches/incidents/réserves Gates,
la formalisation de leurs preuves, les synthèses avancées et la clôture guidée**
restent à développer. Les avis sont audités dans le Markdown, sans
signature cryptographique extérieure.

## 4. Criticité : qualification humaine et réévaluation

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

## 5. Qualification métier indépendante (incrément suivant)

Le registre **Risques…** propose une nouvelle action **« Décider du
statut… »**. Elle est entièrement distincte des commandes de criticité
initiale et résiduelle. Le décideur choisit explicitement :

- **À qualifier** (`ToReview`) : risque en attente de décision métier,
  y compris après une **requalification motivée** ;
- **Validé** (`Validated`) : le risque est reconnu comme pertinent,
  sans présumer son niveau de criticité ni une mitigation terminée ;
- **Rejeté** (`Rejected`) : le risque est écarté au terme d'une
  décision humaine, sans suppression de fiche ni altération des scores.

Chaque changement exige un **nouveau statut différent**, un
**décideur identifié**, une **justification d'au moins 10 caractères**
et une **confirmation finale** résumant ancien statut, nouveau statut,
acteur et motif. L'utilisateur peut revenir à « À qualifier » après
validation/rejet en créant une *nouvelle décision*, jamais en effaçant
une décision antérieure. La fenêtre permet de relire toutes les
transitions, leurs dates, auteurs et raisons.

### Persistance des deux chronologies indépendantes

Le fichier `RIS-xxxx.md` conserve deux journaux distincts :

1. `<!-- nodalis:risk-qualification:start -->` jusqu'à
   `<!-- nodalis:risk-qualification:end -->` : une ligne de commentaire
   JSON par décision métier avec ancien état, nouvel état, GUID stable,
   auteur, raison et date UTC ;
2. `<!-- nodalis:risk-history:start -->` jusqu'à
   `<!-- nodalis:risk-history:end -->` : évaluations initiales et
   résiduelles de la matrice 1–3 × 1–3.

Lorsqu'ils coexistent, le **journal métier précède le journal de
criticité**, qui reste dernier. Les éléments de contexte Markdown
restent libres **avant ces blocs**.

Le service relit les deux journaux pour vérifier que le
`qualification:` du YAML correspond à la dernière décision, et
que les scores sont cohérents avec les avis de criticité.
Une qualification `Validated` ou `Rejected` inscrite à la main
sans événement correspondant n'est **pas fiable** : le document est
signalé dans les diagnostics plutôt que validé silencieusement.
Les transitions dupliquées, états inconnus, anciennes valeurs
incohérentes, GUIDs invalides et fragments d'historique sont rejetés.

Tout nouveau choix passe par la même vérification du **projet
propriétaire, GUID du risque, dossier canonique et SHA-256 exact**
que l'évaluation de criticité. Les projets archivés sont
consultables mais les commandes d'écriture sont refusées. La
modification du statut métier ne touche **ni l'initiale, ni la
résiduelle, ni le cycle de vie, ni les tâches, ni les incidents,
ni les Gates**. Aucun état n'est déduit par une IA.

### Qualification manuelle à effectuer

Dans un projet de test :

1. créer une fiche RIS et confirmer qu'elle est `ToReview` et
   `Unassessed` pour les deux criticités ;
2. valider le statut métier **sans** saisir de score ; le score doit
   rester non évalué ;
3. qualifier ensuite l'initiale à 3×3 puis changer le statut en
   `Rejected` ; l'initiale doit rester à 9 et son historique intact ;
4. réévaluer la résiduelle à 1×2, puis revenir à `ToReview`
   par décision motivée ; les deux journaux doivent rester visibles ;
5. tester Annuler, recharger après une modification externe
   du Markdown, et ouvrir la fiche dans l'éditeur ;
6. vérifier les archives en lecture seule, DPI élevé, clavier,
   petite fenêtre et absence de saut du Worktree.

## 6. Plans de traitement humains et versions immuables (incrément #199)

Depuis **Parcours → Risques…**, sélectionner un risque et ouvrir
**« Plan de traitement… »**. Le formulaire ne déduit aucune stratégie
des scores : une personne choisit **Éviter, Réduire, Transférer ou
Accepter**, indique le **responsable du traitement** et complète trois
rubriques distinctes :

- **Prévention / probabilité** : moyens destinés à éviter le déclenchement.
- **Réduction / transfert** : moyens de limiter ou transférer les conséquences.
- **Contingence** : procédure applicable si le risque survient.

Les trois rubriques sont toujours requises (8 caractères minimum). Si une
mesure ne s'applique pas, l'auteur l'indique et l'explique explicitement,
plutôt que Nodalis invente une stratégie. L'utilisateur fournit ensuite
l'**auteur de la revue** et la **justification de l'approbation ou de la
révision** (10 caractères minimum), puis confirme une dernière fois la
stratégie, le responsable et les mesures. Annuler ne modifie rien.

Un risque neuf n'a **aucun plan implicite**. Une nouvelle proposition
sauvegarde un **événement de plan** avec GUID d'avis, GUID canonique du
risque, stratégie, responsable, mesures, acteur, motif et date UTC.
Une révision se fait par le même formulaire prérempli, **uniquement avec
un contenu changé**, et ajoute une entrée ; l'ancien plan reste
consultable avec son identité. `PreviousId` relie les révisions
successives.

### Persistance et sécurité

Le fichier `RIS-xxxx.md` contient un troisième journal :
`<!-- nodalis:risk-treatment:start -->` jusqu'à
`<!-- nodalis:risk-treatment:end -->`. Il précède le journal de
qualification métier, lui-même avant le journal de criticité lorsqu'ils
sont tous présents. Ce format a été vérifié pour les plans créés
**avant ou après** les deux autres types d'avis.

L'en-tête possède le champ optionnel `treatment-head-id` : l'ID de
la **dernière révision**. Un risque sans traitement porte `null` ou,
pour une ancienne fiche v2 non migrée, ne possède pas ce champ. Le
premier plan approuvé ajoute ce champ sur les anciennes fiches ;
**la simple lecture ne migre rien**. Le replay du journal doit
retrouver exactement la tête annoncée, les `PreviousId`, le projet
et l'ordre temporel. Une suppression de la dernière entrée sans mise
à jour de l'en-tête, une rupture de chaîne ou une source ambiguë sont
rejetées, sans réparation silencieuse.

La commande `ProjectRiskRegistryService.PlanTreatmentAsync` réutilise
la vérification **GUID de projet/risque, chemin canonique, unicité,
SHA-256 affiché**, et la sauvegarde atomique avec contrôle de révision.
Elle ne change **ni la qualification métier, ni la criticité initiale,
ni la criticité résiduelle, ni le cycle de vie**, ni les tâches,
incidents, gates et jalons. L'archive est consultable, mais aucun plan
ne peut y être modifié.

Ces journaux garantissent le respect des invariants par les commandes
Nodalis ; comme pour les autres avis, ils ne remplacent pas une
signature cryptographique externe contre une personne qui manipule
directement tous les champs d'un fichier Markdown.

### Qualification manuelle du plan

1. Créer un RIS puis enregistrer une stratégie `Reduce` avec un
   responsable, trois mesures et une justification.
2. Qualifier le statut métier et la première criticité, puis **réviser**
   le plan en `Transfer` ; vérifier que les deux versions restent
   consultables et que la criticité/qualification ne changent pas.
3. Réévaluer la résiduelle puis revenir à `ToReview` : les **trois
   journaux** doivent rester lisibles et le plan courant inchangé.
4. Essayer une révision identique ou avec une mesure vide : vérifier
   le refus et l'absence d'écriture.
5. Modifier une note Markdown hors journal pendant que le formulaire
   est ouvert : vérifier le rejet SHA, la conservation de la note et
   la nécessité de recharger le registre.
6. Vérifier le mode archive, les écrans étroits, le DPI, la navigation
   clavier, les documents ouverts et l'absence de gel du Worktree.

**Non couvert à ce stade :** lier les mesures à des tâches stables,
attester leur exécution, affecter des preuves et fermer le risque
selon une procédure guidée. Ce sont les incréments suivants, pas
des automatismes cachés derrière la validation d'un plan.

## 7. Avancement humain des trois mesures (incrément suivant)

Une fiche RIS avec un plan de traitement possède trois **rubriques
de mesure** : prévention, réduction/transfert, contingence. Le nouveau bouton
**« Suivi des mesures… »** apparaît seulement si un plan existe. La personne
choisit explicitement une rubrique, puis déclare l'état **En attente,
En cours, Bloquée ou Terminée** avec un auteur et une motivation. Déclarer
**Terminée** exige également une **preuve textuelle motivée** d'au moins
10 caractères (référence de PV, explication de vérification, etc.).
Cette preuve est une *déclaration humaine*, pas un document authentifié
automatiquement. Rien n'est déduit d'une case Markdown cochée, d'un
incident terminé ou d'une réévaluation de criticité. Une mesure peut être
rouverte par une nouvelle déclaration, sans effacer la précédente.

### Identité stable et révision de plan

Chaque événement de mesure possède son GUID, celui du risque et surtout
le **GUID immuable de la version de plan** dans laquelle la mesure a été
définie. La paire `(version de plan, rubrique)` identifie une mesure
même si le nom du risque ou sa fiche est déplacé.

- Une nouvelle version de traitement **ne copie pas** les états `Completed`
  de l'ancienne version : les trois rubriques repartent en `Pending`.
- Les états de l'ancienne version et leurs preuves restent dans
  l'historique, consultables.
- Deux rubriques du même plan progressent indépendamment ; terminer la
  prévention ne termine pas le plan de contingence.
- Les plans, décisions métier et évaluations initiales/résiduelles restent
  **inchangés** lorsque la progression d'une mesure évolue.

L'historique est porté dans le même Markdown canonique, dans
`<!-- nodalis:risk-measures:start -->` …
`<!-- nodalis:risk-measures:end -->` **avant le journal de traitement**.
La première ligne du journal porte un `risk-measures-head` pointant
vers le dernier événement ; chaque événement possède également un
`PreviousId` global et un état antérieur attendu. Cela détecte
un événement manquant, y compris la suppression du dernier événement,
et vérifie les changements de statut *dans la même version de plan*.

### Vérifications et restrictions

La commande `UpdateMeasureAsync` revalide le projet propriétaire,
le chemin canonique, la source et le **SHA-256 précis** ayant été
présenté à l'utilisateur. Si le Markdown a changé depuis l'ouverture
du formulaire, la commande refuse la modification et exige une
relecture. Le service refuse les projets archivés et les GUID
étrangers, toute mesure sans plan, et les preuves manquantes lors
d'une déclaration `Completed`.

**Relations explicites vers les tâches :** le moteur Markdown
dispose d'un **GUID permanent facultatif** de checkbox
(`TaskItem.PersistentId`), créé sur une nouvelle tâche ou attribué
explicitement à une ancienne tâche. Les anciens `TaskItem.Id` sans
`PersistentId` restent dérivés de l'occurrence et **ne sont jamais
utilisés comme clés relationnelles**.

Depuis **Risques… → Tâches liées…**, un opérateur peut désormais
**lier ou délier** une tâche au quadruplet
`(risque GUID, version plan GUID, rubrique, tâche PersistentId)`.
Le service vérifie les collisions, l'appartenance au projet et
la présence physique du marqueur dans la source avant une création.
Les liens historiques survivent aux changements de plan et la
dissociation d'un GUID orphelin exige un choix humain. Voir
[contrat des identités de tâches](v2-task-identities.md) et
[relations typées RIS/tâches](v2-risk-task-links.md).
Une mesure terminée ne coche ni ne ferme aucune tâche.

**Recette visuelle à réaliser :** créer un plan ; déclarer la prévention
`InProgress`, puis `Completed` avec une preuve ; constater que les deux
autres rubriques restent `Pending`. Réviser le plan ; constater que le
nouveau plan possède trois états `Pending`, avec maintien de
l'historique de l'ancienne version. Tester les actions Annuler,
les champs incomplets, un refus de fichier modifié depuis le
chargement, le mode archive en lecture seule, le clavier et le DPI.

## 8. Sécurité et performances

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

## 9. Tests automatiques et recette Windows

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

La suite `ProjectRiskQualificationSmokeTests` vérifie la séparation
stricte des deux journaux, toutes les transitions métier, les
modifications externes, les tentatives de réécriture du YAML, les
états incohérents et le refus d'un projet tiers.

La suite `ProjectRiskTreatmentSmokeTests` couvre les quatre
stratégies, l'obligation des mesures, la chaîne des révisions GUID,
la validation de `treatment-head-id`, les créations de plans avant et
après les autres journaux, les conflits de sources et les tentatives
d'altération d'un historique.

La suite `ProjectRiskMeasureSmokeTests` couvre notamment la preuve
obligatoire pour `Completed`, les quatre états, les trois rubriques,
la chaîne des événements, les versions successives du plan, les
révisions concurrentes, les périmètres de projets et le diagnostic
sans migration des journaux altérés.

Recette visuelle **encore à faire** : risque créé puis rouvert,
onglets Markdown existants intacts, fenêtre étroite, DPI élevé, clavier,
projet Ultra-simple, archives en lecture seule et plusieurs milliers
de fiches sans gel ni saut de Worktree.

**Gates #202 et réserves :** ne pas fabriquer un lien vers un simple
`Risques.md` ou une ligne de tableau. Le rattachement à une réserve
attendra l'usage fiable du `risk-id` de fiche v2 et la qualification
des événements de relation. Aucun merge vers `main`, tag ni release 2.0.
