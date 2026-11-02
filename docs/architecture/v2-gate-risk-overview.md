# Nodalis 2.0 — synthèse transversale réserves et RIS

**État :** incrément réalisé sur `develop`, qualification visuelle
Windows non encore effectuée. La synthèse n'est pas un moteur de
décision GO/NO-GO ou d'achèvement automatique des mesures.

## Accès et lecture

Dans **Parcours du projet → Gates → Synthèse réserves / risques…**,
Nodalis lit **une fois par commande utilisateur** le fichier
`.project-gates.json` via `ProjectGateRegistryService.ReadAsync`,
puis le registre des fiches structurées RIS
`Analyse/Risques/RIS-xxxx.md` via
`ProjectRiskRegistryService.ListAsync`.

Le composant
`ProjectGateRiskOverviewBuilder.Build(registry, catalog, today)`
effectue un replay en mémoire de `RiskLinkEvents`, un
rapprochement par `(DecisionId, ReservationId, RiskId)`, puis
une projection des réserves et risques par GUID. Il n'effectue
aucune lecture/écriture sur disque lui-même. La fenêtre WPF
ne relit aucun document pendant les filtres, recherches,
changements de sélection ou le scroll.

Le tri suit les conventions de la vue réserves existante :
d'abord les ouvertes, en retard et à échéance proche, puis les
levées, sans réécriture des décisions historiques.

## Indicateurs et filtres

La synthèse donne les comptes suivants :

- **Ouvertes / levées :** états des événements humains distincts
  du verdict initial de la décision Gate.
- **Ouvertes sans RIS :** réserves n'ayant **aucun lien actif**
  vers un risque (un ancien lien dissocié n'est pas actif).
- **Ouvertes avec RIS orphelin :** au moins un GUID de risque
  encore lié mais sans fiche canonique et unique dans le catalogue.
  Cela comprend une fiche supprimée, malformée ou dupliquée.
  Un GUID orphelin reste visible mais **n'a pas de chemin
  Markdown inventé**.
- **Ouvertes avec mesure bloquée :** au moins une des trois
  mesures du **plan de traitement courant** d'un RIS résolu
  possède le statut `Blocked` déclaré par une personne.
- **Ouvertes avec évaluation absente :** au moins un RIS
  lié et résolu possède une criticité initiale ou résiduelle
  `Unassessed`. Une valeur inconnue ne signifie pas faible.
- **Ouvertes avec mesure non terminée :** au moins une mesure
  du plan actuel est `Pending`, `InProgress` ou `Blocked`
  selon le journal de suivi humain. L'absence complète de
  plan apparaît explicitement « aucun plan », et non comme
  trois mesures en échec.

La recherche inclut description de réserve, Gate, responsable,
référence RIS, titre RIS et GUID orphelin. Les détails d'une
fiche affichent la qualification métier, les criticités,
les mesures bloquées/non terminées et le nombre de **GUID
de tâches liés au plan courant**. Ce nombre n'indique pas
que les tâches existent encore, ont été cochées ou qualifiées.
Leur provenance se vérifie dans les vues de tâches dédiées.

Les dissociations historiques ont un compteur d'événements ;
elles ne sont pas réactivées lorsque le RIS réapparaît.

## Navigation et garanties

- **Ouvrir le dossier Gate** retourne les GUID du Gate,
  de la décision initiale et de la réserve, puis sélectionne
  exactement ce dossier dans le registre habituel.
- **Ouvrir la fiche RIS** retourne uniquement
  `ProjectRiskEntry.RelativePath` d'une fiche présente et
  non ambiguë ; l'ouverture passe par le parcours sécurisé
  du workspace. Le bouton reste indisponible sur un GUID
  orphelin ou ambigu.
- En cas de modification externe entre le catalogue et
  l'ouverture, le système d'ouverture des sources doit
  refaire son contrôle habituel de confinement et disponibilité.
- La consultation ne modifie jamais un registre Gate, un
  Markdown RIS, une tâche, une mesure, une réserve ni une
  autorisation de déploiement. Elle n'infère jamais un GO,
  une levée de réserve ou une clôture automatique.

Le calcul est un rapprochement indexé des historiques validés.
Il n'actualise pas implicitement le Worktree et reste limité
au projet choisi : une autre application ou un autre projet
n'est jamais agrégé.

## Tests et qualification manuelle

`ProjectGateRiskOverviewSmokeTests` crée deux projets réels,
trois réserves d'une décision GO sous réserves, des RIS
canoniques, un plan de traitement avec mesure bloquée et
un GUID de tâche explicitement lié. Il vérifie les comptes
ouverts/levés, les RIS sans liens, les risques non évalués,
les sources supprimées ou dupliquées, la désassociation
ciblée et l'absence de mutation des sources Gate/RIS/tâche
entre deux lectures. Le verdict historique reste figé.

**Recette Windows encore requise :** fenêtre étroite, taille
et DPI élevés, navigation clavier et double-clic, plusieurs
documents déjà ouverts, source RIS supprimée ou déplacée,
projet archivé en lecture seule, filtres successifs sur
plusieurs centaines de réserves, et absence de saut ou gel
du Worktree. Confirmer aussi les libellés des évaluations
non qualifiées et le comportement d'un GUID orphelin.

**Suite envisagée :** vérification sur demande des GUID de
tâches et des preuves, diagnostic des dépendances par Gate,
et intégration aux étapes MEP sous contrôle humain. Aucune
validation automatique, merge, tag ou release 2.0.


## Provenance des tâches liées et des preuves de mesures (02/11/2026)

La synthèse Réserves ↔ risques résout désormais, **à l'ouverture explicite
de la fenêtre**, les GUID de tâches du plan de traitement RIS courant.
Elle effectue une seule lecture du catalogue complet du workspace puis
indexe les identifiants en mémoire ; les filtres et sélections WPF ne
déclenchent **aucune nouvelle lecture de fichier**.

Pour chaque GUID lié, le statut est distingué :

- **Source unique dans le projet** : référence retrouvée dans le catalogue
  à la ligne identifiée, checkbox ouverte ou cochée affichée séparément.
- **Source absente** : GUID sans checkbox source résolue dans le workspace.
- **Source ambiguë** : même GUID dans plusieurs documents, ou plusieurs
  fois dans un même document. Un ensemble typé de GUID en conflit est
  conservé par le catalogue, même si les tâches concernées ont perdu leur
  PersistentId exploitable pour empêcher une liaison dangereuse.
- **Autre projet** ou **source non vérifiable** : jamais présentées comme
  appartenant implicitement au projet de la réservation.
- **Non inspectée** : lorsque le constructeur est appelé sans catalogue
  de tâches ; ce n'est pas un diagnostic de disparition.

Les chemins et numéros de ligne sont des **instantanés de lecture**.
L'ouverture/édition ultérieure doit revalider le chemin, l'identité et
la version source selon les règles ordinaires de l'éditeur. Aucun
chemin n'est inventé pour une relation orpheline.

**La checkbox cochée n'est jamais une qualification de mesure.** Les
mesures terminées résultent exclusivement du dernier événement humain
de leur rubrique, sur la *version de plan actuelle*. Les événements des
anciennes versions restent historiques ; ni leurs liens de tâches,
ni leurs validations ne sont propagés. La « preuve » associée à la
mesure est actuellement une **description textuelle déclarée** :
la synthèse affiche combien de mesures terminées ont une telle
déclaration, sans prétendre en vérifier l'existence, le contenu ni
l'empreinte SHA-256. Cela n'affecte pas la capture de preuves figées
du registre Gate.

Un compteur et un filtre permettent de repérer les **réserves ouvertes
dont un RIS contient une référence de tâche absente, ambiguë ou hors
projet**. En sélectionnant un RIS, les GUID et leurs états sont consultables.
Aucune association, case, mesure, réserve, décision ou autorisation
de déploiement n'est modifiée par la consultation.

Régressions ajoutées : source unique non cochée, checkbox cochée sans
transition de mesure bloquée, collision de GUID entre projets, correction
de collision, déclaration humaine explicite avec justification, disparition
de source après qualification. **Recette visuelle Windows restante** :
plusieurs onglets, changement de filtre, DPI 150–200 %, sélection de
RIS, grand workspace et aucune oscillation du Worktree.
