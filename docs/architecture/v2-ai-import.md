# Nodalis 2.0 — import de réponses IA externes (premier contrat)

Issue #197, dépendances #193 et #196. **Ce contrat ne modifie aucun projet**.
`nodalis.import.v1` est une enveloppe JSON de PROPOSITIONS et non un
protocole d'ordres exécutables.

## Format minimal

```json
{
  "schema": "nodalis.import.v1",
  "responseId": "aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa",
  "promptKey": "frt100-01",
  "proposals": [
    {
      "proposalId": "bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb",
      "kind": "requirement",
      "operation": "create",
      "certainty": "explicit",
      "sourcePath": "Applications/Exemple/Projets/Fictif/Notes.md",
      "proposedFields": {
        "title": "Exemple fictif",
        "description": "Une exigence à contrôler manuellement."
      }
    }
  ]
}
```

Les clés de propriétés sont en camelCase, les valeurs des champs proposés
sont des **chaînes**. `kind` : requirement, criterion, risk, test, task,
decision ; `operation` : create, update ou link ; `certainty` : explicit,
derived ou hypothesis. Pour une mise à jour, `baseFields` optionnel
contient les valeurs effectivement vues à la préparation du prompt.
`targetId` et `candidateReference` ne sont que des indications à
résoudre par le moteur Core sous contrôle de l'utilisateur.

Le lecteur accepte le JSON brut ou **un unique bloc** ```json entourant
toute la réponse. Limites : 1 Mio, 100 propositions, 30 champs proposés par
objet, 8000 caractères par valeur, profondeur JSON 32. Identités vides,
doublons de clés (insensibles à la casse), traversals de chemins,
types inconnus, actions `delete` et champs d'identité/approbation/statut
sont rejetés. SHA-256 calculé sur la réponse brute, sans persistance.

`ProjectAiThreeWayDiff.Compare` classe chaque champ en candidature à
examiner, déjà présent, conflit concurrent ou base inconnue. Aucun
état n'est validé automatiquement et aucune IA ne produit de GO/NO-GO.
Il n'existe pas encore de matching sécurisé vers les objets, d'interface
d'acceptation/rejet, de journaux persistés ni de transaction de commit :
**cet incrément est un parser et une projection en lecture seule**.

Les tests synthétiques vérifient collisions, erreurs, contraintes,
idempotence des identifiants et absence d'écrasement par comparaison.
Les prochaines étapes #197 sont l'interface de prévisualisation,
les sources/versions, la qualification champ par champ et le commit
récupérable validé par l'utilisateur.

## Première interface WPF de prévisualisation

Dans l'onglet **Parcours**, la commande **Analyser une réponse IA…**
ouvre un dialogue de collage JSON. L'analyse du schéma
`nodalis.import.v1` et des limites se fait **hors Dispatcher WPF**.
Une liste virtualisée affiche jusqu'à 100 propositions, leur type,
leur action, leur degré de certitude déclaré et leur référence candidate.
Le détail affiche les champs et la base déclarée sans présumer du
contenu réel du projet. Une modification de la zone de texte invalide
immédiatement les résultats précédents.

Cette première interface est **strictement consultative** : elle ne
possède aucun bouton d'acceptation, aucun service d'écriture ni
connexion réseau. La prochaine tranche intégrera une résolution
d'identité projet explicite et un diff à trois voies depuis une
source canonique revalidée avant commit.


## Deuxième incrément — comparaison au registre canonique (03/11/2026)

Dans **Parcours → Analyser une réponse IA…**, le bouton
**Comparer aux exigences du projet** apparaît après une analyse JSON réussie.
Ce bouton est distinct de l'analyse : il lit exclusivement le registre
canonique `Documentation/Exigences` du **projet sélectionné** via
`ProjectRequirementRegistryService`. La lecture et la résolution s'exécutent
hors Dispatcher WPF ; aucune écriture ni recherche globale n'a lieu.

Pour les propositions `requirement/update`, la résolution suit une
priorité stricte :

1. `targetId` : GUID présent et unique dans le **même projet**.
   Un GUID absent ne déclenche aucun repli automatique.
2. En l'absence de GUID : `candidateReference` doit être une référence
   canonique exacte `EXI-xxxx`. Le résultat reste indicatif.
3. Le titre et `sourcePath` ne peuvent **jamais** déterminer la cible.
   `sourcePath` est une indication de provenance non fiable : elle ne
   donne pas le droit de lire un document d'une autre application/projet.
4. Référence et GUID contradictoires, doublons ou cible absente :
   correspondance bloquée ; la réponse IA ne peut corriger ces incohérences.

La proposition `create` n'est qu'un brouillon. Une référence déjà occupée
ou un `targetId` injecté par l'IA produisent un avertissement de collision.
Les types `risk/test/task/criterion/decision` et l'opération `link` sont
pour l'instant signalés **non résolus**, plutôt que devinés.

Le premier vrai diff à trois voies couvre uniquement le champ canonique
`title` d'une fiche EXI v2 ; les autres champs restent *non vérifiés*
jusqu'à l'ajout de leurs lecteurs métier. Les valeurs de base sont celles
**déclarées par l'IA** et ne sont pas authentifiées : une égalité ne constitue
jamais un consentement à l'écriture. La prévisualisation affiche l'ID
réel, la référence, le chemin et l'empreinte SHA-256 instantanée.

**Limites :** pas encore de création, modification, validation par champ,
journal de provenance ou commit transactionnel. Un fichier peut changer
après la lecture : une future mutation nécessitera une relecture des octets
et un contrôle optimiste d'empreinte immédiatement avant toute écriture.

### Recette Windows (à qualifier)

- Coller une proposition `requirement/update` avec GUID d'une exigence du
  projet : vérifier la référence, la valeur actuelle et le diff du titre.
- Tester une référence EXI exacte sans GUID, puis un GUID d'un **autre
  projet** accompagné d'une référence valide du projet actuel : le second
  cas doit rester **introuvable**, sans repli dangereux.
- Modifier localement le titre entre la préparation du prompt et la
  comparaison : voir `ConcurrentConflict`. Sans base, voir `UnknownBase`.
- Soumettre des GUID/références contradictoires, une création avec référence
  déjà occupée, une proposition de risque et un champ `description` :
  aucune ne doit recevoir une écriture ni un diff inventé.
- Recommencer après une modification du JSON : les résultats anciens sont
  invalidés. Vérifier petit écran, clavier, grands registres, retour de
  veille et absence de gel.

Les régressions automatisées portent le nom
`ProjectAiRequirementReviewSmokeTests`. Aucun merge vers `main`,
tag ou release 2.0 ne découle de cette étape.


## Troisième incrément — qualification humaine et plan préparatoire (03/11/2026)

Sur `develop`, le contrôle **Accepter / Modifier / À revoir / Ignorer**
s'applique **à un champ sélectionné**, sans affecter ses voisins. L'utilisateur
doit d'abord analyser puis comparer la réponse au registre canonique du projet
courant. Les décisions ne sont **jamais** déduites de `certainty` ni du texte
de l'IA. La liste et la comparaison sont invalidées dès que le JSON collé
change ; une nouvelle comparaison invalide les choix précédents.

Le contrat `ProjectAiFieldDecision` et le planificateur pur
`ProjectAiImportDecisionPlanner.Prepare` produisent un
`ProjectAiPreparedReviewPlan` **en mémoire seulement**. Une modification
proposée contient : GUID de proposition, GUID réel de l'exigence et du projet,
référence EXI, chemin canonique, empreinte SHA-256 de la version locale
consultée, ancien et nouveau titre, justification éventuelle et empreinte
de la réponse brute. Le panneau **Plan prévisionnel** expose ces informations
pour vérification ; rien n'est écrit ni envoyé à un service IA.

### Contrôles de sécurité

- Seule une proposition `requirement/update` associée sans ambiguïté à un
  objet du projet, et uniquement son champ `title`, peut être acceptée ou
  modifiée dans cet incrément. Les autres champs et types peuvent être
  marqués **À revoir** ou **Ignorer** ; jamais appliqués par supposition.
- En cas de base absente ou de changement local concurrent, **une justification
  humaine non vide** est exigée même si la proposition est acceptée.
  Cela ne dispense pas d'une relecture de la version du fichier au commit.
- Les choix contradictoires pour un même champ, les cibles disparues, les
  propositions étrangères, les titres multiligne/vides ou trop longs, deux
  changements approuvés sur une même exigence et les révisions de comparaison
  incomplètes sont rejetés.
- Modifier la valeur ou la justification dans les champs de saisie invalide
  **la décision précédemment enregistrée** tant que l'utilisateur n'a pas
  explicitement réappuyé sur Accepter ou Modifier. Les décisions restent
  volatiles : fermer la fenêtre ne les enregistre pas.
- Le plan n'est **pas exécutable**, ne modifie aucun fichier Markdown et ne
  tient pas encore d'historique durable. Le commit transactionnel, la
  relecture atomique, la récupération après interruption et la prévention
  persistée des réimports sont un incrément distinct.

### Recette Windows restante

1. Ouvrir le Parcours d'un projet fictif, analyser une réponse avec deux
   modifications d'exigences et un autre type de proposition ; comparer au
   registre du projet.
2. Choisir **À revoir** pour une suggestion, **Ignorer** pour une autre,
   **Accepter** ou **Modifier** pour un titre existant. Vérifier qu'aucun
   autre champ n'est accepté automatiquement.
3. Faire varier la base, provoquer un conflit concurrent, puis vérifier
   qu'une justification est requise pour obtenir un plan.
4. Modifier une valeur après l'avoir acceptée : le plan devient périmé et
   la décision est invalidée. Modifier le JSON : tout le plan est effacé.
5. Simuler deux propositions visant la même exigence : la seconde décision
   doit être refusée. Fermer et rouvrir : aucun état ou Markdown nouveau.
6. Vérifier le redimensionnement, le clavier, le DPI et l'absence de gel avec
   jusqu'à 100 propositions.

Tests : `ProjectAiImportDecisionSmokeTests` dans la suite Windows.
Les projets et données de test sont fictifs et aucun écran n'est commité.
Aucun merge sur `main`, tag ou release.


## Quatrième incrément — publication atomique d'un titre EXI (03/11/2026)

Le nouvel exécuteur `ProjectAiTitleImportTransactionService` est volontairement
**restreint à une seule modification de titre d'exigence existante à la fois**.
Il n'exécute aucune autre proposition de l'enveloppe `nodalis.import.v1`.
L'onglet de qualification ne débloque **Enregistrer 1 titre approuvé…**
qu'après l'analyse, la lecture canonique, les choix explicites et un plan
comportant exactement **une modification**. Un second clic de confirmation
mentionne la fiche, sa valeur actuelle, le titre retenu et le journal local.

### Contrôles avant publication

1. Revalider la manifestation du projet et interdire les projets archivés,
   déplacés, hors `Applications` ou non propriétaires de l'exigence.
2. Résoudre la fiche canonique existante exclusivement par le chemin
   `Documentation/Exigences/EXI-xxxx.md`, sans symlink ni jonction.
3. Recalculer l'inventaire du projet, son GUID, sa référence, son SHA-256 exact
   et le titre actuel approuvé. Tout écart entraîne un refus, pas une fusion.
4. Modifier **uniquement** `title:` dans le front-matter JSON-échappé et
   `# EXI-xxxx — titre` dans l'en-tête de la fiche. Le corps Markdown et
   les états indépendants validation/implémentation/vérification restent
   inchangés ; un en-tête personnalisé ambigu est refusé.
5. Écrire deux instantanés UTF-8 (source originale, version retenue) et un
   manifeste `nodalis.ai-title-commit.v1` **avant** toute publication.
   `AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync` vérifie les octets,
   l'empreinte et l'identité Windows du fichier sous verrou de lecture
   avant un remplacement atomique sur le même volume.

### Journal, reprise et limites

Le répertoire de récupération est
`<projet>/.nodalis-ai-import/<proposalId-N>/` et contient
`original.md`, `published.md` et `intent.json`. Le journal conserve
la provenance d'empreinte de la réponse, le GUID de proposition, les GUID
projet/exigence, la référence, les deux SHA-256, les deux titres et la
justification humaine éventuelle. **Il contient des copies intégrales de la
fiche** : ce répertoire reste local, privé au workspace, sans réseau.

Un second appel identique, si la fiche porte déjà le SHA publié, indique
`AlreadyApplied` sans réécrire. Si l'opération a été interrompue après
le manifeste et si les octets de la fiche sont encore strictement ceux du
snapshot original, une réexécution **explicitement autorisée** termine
la publication. Si l'état courant diverge ou si le journal/ses snapshots
ne sont pas intacts, l'exécuteur refuse toute reprise automatique. Un
répertoire partiellement préparé **sans manifeste** nécessite une
récupération/inspection manuelle et ne peut jamais être publié à l'aveugle.

Le verrou `.lock` sérialise les transactions de cette tranche dans un même
projet. Cela ne constitue **pas** une transaction multi-document : les plans
comportant deux modifications ou davantage restent prévisualisables, mais
ne peuvent pas être publiés ensemble. Les réimports de création, les autres
champs, les risques, tests, décisions et relations restent hors périmètre.

L'éditeur Markdown peut disposer d'un état non sauvegardé :
l'interface refuse maintenant la publication si **la fiche cible est
encore ouverte dans un onglet du MainWindow** (y compris le second
panneau de l'éditeur partagé), ou si le workspace/projet est en lecture
seule. Il faut fermer l'onglet, résoudre les sauvegardes pendantes, puis
recomparer avant de confirmer. Les éditeurs externes restent protégés
par le contrôle d'empreinte atomique, mais tout éditeur ouvert doit
être rechargé après une publication. Le journal fournit une base de récupération, mais
il ne remplace pas encore l'historique riche des réponses brutes, ni
un moteur de rollback multi-document.

### Qualification Windows

- Depuis un projet fictif et une exigence EXI, approuver un titre et
  enregistrer après confirmation. Les statuts et sections restent byte-pour-byte
  identiques hors des deux lignes de titre.
- Rejouer exactement la même proposition : aucun second changement,
  aucun nouveau GUID, résultat `AlreadyApplied`.
- Faire modifier le Markdown par un éditeur externe après prévisualisation :
  écriture refusée sans écrasement ; vérifier une deuxième version différente
  de la même proposition : son journal refuse les paramètres divergents.
- Tester la reprise à partir du journal intact avec fichier encore au SHA
  original, puis un fichier ayant divergé du journal : la première est
  rejouable sur action humaine, la seconde est bloquée.
- Tester un autre projet, un projet archivé, un chemin sous jonction, une
  fiche trop volumineuse, une interruption avant manifeste, les petits
  écrans, l'utilisation clavier/DPI et la fermeture d'un document ouvert.

Automatisation : `ProjectAiTitleImportTransactionSmokeTests`, suite
Windows existante. La qualification visuelle demeure à faire ; cette
tranche ne déclenche aucun merge vers `main`, tag ni release v2.0.


## Cinquième incrément — création qualifiée d'une exigence EXI (03/11/2026)

Un nouveau parcours utilisateur complète les mises à jour de titres existants :
**Parcours → Analyser une réponse IA → Comparer → Accepter/Modifier le
champ `title` d'une proposition `requirement/create` → Préparer le
plan → Créer 1 exigence approuvée…**.

Une proposition de création reste un brouillon tant qu'un utilisateur ne
l'a pas acceptée ou corrigée champ par champ. Les champs hors `title`
peuvent seulement être mis **À revoir** ou **Ignorer**, sans écriture.
Si la réponse IA tente d'attribuer un `targetId` ou une
`candidateReference` officielle, la création est rejetée : les
identifiants sont réservés à Nodalis.

### Identité, périmètre et publication

- Le plan `ProjectAiPreparedRequirementCreation` conserve
  `ProposalId`, le **GUID vérifié du projet courant**, le titre
  expressément approuvé et une éventuelle justification humaine ;
  il **ne contient pas d'identité d'exigence ni de code EXI IA**.
- Juste avant la confirmation, Nodalis reconsulte le registre du projet
  pour afficher **la prochaine référence EXI disponible**. Un journal
  existant pour le même `ProposalId` expose au contraire la référence
  précédemment réservée, afin d'expliquer le réimport sans faux code.
- Après confirmation, le service
  `ProjectAiRequirementCreateTransactionService` vérifie l'identité
  du projet, la non-archivage, l'appartenance au workspace, la référence
  choisie et le verrou `.nodalis-ai-import/.lock` partagé avec les
  publications de titres existants. Tout conflit sur le code EXI impose
  une nouvelle qualification ; aucun changement silencieux de référence.
- L'application génère un **nouveau GUID**, un `created-utc` et une
  fiche `Documentation/Exigences/EXI-xxxx.md` utilisant le codec
  canonique. Les trois statuts restent strictement séparés et
  conservateurs : validation **Draft**, implémentation **NotStarted**,
  vérification **NotVerified**. Aucun objet de type risque, test,
  décision, tâche ou lien n'est généré par ce service.
- Le service écrit le snapshot immuable `published.md` et le journal
  `intent.json` sous
  `<projet>/.nodalis-ai-import/<proposalId-N>/` **avant** le fichier
  métier. L'écriture de la fiche est atomique et **sans écrasement**.
  Une empreinte SHA-256 et une preuve `applied.json` sont ensuite
  conservées pour la reprise.

### Réimport et interruption

Une nouvelle tentative avec **le même couple réponse/proposition/
décision** relit le journal, valide son appartenance au projet et
l'intégrité du snapshot. Si le fichier canonique possède déjà les
octets attendus, la commande retourne `AlreadyCreated` : **aucun
nouveau GUID, aucun nouveau fichier**.

Si l'opération s'est interrompue après `intent.json` mais avant le
fichier EXI ou la preuve, un **nouveau clic humain** permet de publier
l'instantané original journalisé, avec **le même GUID et la même
référence**, tant que le code est toujours disponible. Un journal
incomplet avant l'intention reste bloquant et nécessite une inspection
manuelle. Une fiche modifiée extérieurement, un snapshot corrompu,
une référence désormais occupée, un projet différent ou une
autorisation divergente entraînent un refus sans écrasement.

Si la preuve `applied.json` existe mais que l'EXI a disparu,
Nodalis **ne ressuscite pas silencieusement** la fiche supprimée.
Une réponse complète reste temporairement en mémoire dans l'interface ;
le journal ne conserve pas encore les réponses brutes d'IA ni les
décisions portant sur les champs rejetés.

### Qualification Windows de la création

1. Dans un projet fictif, préparer `requirement/create` avec
   `proposedFields.title`, puis accepter ou modifier explicitement
   ce titre. Vérifier que la prévisualisation ne produit aucun fichier.
2. Faire afficher la prochaine référence EXI. Confirmer et contrôler
   l'identité générée, les quatre sections Markdown et les états Draft,
   NotStarted, NotVerified.
3. Rejouer la même enveloppe : le code affiché est celui du **journal
   initial** et aucune seconde exigence n'apparaît.
4. Tenter un projet distinct, une référence occupée, une proposition
   contenant `targetId` ou `candidateReference`, puis une fiche
   altérée après publication : toutes ces opérations doivent être refusées.
5. Simuler un arrêt entre le journal et la publication et un snapshot
   corrompu ; seule la reprise du journal intact sur consentement
   explicite est permise.
6. Qualifier le redimensionnement et le clavier WPF, les projets en
   lecture seule, les fermetures de documents et le rafraîchissement du
   Worktree après création.

Régression automatisée :
`ProjectAiRequirementCreateTransactionSmokeTests` dans les smoke
tests Windows. La création est **unitaire** ; la transaction de
plusieurs propositions reste un travail distinct de #197.
Aucun merge `main`, tag ni release v2.0 avant la qualification.


## Sixième incrément — lots de titres EXI, reprise après redémarrage (03/11/2026)

**Périmètre volontairement limité :** deux à vingt propositions
`requirement/update.title` **explicitement acceptées ou corrigées**
dans **un même projet**. Aucun autre champ, aucune création d'objet,
aucun changement de statut, aucun lien ni tâche n'est publié par le
moteur de lot. Les créations d'EXI restent des transactions unitaires
distinctes.

### Flux utilisateur

1. Depuis **Parcours → Analyser → Comparer**, qualifier chaque
   `title` par **Accepter** ou **Modifier**. Les autres champs peuvent
   être laissés **À revoir** ou **Ignorer**.
2. Cliquer **Préparer le plan**. Si deux à vingt titres existants sont
   retenus sans création concomitante, **Enregistrer le lot de titres…**
   devient disponible. Les fiches encore ouvertes dans un onglet Markdown,
   y compris le second panneau, bloquent la publication.
3. La confirmation affiche **chaque référence EXI, titre avant/après** et
   prévient du caractère **récupérable, mais non atomique globalement**,
   du lot. Le bouton peut modifier les fichiers uniquement après
   confirmation explicite.
4. Si l'application a été interrompue, ouvrir à nouveau la fenêtre
   d'import IA, cliquer **Rechercher les lots interrompus** ; choisir
   le journal incomplet et cliquer **Reprendre le lot choisi…**.
   Une seconde confirmation expose la progression et toutes les
   modifications journalisées, même sans la réponse IA originale
   encore collée dans la fenêtre.

### Préparation et publication

`ProjectAiBatchTitleImportTransactionService` possède un
`PlanFingerprint` SHA-256 déterministe sur les identités, chemins,
empreintes des fichiers, titres et justifications humaines, triés par
référence. Deux listes contenant les mêmes décisions dans un ordre
différent conservent la même identité de transaction.

Avant toute écriture métier, le service vérifie **toutes** les exigences
contre le registre canonique du projet, les GUID, chemins EXI, valeurs
et empreintes SHA-256. Il prépare, par fichier, des instantanés **des
octets originaux exacts** et de la version approuvée qui ne change que
`title:` et le H1. Les copies et le manifeste
`nodalis.ai-title-batch.v1` sont persistés sous :

`<projet>/.nodalis-ai-import/batch-<SHA-256-du-plan>/`

avec `original/0000.md`, `approved/0000.md` et `intent.json`.
Un verrou `.nodalis-ai-import/.lock` est partagé avec les créations
et les mises à jour EXI unitaires. Les sources ne sont jamais
acceptées depuis un chemin envoyé par l'IA : elles sont résolues
dans le registre du projet sélectionné.

Après la préparation et avant la première publication, le service
revérifie **tous les fichiers courants et tous les instantanés**.
Chaque publication vérifie une nouvelle fois, sous verrou de lecture
Windows, le SHA-256 et l'identité NTFS avant `File.Replace`.
Aucun fichier divergent n'est écrasé.

**Limite explicite :** Windows ne fournit pas de transaction atomique
pour un ensemble de fichiers indépendants. Une interruption entre
deux remplacements peut donc laisser un lot **partiellement publié**.
Le journal demeure disponible pour une reprise **uniquement sur
action humaine**. Si un autre éditeur a changé un fichier, **la
totalité de la reprise est bloquée sans toucher aux autres fichiers**.

### Reprise indépendante de l'ancienne fenêtre

`ProjectAiBatchTitleImportRecovery` découvre en lecture seule les
journaux incomplets (au plus 100 dans un projet). Une tentative de
reprise reconstruit les décisions approuvées à partir du **journal
persisté** et exige, avant de les exposer à l'utilisateur :

- identité et statut actif du projet vérifiés ;
- empreinte du plan correspondant au journal, références EXI et
  inventaire sans doublons ;
- instantanés originaux et publiés intacts, sans symlink/jonction ;
- **preuve octet par octet** que chaque version publiée est uniquement
  la réécriture canonique du titre de son original et ne modifie
  aucun état métier ou contenu de section ;
- fichiers en production correspondant chacun soit à son SHA original,
  soit à son SHA publié, sans troisième état.

Les journaux endommagés ou les fichiers divergents sont exclus de
la liste des reprises possibles et demandent une intervention
manuelle. Une relecture/revalidation complète précède la
reprise elle-même ; le résultat `AlreadyApplied` signifie qu'aucun
fichier n'a été réécrit.

### Qualification Windows et tests

La suite `ProjectAiBatchTitleImportSmokeTests` teste, sur deux
exigences fictives : rejet sans aucune publication si le **second**
fichier a changé avant l'import, interruption injectée après le
premier remplacement, journal et copies intacts, blocage de toute
reprise en présence d'un changement externe, restauration des
octets de test, puis reprise du **second** fichier seulement. Elle
teste aussi la découverte après **nouvelle instance du service**,
l'isolation entre projets, les journaux et snapshots corrompus,
les demandes contradictoires et la non-réécriture au réimport.

**À qualifier manuellement sur Windows :** ouverture/fermeture
des onglets, accessibilité des commandes et longueur de la liste
des changements, affichage de 20 propositions, état du Worktree
après publication, espace disque insuffisant, éditeurs externes,
comportement après sortie de veille et stabilité lors des reprises.
Une gestion plus élaborée des erreurs partielles, des lots mixtes
création/mise à jour, et des historiques d'arbitrages reste à
traiter dans #197. Aucun merge `main`, tag ni release avant
qualification utilisateur.

Les répertoires locaux `.nodalis-ai-import/` sont ignorés par Git pour
limiter le risque de commit accidentel des copies Markdown confidentielles.
Ce filtrage ne constitue pas un chiffrement ni une sauvegarde : protéger les
permissions du workspace et conserver le journal tant qu'un lot est partiel.


## Septième incrément — import mixte d'exigences, contrôlé et reprenable (03/11/2026)

**Nouveau périmètre de #197 :** dans **un seul projet** et une seule réponse
`nodalis.import.v1`, le plan de qualification peut approuver **au moins une
mise à jour de titre d'EXI existante ET une création d'exigence** (maximum
**20 opérations** en tout). Les propositions doivent être comparées et
chaque titre explicitement **Accepté** ou **Modifié** par l'utilisateur.
Les autres champs, états, critères d'acceptation, décisions, relations,
risques et tests ne sont **pas** publiés par le moteur mixte.

### Déroulé humain et identités

- Depuis **Parcours → Analyser → Comparer**, qualifier les propositions
  `requirement/update.title` et `requirement/create.title` puis
  **Préparer le plan (sans écrire)**.
- **Publier créations + titres…** relit les décisions et vérifie que
  chaque fiche existante est fermée dans les éditeurs Markdown de
  Nodalis. Le service recalcule les références EXI disponibles, ou
  restitue les références **déjà réservées dans un journal de reprise**.
- Une confirmation affiche **chaque titre avant/après** et chaque
  **nouvelle référence EXI**, avant tout enregistrement.
  L'utilisateur confirme le **lot exact** et les codes proposés.
- Les créations reçoivent de **nouveaux GUID Nodalis** et le codec
  `ProjectRequirementMarkdownCodec.Format`, avec validation
  `Draft`, implémentation `NotStarted`, vérification
  `NotVerified` et classification `Unclassified`.
  Aucune identité, référence ou approbation de l'IA n'est utilisée
  comme donnée d'autorité.

### Transaction durable : préflight complet puis écritures par fichier

`ProjectAiMixedImportTransactionService` impose la même serrure
locale `<projet>/.nodalis-ai-import/.lock` que les imports
unitaires et les lots de titres. Il contrôle le manifest du projet,
le registre EXI, les GUID et SHA des mises à jour, l'absence de
collisions entre les créations, ainsi que l'absence de réemploi
d'un `proposalId` déjà enregistré dans un autre journal.

Le SHA-256 déterministe du **plan humain** sert de clef
`<projet>/.nodalis-ai-import/mixed-<planSha256>/`.
Le service y conserve des instantanés `original/` (les octets
originaux des fiches existantes), `approved/` (toutes les versions
approuvées), le `intent.json` et les preuves de création
`receipts/*.json`.

**Aucun fichier métier n'est modifié avant que toutes les sources
aient été relues, les nouveaux codes contrôlés, les copies produites
et l'intention durable écrite.** Une préparation interrompue avant
`intent.json` reste volontairement bloquée pour inspection
manuelle : Nodalis ne régénère jamais silencieusement des GUID.

Le journal et les copies sont revérifiés intégralement **avant
la première publication** : les mises à jour ne peuvent changer
que le champ `title:` et le H1 canonique ; les créations doivent
correspondre **exactement** au Markdown généré par le codec. Même
un snapshot accompagné d'un SHA falsifié ne peut injecter du
contenu ou des statuts différents. La création est atomique **sans
écrasement**, les mises à jour utilisent une substitution atomique
avec revalidation du SHA et de l'identité Windows du fichier.
Le moteur refuse la moindre divergence externe.

**Aucune atomicité globale multi-fichiers n'est annoncée :**
une panne après une opération peut laisser un lot partiel.
Le journal contient les références et GUID déjà attribués.
Les créations enregistrent également un reçu durable après
publication. Une fiche créée puis supprimée avec ce reçu ne
peut être recréée automatiquement.

### Reprise après fermeture ou redémarrage de Nodalis

La commande existante **Rechercher les lots interrompus** découvre
désormais les lots **titres** et les lots **mixtes**, sous des libellés
distincts. Après sélection, **Reprendre le lot choisi…** relit et
vérifie le journal et toutes ses copies, l'état actif du projet et
l'intégrité de chaque fichier courant. L'interface présente chaque
création et chaque modification ainsi que la progression
(publiés/restants) et **exige une seconde confirmation humaine**.

La reconstitution des décisions repose sur le journal, non sur une
réponse IA encore présente dans la fenêtre. Aucun réimport
automatique : un fichier modifié, un journal incomplet ou
falsifié, un reçu incohérent ou une identité étrangère bloque
l'ensemble de la reprise **avant toute nouvelle écriture**.
Les dossiers `.nodalis-ai-import/` sont ignorés par Git mais
restent sensibles et doivent être protégés localement.

### Régressions et recette Windows

`ProjectAiMixedImportSmokeTests` couvre un projet de démonstration
synthétique avec **deux titres existants et deux créations**.
Cas testés : génération de deux codes successifs, plan sans
écriture, contrôle des projets différents, code approuvé devenu
périmé, édition concurrente du second fichier bloquant le
lot sans modifier le premier, interruption après le premier
titre, redécouverte du journal après nouvelle instance,
reprise bloquée en cas d'édition externe, seconde interruption
après une création, conservation des GUID et des reçus,
refus de ressusciter une fiche supprimée, journal JSON
corrompu, SHA falsifié pour injecter une section, reprise
intègre puis réimport sans doublon, états et corps Markdown
préservés.

Qualification manuelle restante : tester l'affichage de
20 décisions sur Windows, l'accessibilité clavier/DPI, les
éditeurs ouverts dans les deux panneaux, la reprise après
redémarrage réel de l'application, les mises en veille,
les permissions des dossiers et les diagnostics de registre.
Cette tranche ne clôt pas #197 ; historique complet des
réponses brutes, décisions ignorées et autres objets métier
restent prioritaires. Aucun merge vers `main`, tag ou
release avant qualification.


## Huitième incrément — historique des arbitrages IA, réponse brute sur consentement (04/11/2026)

L'import IA dispose désormais d'un **journal distinct des publications EXI** :
`ProjectAiReviewHistoryService`. Il documente **ce que l'utilisateur a
réellement décidé**, sans confondre « revue enregistrée », « publication
confirmée », « publication réussie » et « lot interrompu ».

### Interface et déclenchements

Après **Analyser → Comparer**, l'utilisateur peut cliquer **Archiver la
revue…**. Une confirmation rappelle les données retenues ; cette
commande **n'applique aucune proposition métier**. Les commandes
existantes **Enregistrer un titre**, **Créer une exigence**, **Enregistrer
un lot de titres** et **Publier créations + titres** capturent
également les décisions **après la confirmation de publication, mais
avant la première écriture métier**. Une erreur d'archivage empêche de
commencer cette publication.

**Historique des revues** permet de retrouver les archives d'un projet
après redémarrage ; **Afficher la revue** montre les décisions par
champ et, si présent, le résultat de publication. La fenêtre n'ouvre
**jamais automatiquement** une ancienne réponse brute.

### Choix explicites, champs en attente et confidentialité

Le journal `nodalis.ai-review-audit.v1` contient :

- la date UTC, un **GUID de revue attribué par Nodalis**, le GUID du
  **projet courant**, le GUID de réponse externe et son SHA-256 exact ;
- le `promptKey` informatif, les nombres de décisions explicites,
  champs ignorés et champs à revoir ;
- **une ligne pour chaque champ proposé** — y compris les champs sans
  décision (`Pending`) ou explicitement marqués `Review` ;
- le type et l'opération de la proposition, la comparaison canonique,
  les GUID existants vérifiés et SHA source, la disposition humaine
  `Accept` / `Edit` / `Ignore` / `Review` / `Pending` ;
- les textes **explicitement acceptés ou corrigés**, les justifications,
  et seulement **les empreintes SHA-256** des valeurs proposées et
  bases IA pour les champs non retenus.

La case **Archiver aussi la réponse brute** est **décochée par
défaut**. Si et seulement si l'utilisateur l'active, Nodalis
recalcule le SHA-256 de la réponse collée, vérifie qu'il correspond
à la réponse prévisualisée, puis conserve son contenu UTF-8 exact
dans `response.txt`. Sinon, le contenu JSON complet **n'est pas
écrit** dans le dossier d'archive, même si la zone de texte contient
toujours la réponse en mémoire. L'archivage de la revue **n'implique
pas le consentement à conserver la réponse brute**.

Les titres acceptés et justifications restent eux-mêmes des
données potentiellement confidentielles. La documentation doit
insister sur les permissions du workspace, et non présenter ce
dossier comme chiffré ou anonymisé.

### Journal de revue et résultat métier

Chaque capture est enregistrée dans
`<projet>/.nodalis-ai-import/reviews/<reviewGuid-N>/` :

- `review.json` : décisions et empreintes, écrit en dernier pour
  ne pas rendre visible une préparation incomplète ;
- `response.txt` : **facultatif, exclusivement après opt-in** ;
- `outcome.json` : résultat du moteur de publication, si une
  publication a réellement été tentée et le résultat enregistré.

Les tentatives de publication ont une référence déterministe
`TransactionKey` vers le **nom prévu du journal** (ce journal peut
ne pas exister si le contrôle préliminaire refuse toute publication) :
`<proposalGuid-N>`, `batch-<SHA>`, ou `mixed-<SHA>`.
La revue **ne suffit pas à affirmer qu'un EXI a été publié**.
Les résultats sont conservés dans un **fichier indépendant,
atomique et sans écrasement**, avec `Applied`, `AlreadyApplied`,
`Created`, `AlreadyCreated` ou `Failed`.

**`Failed` ne signifie PAS « tous les fichiers sont intacts »** :
un lot peut avoir été partiellement appliqué avant une interruption.
Une interruption brutale entre revue, import et outcome peut
laisser une revue **sans outcome**, à distinguer d'une réussite.
Si l'écriture de l'outcome échoue après une publication métier
réussie, l'interface conserve le succès métier et signale
séparément la perte de cette trace d'audit, sans relancer
automatiquement la transaction.

La revue archivée ne déclenche jamais une reprise de journal.
Les commandes existantes de récupération continuent à imposer
les contrôles de fichiers et une **nouvelle confirmation humaine**.

### Sécurité et qualification

- le projet doit être **actif**, dans le workspace sélectionné,
  et ses GUID de comparaison/plan/revue doivent correspondre ;
- le journal est écrit par fichiers atomiques `CreateNew`,
  sous le même verrou projet `.nodalis-ai-import/.lock` ;
- les chemins, fichiers et dossiers d'archive sous liens
  symboliques/jonctions sont refusés ;
- les contenus et nombres de champs restent bornés, les
  empreintes des réponses brutes sont vérifiées à la lecture ;
- `.nodalis-ai-import/` reste exclu de Git : **pas de commit
  de copies de travail, pas de capture d'écran, pas d'envoi IA**.

Tests automatisés :
`ProjectAiReviewHistorySmokeTests` vérifie la séparation
entre sauvegarde de revue et écriture métier, l'isolation
entre projets, l'historique des champs `Accept`, `Ignore`,
`Review` et `Pending`, la **non-persistance des textes IA
ignorés/en attente**, l'archivage brut uniquement avec consentement,
le refus d'une empreinte différente, la reprise de lecture après
nouvelle instance, le résultat lié au journal et le refus de
réécriture des preuves.

**Recette manuelle Windows restante :** confirmation/décochage de
l'option brute, fermeture/réouverture de Nodalis, consultation de
100 revues, lisibilité sur petits écrans et clavier, dossiers
en lecture seule, et avis de publication partielle en cas
d'interruption. Les autres champs/objets et l'historique des
réhabilitations/reprises ultérieures restent à développer.

La qualification de #197 ne vaut ni merge vers `main`, ni tag,
ni publication de la 2.0.


## Neuvième incrément — audit des reprises IA après interruption (04/11/2026)

La publication initiale d'un lot de titres ou d'un lot mixte et
la **reprise du même journal après arrêt** sont maintenant
documentées comme **deux événements humains distincts**.

### Flux utilisateur

1. **Rechercher les lots interrompus** reste une lecture seule des
   journaux de transaction EXI.
2. **Reprendre le lot choisi…** affiche le projet, les références
   concernées, les titres avant/après et la progression exacte.
   Rien n'est écrit si l'utilisateur annule.
3. Après **Oui**, `ProjectAiRecoveryHistoryService.StartAsync`
   relit et contrôle à nouveau le journal, l'appartenance au
   projet, les dossiers non redirigés, les identités et le
   nombre de fichiers déjà publiés/encore en attente.
   Il écrit un **audit de tentative** avant le premier fichier
   métier restant.
4. Le moteur d'import d'origine reprend ensuite normalement.
   Le service de traçabilité relit la progression finale,
   contrôle l'empreinte du journal métier, puis consigne un
   **résultat indépendant**. Une interruption brutale ne
   peut donc pas être présentée comme une reprise réussie.
5. **Historique des décisions IA / confidentialité →
   Historique des reprises → Afficher la reprise** montre
   la date, le lot, son type, le projet, la progression
   avant/après, les références et GUID des exigences et
   le résultat obtenu. Aucun texte de réponse brute ni
   contenu Markdown n'est chargé dans cette vue.

### Journal et sémantique des états

Dossiers locaux et exclus de Git :

`<projet>/.nodalis-ai-import/recoveries/<attempt-guid-N>/`

- `attempt.json` — écrit **seulement après validation humaine**,
  immuable, contient l'identité du projet, le type de lot
  (`title-batch` ou `mixed`), le nom canonique du
  journal d'import, le SHA-256 exact de son `intent.json`,
  la progression avant reprise et les références,
  opérations `create/update` et GUID métier associés.
- `outcome.json` — écrit ultérieurement **sans écrasement**,
  indique `Applied`, `AlreadyApplied`, `Failed` ou
  `NeedsInspection`, la date et les comptes après reprise
  **seulement s'ils ont pu être vérifiés**.

Un `attempt.json` **sans `outcome.json`** signifie
*tentative confirmée, résultat inconnu ou interruption*,
**jamais succès**.

`Failed` signifie que le moteur a rencontré une erreur :
**certains fichiers peuvent déjà avoir été publiés**.
Le service tente de calculer le nombre encore en attente,
mais si un document est divergent il note l'état final
comme **non vérifiable** plutôt que d'inventer des comptes.

`Applied` et `AlreadyApplied` ne sont conservés comme
succès vérifiés que si le moteur a répondu ainsi ET si
**tous les documents sont conformes** aux empreintes
publiées, **zéro fichier restant**, les GUID et références
sont identiques et l'`intent.json` n'a pas changé
par rapport à la confirmation initiale.
Si cette relecture échoue, le résultat est
`NeedsInspection` (pas une réussite vérifiée).

Une tentative et son résultat ne sont ni une nouvelle
autorisation de l'IA, ni une modification des décisions
initiales. Ils renvoient au **journal métier durable**.
La journalisation n'appelle aucun LLM, ne copie pas
les réponses brutes et ne provoque pas de reprise automatique.

### Régressions et qualification

`ProjectAiRecoveryHistorySmokeTests` simule, dans
des projets fictifs, un premier fichier publié puis
un arrêt, plusieurs tentatives distinctes, une
interruption sans résultat, une édition externe
bloquant la reprise, un résultat `Failed`
sans affirmer l'absence de changement, une vraie
reprise complète, un résultat vérifié, un journal
modifié durant la reprise malgré tous les fichiers
publiés (`NeedsInspection`), le refus d'écraser
une preuve déjà écrite, l'isolation entre projets,
les comptes des créations EXI dans un lot mixte
et les audits JSON corrompus.

À qualifier manuellement sur Windows : relancer
l'application entre les tentatives ; ouvrir les
éditeurs sur les deux panneaux pendant une reprise ;
contrôler le retour d'erreur en cas de modification
du fichier en parallèle ; vérifier l'affichage en
DPI élevé et dans les thèmes sombre/clair.
Un audit ne remplace pas un journal métier : la
réparation manuelle d'un lot divergent reste distincte.

Cette tranche de #197 n'autorise aucun merge de
`develop` vers `main`, aucun tag, ni release.

## Dixième incrément — comparaison locale des descriptions EXI (04/11/2026)

Lorsqu'une réponse externe propose un champ `description` sur une
exigence existante, le registre lit **dans le même instantané Markdown**
que le titre et l'empreinte SHA-256 la section
`## Description vérifiable`. La vue de revue affiche désormais la
**comparaison à trois voies** (base IA déclarée, texte local, proposition)
et signale les conflits. Le texte Markdown local est extrait en lecture
seule, hors blocs de code balisés ; les sous-sections restent visibles.

Une section absente, dupliquée, malformée ou de plus de 8 000 caractères
est **non résolue** : aucun diff local fiable n'est présenté. Les valeurs
générées ne sont jamais utilisées comme texte local de substitution.
La comparaison est strictement **consultative** : les boutons
Accepter/Modifier et le plan de publication restent réservés au titre.
Les descriptions, critères, statuts et validations ne sont toujours
**pas modifiés** par l'import IA. La publication des descriptions demandera
un service de transaction séparé et une qualification humaine complète.

Tests de non-régression : section Markdown, sous-titres H3, titre H2 dans
un bloc de code, doublon, taille maximale, conflit de base déclaré, et
refus d'approuver une description dans le plan actuel.

### Incrément suivant — codec de remplacement pur (04/11/2026)

Le lecteur de description EXI possède un moteur **en mémoire uniquement**
pour produire un instantané Markdown proposé : section unique, prise en
charge des clôtures de blocs de code, contraintes de taille, contrôle de
l'identité et des métadonnées, relecture de la valeur avant/après.
Les portions du document hors description sont conservées textuellement,
y compris les fins de ligne LF/CRLF. Une nouvelle section de niveau H1/H2
introduite par une proposition est refusée.

Ce moteur **n'effectue aucune écriture** et ne contourne pas le statut
consultatif des descriptions dans le dialogue d'import. Il sert à
construire ultérieurement une transaction explicite avec journal,
confirmation humaine et vérification SHA-CAS.

## Nouvel incrément #197 — publication d'une description EXI (04/11/2026)

Le dialogue de prévisualisation IA accepte une décision humaine
`Accept/Edit` sur `description` **uniquement** lorsque le Markdown
local est résolu sans ambiguïté. La zone de saisie devient multiligne ;
le plan expose les deux textes complets, les identités et l'empreinte
du document. Un bouton **Enregistrer 1 description approuvée…** impose
la fermeture du document ouvert, un plan reconfirmé et une confirmation
finale indépendante, avec option d'archiver la réponse brute **désactivée
par défaut**.

Le service de publication n'autorise qu'une description par transaction
et conserve `original.md`, `published.md`, puis `intent.json` avant
l'unique remplacement atomique Windows protégé par SHA-256. Une reprise
identique est idempotente ; une divergence, une collision de GUID,
un répertoire redirigé ou un journal corrompu interdit toute réécriture
silencieuse. L'audit des décisions et l'issue de la transaction sont
séparés et persistent sous `.nodalis-ai-import/` dans le projet.

**Les statuts, critères et autres champs EXI restent inchangés.** La
publication des descriptions ne s'intègre pas aux lots de titres ou
mixtes : les actions existantes refusent les plans contenant une
description approuvée. Les fichiers concernés, résultats de validation
et historiques doivent être qualifiés visuellement sur Windows.

## Suite #197 — aperçu comparatif des critères CA (04/11/2026)

Les propositions `kind: "criterion"` et `operation: "update"`
peuvent désormais être **comparées en lecture seule** lorsqu'elles
fournissent le `targetId` d'un bloc `nodalis:block:<GUID>`
déjà identifié dans une exigence du projet courant.
Le résolveur construit **une seule fois** un index des GUID CA à
partir du registre EXI, exclut les critères non identifiés ou ambigus,
les collisions inter-fiches et les divergences `candidateReference`
(`CA-01` n'est jamais un identifiant global). Aucune lecture du
`sourcePath` transmis par l'IA et aucun repli par titre.

L'interface présente le parent EXI, la référence locale CA, son GUID
et la comparaison à trois voies du **titre de critère**.
Les champs non résolus restent signalés ; création et liaison de CA
ne sont pas prises en charge. Les boutons `Accept` et `Edit`
demeurent bloqués pour les critères : il n'existe encore **aucun
moteur de publication CA** dans cet incrément. Les choix
`Review` et `Ignore` peuvent être archivés séparément.
Aucun verdict, test, statut ou lien n'est modifié.

Qualification : réponse IA de test contenant un GUID CA stable,
collision de GUID inter-EXI, CA homonymes, référence divergente,
GUID absent, base IA concurrente et source issue d'un autre projet.
