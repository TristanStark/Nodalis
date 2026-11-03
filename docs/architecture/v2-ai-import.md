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
