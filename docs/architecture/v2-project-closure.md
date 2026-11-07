# Nodalis 2.0 — Pré-audit de clôture (#204)

## Premier incrément sur `develop`

Avant l'archivage via la commande existante « Clore et archiver », Nodalis
propose un **pré-audit consultatif** calculé sur demande, sans initier de
migration ni créer de nouveau registre. Le travail de lecture se déroule
hors du thread WPF. L'utilisateur peut refuser ou poursuivre l'archivage
après revue des constats ; aucun élément ouvert ne devient bloquant.

Les quatre sources canoniques actuellement couvertes sont :

- **Risques** : fiches RIS dont le cycle n'est pas `Closed` ou la
  qualification n'est pas `Validated`.
- **Exigences** : fiches EXI non rejetées/obsolètes dont la validation
  explicite et la vérification ne sont pas toutes deux respectivement
  `Approved` et `Verified`.
- **MEP** : opérations sans bilan métier explicite.
- **Réserves MEP** : obligations RES toujours `Open` ou `InProgress`
  après reconstruction de l'historique append-only.

Les identités et les références affichées viennent des documents existants.
Chaque lecteur vérifie son périmètre projet ; les catalogues ne sont pas
modifiés. Un registre avec propriétaire divergent provoque une erreur,
jamais une inclusion silencieuse d'éléments externes. Les diagnostics de
lecture existants sont conservés : ils indiquent une **couverture
potentiellement incomplète**, et non un état « tout est soldé ».

Le pré-audit présente les comptages, au plus huit objets détaillés depuis
l'interface et jusqu'à quatre diagnostics. Les autres éléments sont
comptabilisés, sans générer une boîte de dialogue démesurée. Cette revue
**ne constitue pas un bilan de clôture signé**.

## Limites explicites du premier incrément

Le décompte n'inclut pas encore les tâches Markdown, jalons, incidents,
campagnes de tests, points de contrôle ni les transferts vers un projet
successeur. L'utilisateur doit les examiner indépendamment : un total
« 0 élément » n'atteste pas que le projet est intégralement soldé.

Il n'y a pas encore de justification persistée, de versionnement du bilan,
de mouvements d'objets ou de journal de transfert dans ce lot. Le service
d'archivage 1.9 existant reste responsable du déplacement réversible du
dossier complet, de la prévention de collisions et des imports DOCX en
attente. Aucune écriture dans les documents métier n'est déclenchée
par la préparation de la revue.

## Régressions

`ProjectClosureAuditSmokeTests` vérifie les états humains ouverts puis
résolus, les GUIDs stables, le refus d'un autre propriétaire, la non-
mutation des registres en mémoire et la lecture d'un ancien projet
Ultra-simple sans création de fichiers v2. La qualification CI et la
qualification visuelle Windows restent nécessaires.

## Suites de #204

1. Inventaire exhaustif à l'échelle du projet, sans rescans globaux
   (tâches, jalons, incidents, campagnes, contrôles).
2. Bilan versionné `Management/Bilan de clôture.md` avec auteur,
   justification, preuves, dettes et décisions humaines.
3. Passation au cas par cas : déplacement à identité conservée ou
   successeur à nouvelle identité, chacun avec prévisualisation,
   sauvegarde, reprise et journal de provenance.
4. Validation du scénario de clôture puis réactivation des archives,
   prévention des liens cassés et qualification visuelle.

Aucun merge `main`, tag ni release ne découle de ce lot.


## Deuxième incrément — bilan Markdown historisé et interface (07/11/2026)

Dans **Projet > Bilan de clôture…**, la fenêtre présente l'historique
des revues du projet, le pré-audit consultatif actuel et les limites
explicites de couverture. La saisie rassemble un verdict, un auteur et
une justification ; la confirmation est distincte de l'archivage.

Le document canonique opt-in `Management/Bilan de clôture.md` contient
un bloc JSON typé marqué `nodalis:closure-report:v1` et des **revues
append-only** (GUID, numéro, date, acteur, justification et résultat).
Chaque revue fige les objets du pré-audit, leurs références, leurs états
déclarés, leurs chemins sources et les diagnostics disponibles à la date
de la revue. Les anciens événements ne peuvent pas être supprimés ou
modifiés via ce service. Les trois issues possibles sont :
`ReviewRecorded`, `ReadyForArchive` ou `Deferred`. Elles expriment
uniquement la position humaine au moment de la revue ; « prêt à archiver »
**ne déclenche aucun transfert, déplacement, clôture d'objet ou archive**.

La projection ajoute un **SHA-256 de sources de l'audit** : toutes les
fiches canoniques RIS, EXI et MEP, le registre RES, les statuts et les
diagnostics sont inclus, même si un objet ne figure pas parmi les dettes
ouvertes. Au moment de consigner une nouvelle revue, le service relit
les sources et refuse une empreinte différente. La révision du bilan
est également vérifiée par SHA-256, avec création sans écrasement et
remplacement conditionnel atomique. Un fichier de bilan libre préexistant,
non conforme au contrat v1, n'est **jamais remplacé**.

La consultation ne crée aucun fichier. Le dossier Management et le
bilan n'apparaissent qu'après approbation humaine. Les archives sont
consultables en lecture seule, y compris à leur emplacement déplacé.
La fenêtre permet de consulter les quatre dernières revues et les
totaux, sans essayer de reconstituer les sources d'un projet archivé.

**Contrainte de cohérence :** la vérification des sources juste avant
l'écriture ne constitue pas une transaction atomique multi-documents.
Le SHA enregistré représente les sources **revues** et non une garantie
d'immutabilité future des RIS/EXI/MEP/RES. L'archivage reste une
commande distincte et réaffiche son propre pré-audit.

**Régressions :** `ProjectClosureReportSmokeTests` vérifie la création
opt-in, le format Markdown canonique, plusieurs versions append-only,
le rejet d'une révision obsolète, la détection d'une nouvelle EXI,
la lecture en archive et le refus d'écriture dans les archives ou
d'écraser un bilan libre existant.

Les tâches, jalons, incidents, campagnes, décisions de gate, justifications
par objet et transferts inter-projets restent à intégrer au suivi #204.


## Troisième incrément — inventaire local élargi (07/11/2026)

Le **pré-audit de clôture** et le **bilan versionné** consultent désormais
également les sources réelles du projet sélectionné :

- **Campagnes REC** : seules les campagnes v1 uniques et valides du
  projet sont retenues ; les cycles `Draft`, `InProgress` et
  `InQualification` sont proposés comme actions de suivi. Un cycle
  `Closed` ou `Canceled` n'est pas automatiquement considéré comme un
  résultat de test conforme.
- **Incidents de maintenance** : seuls les `INC-<GUID>` canoniques,
  uniques et complets de la fiche de maintenance locale sont examinés.
  Le `Statut` reste un texte libre : **tous les incidents recensés sont
  présentés comme « à vérifier »**, sans déduire une résolution.
  Les fiches incomplètes ou doublonnées donnent des diagnostics.
- **Tâches Markdown** : lecture des cases à cocher existantes, y compris
  hors du document `Tâches.md`, en réutilisant le codec d'identité
  persistante ; les cases cochées sont exclues. Une case ouverte sans
  GUID stable ou une identité dupliquée est **signalée**, pas inventée.
  Les blocs de code fenced ne deviennent pas des tâches.
- **Jalons** : lecture directe du fichier singleton de Jalons du
  projet, en réutilisant le parseur existant et en interdisant la
  migration implicite. Les jalons explicitement terminés ou annulés
  sont exclus ; les IDs calculés à partir de la position d'une ligne
  ne sont pas jugés transférables et donnent un diagnostic.

L'exploration ne traverse **aucun sous-projet portant son propre
`.project.json`** ni aucun lien symbolique. Elle reste sur le projet
demandé, sans actualisation de l'index global du Worktree, avec une
limite de 2 000 fichiers Markdown, 2 Mio par fichier
(25 Mio pour la fiche de maintenance) et 96 Mio par inspection.
Les registres structurés `Tests/Campagnes`,
`Exploitation/Déploiement/Opérations` et
`Exploitation/Déploiement/Suivi` sont hachés par leurs lecteurs
canoniques sans être reparcourus comme de simples tâches ; le bilan
de clôture lui-même est exclu du recalcul pour ne pas modifier sa
propre empreinte. Une limite dépassée **interrompt explicitement la revue** :
elle ne produit jamais un bilan prétendant qu'il n'y a plus d'actions
à reprendre.

L'empreinte SHA-256 combine les révisions RIS/EXI/MEP/RES, les
campagnes REC, les fichiers Markdown locaux examinés (y compris ceux
qui ne contiennent pas de case ouverte), et les diagnostics. Elle est
relue lors de l'enregistrement d'un bilan, qui refuse une source
modifiée ou nouvellement créée depuis le pré-audit.

Le comptage reste un **inventaire consultatif**. Les incidents
« vérifiés » et les campagnes fermées ne prouvent ni la résolution
formelle des anomalies ni la réussite des tests ; les réserves,
liens, décisions GO et preuves nécessitent toujours revue humaine.
Aucun objet n'est transféré automatiquement à ce stade.
`ProjectClosureAdditionalSmokeTests` couvre l'isolation des
sous-projets, les identités de tâches, les jalons, les INC, les REC,
les sources inchangées et la détection d'un audit devenu obsolète. Le lecteur INC restitue
également le SHA-256 des octets exacts qu'il a analysés ; si la fiche
de maintenance change avant le relevé local de tâches, le pré-audit
s'interrompt au lieu de mélanger deux états de la source.


## Quatrième incrément — préflight de passation A/B (07/11/2026)

Le menu unique **Projet > Préparer une passation…** ouvre une fenêtre
dédiée aux éléments encore à reprendre, aux projets destinataires
existants, aux deux stratégies, à l'auteur et à la justification.
L'historique des intentions du projet source est consultable en lecture
seule après archivage.

Le **préflight est strictement non mutateur**. Il exige deux projets
réels et actifs de ce workspace, dotés de manifestes distincts et d'un
emplacement canonique (y compris les sous-projets). Il refuse un
objet absent du pré-audit du propriétaire ou une sélection ambiguë,
puis relit indépendamment les deux inventaires et leurs empreintes.

- **A — Déplacer l'original** : la future identité du propriétaire est
  le GUID actuel. Le préflight bloque si ce GUID apparaît déjà dans
  le destinataire, même dans une tâche cochée ou une fiche soldée.
- **B — Créer un successeur lié** : un nouveau GUID est proposé et
  présenté avant consentement. L'original conserve son propriétaire
  et son état. Le GUID exact proposé est vérifié à nouveau lors de
  l'enregistrement de l'intention ; aucun successeur n'est créé ici.

Les diagnostics des sources et les différences d'applications sont
signalés sans être masqués. Les identités découvertes incluent les
RIS/EXI/MEP/RES, REC, INC, tâches à GUID même cochées et jalons à
identité persistante. **Ce n'est pas encore une preuve exhaustive de
non-collision** : documents invalides ou objets non indexés, liens
externes, références transversales et historiques de gates restent à
prendre en charge par l'exécuteur transactionnel.

Une confirmation humaine distincte crée un événement **append-only**
dans `Management/Passations.md`, au format exact
`nodalis:handover-intents:v1`, avec source/destination, identité,
référence, mode, nouveau GUID proposé, empreintes SHA-256 des
inventaires, auteur, date et justification. Ce registre n'est ni un
journal d'exécution ni une seconde source de vérité métier.
L'écriture est opt-in, bornée, réalisée en `CreateNew` ou par
remplacement atomique conditionné au hash précédent (SHA-CAS).
Les anciennes intentions ne sont pas réécrites. Les bilans libres
existant sous ce nom ne sont pas remplacés.

Le service relit source et destinataire **avant** toute écriture
et refuse un objet modifié, des collisions nouvelles, un changement
de cible ou une archive. Les décisions A/B ne modifient aucune tâche,
fiche Markdown métier, MEP, campagne REC ou lien : l'utilisateur ne
peut pas les prendre pour un transfert exécuté.

Les nouveaux tests `ProjectHandoverIntentSmokeTests` valident
les GUID A/B, les collisions avec les objets terminés, le refus de
relecture obsolète (source, destinataire ou journal), la protection
des projets archivés, la non-mutation des documents et la
préservation des anciens fichiers libres.

### Suite — exécution transactionnelle (non livrée)

Avant toute activation d'un véritable transfert, la feuille de route
exige encore : dry-run complet des backlinks, confirmation des
références visibles dans la destination, snapshot de sauvegarde du
source et de la cible, journal durable de phases, reprise / rollback
après interruption, garantie d'un seul propriétaire actif pour A,
et provenance bidirectionnelle liée pour B. La qualification visuelle
Windows et les tests d'interruption devront précéder cette activation.

Aucun merge, tag ni release 2.0 n'est engagé.


## Cinquième incrément — transfert transactionnel de tâches simples (07/11/2026)

Le dialogue **Projet > Préparer une passation…** permet désormais de
déclencher une exécution **distincte de la préparation** uniquement
pour une intention A/B déjà consignée et visant une tâche ouverte
identifiée par GUID dans le fichier racine `Tâches.md` de chacun des
deux projets. L'exécution n'est **pas** activée pour les tâches
imbriquées, les checkboxes dans d'autres documents Markdown, les
tâches générées à partir d'un jalon, les fiches RIS/EXI, campagnes
REC, incidents INC, réserves ou jalons.

### Double vérification avant publication

Le dry-run `ProjectHandoverTaskExecutionPlanner` exige deux projets
actifs et des fichiers `Tâches.md` existants, UTF-8 sans BOM et de
moins de 2 Mio. Il contrôle l'intention humaine append-only, les
GUIDs, la source et le destinataire, leurs empreintes, les fichiers
physiques et les tâches uniques sans sous-checkbox ni lien intégré.
Pour A uniquement, il effectue **à la demande de l'utilisateur**
une inspection bornée du workspace (Markdown et JSON, 6 000 fichiers,
96 Mio) : toute référence au GUID trouvé en dehors des deux fichiers
de tâches sélectionnés bloque le déplacement, sans réécriture
automatique de backlinks. Des jonctions ou scans trop volumineux
refusent également A, plutôt que d'afficher une certitude trompeuse.

Une dernière fenêtre présente les fichiers exacts à modifier et les
avertissements avant de demander une **deuxième confirmation humaine**
d'exécution. Préparer l'intention ne déclenche jamais cette action.

### Journal, snapshots et propriété

`ProjectHandoverTaskTransactionService` stocke ses journaux dans
`Imports/.handover-task-transactions/<UUID>/` avec les fichiers
`original-source.md`, `original-target.md`,
`proposed-source.md`, `proposed-target.md` et `journal.json`.
Les quatre copies durables et leurs SHA-256 sont écrits **avant**
la première publication ; le transfert est sérialisé par un verrou
workspace et les remplacements individuels utilisent
`AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync`.

- **A — déplacer** : la case source disparaît et est remplacée par
  une trace Markdown lisible ; l'original avec son GUID exact est
  ajouté à `Tâches.md` du destinataire. Ordre : retrait à la source
  **avant** insertion à destination, sans doublon actif entre les
  deux publications. Les backlinks hors des deux fichiers bloquent A.
- **B — successeur** : la case d'origine et son GUID sont conservés ;
  une case distincte avec le GUID approuvé est ajoutée à destination,
  et les deux documents conservent les identifiants de provenance
  et de l'intention. Ordre : successeur d'abord, provenance ensuite.

La publication reste **atomique fichier par fichier**, et non une
transaction du système de fichiers englobant deux fichiers. Une panne
entre les deux publications laisse un journal **pending** explicite,
aucune reprise silencieuse, et bloque les transferts suivants.

### Reprise et rollback

Les commandes de la même fenêtre permettent la **reprise explicite**
des journaux interrompus et le **rollback manuel** depuis les quatre
snapshots conservés. La récupération confronte les SHA des fichiers
réels aux états exacts originaux ou publiés. Elle rejette tout état
étranger, fichier manquant, propriétaire déplacé ou sauvegarde altérée,
sans écraser une modification humaine. Elle réapplique uniquement
l'étape manquante, dans l'ordre garantissant l'absence de deux
propriétaires actifs pour A. Le rollback utilise l'ordre inverse ;
les journaux et sauvegardes restent conservés. Une intention déjà
consommée ne peut pas être rejouée silencieusement.

Les tests `ProjectHandoverTaskTransactionSmokeTests` couvrent A/B,
la validation distincte, l'interruption après la première publication,
la reprise par une nouvelle instance, la restauration octet pour
octet, le refus de backlinks non examinés et la protection d'une
édition concurrente durant la panne.

**Restes à faire** : exécuteur analogue pour les objets RIS/EXI/INC,
jalons et campagnes ; détection/traitement approfondi des liens
métier et des références visibles, journal d'exécution consultable
dans la navigation, qualification visuelle et tests de grande
volumétrie. Il n'y a toujours ni merge `main`, ni tag ni release v2.0.


## Sixième incrément — préparation des passations EXI (07/11/2026)

La fenêtre **Projet > Préparer une passation…** propose désormais
**Prévisualiser une passation EXI…** pour les intentions d'exigences
déjà validées humainement. Contrairement à l'exécuteur transactionnel
de tâches, **aucun déplacement ou successeur EXI n'est encore écrit**
à cette étape.

`ProjectRequirementHandoverPreviewService` vérifie sur demande :

- Le propriétaire et le projet destinataire actifs (manifestes
  distincts, chemins du workspace et sans liens symboliques).
- L'intention A/B append-only, son GUID exact et ses empreintes
  `SourceFingerprint` / `TargetFingerprint` ; toute évolution d'une
  source ou d'un destinataire invalide la préparation historique.
- La source Markdown EXI versionnée, le même SHA-256 que la lecture
  canonique, le même GUID, le même propriétaire et le même code
  `EXI-xxxx`. Les registres présentant des sources ambiguës bloquent
  une préparation fiable. Les sources UTF-8 LF ou CRLF sont acceptées
  sans conversion silencieuse : les fiches produites sous Windows avec
  `AppendLine()` conservent leur CRLF dans le candidat.
- La prochaine référence `EXI-xxxx` libre et non encore réservée
  chez le destinataire ; aucun dossier ou fichier de destination
  n'est créé.

`ProjectRequirementHandoverCodec` construit un candidat **en mémoire**
en préservant les descriptions et sections Markdown métier :

- **A** : le GUID de la fiche EXI et ses statuts explicites sont
  conservés ; le propriétaire `project-id` et la référence lisible
  deviennent ceux de la future destination. Les liens et anciens
  codes doivent être requalifiés avant tout véritable déplacement.
- **B** : un nouveau GUID approuvé est utilisé, la référence du
  destinataire est différente et l'historique métier de l'original
  reste en place ; les nouveaux états repartent explicitement de
  `Draft / NotStarted / NotVerified`, jamais d'une approbation
  ou d'un verdict de tests copiés.
- Toute EXI contenant déjà des **critères d'acceptation CA** est
  refusée pour une succession B tant que le renouvellement de leurs
  identités, liens et preuves n'est pas conçu et validé. A signale
  les critères comme liens à vérifier.

La provenance source, référence historique, GUID et intention
est inscrite **dans le candidat uniquement**, non dans une fiche
persistée. Les tests `ProjectRequirementHandoverSmokeTests` contrôlent
le nouveau GUID, la prochaine référence disponible, la préservation
octet des sources, le refus d'un préflight périmé, le refus de B avec
CA et la non-reprise d'approbations / verdicts passés.

**Étape restante :** exécuteur EXI spécifique avec une
contrepartie/source de provenance, journal durable des renommages,
gestion des références EXI textuelles et des backlinks, snapshots,
reprise et rollback comme pour les tâches ; une nouvelle confirmation
humaine sera requise avant toute écriture. La qualification WPF est à
faire sur workspace de démonstration. Aucun merge `main`, tag ou
release v2.0.


## Septième incrément — succession EXI B exécutable et réversible (07/11/2026)

La première mutation **réelle** de fiche EXI est désormais possible :
**Projet > Préparer une passation… > Prévisualiser une passation EXI…**.
L'intention doit avoir été consignée séparément. Une dernière fenêtre
affiche le GUID, la référence destinataire, la provenance, les blocages,
et demande une **seconde confirmation humaine** avant de créer
la fiche `Documentation/Exigences/EXI-xxxx.md` dans le destinataire.

### Périmètre exact

- **B — successeur lié : actif uniquement pour une EXI simple**, sans
  critères d'acceptation, texte libre dans les critères, blocs avec
  GUID, checkbox/tâche, ou liens Markdown nécessitant requalification.
  Le nouveau GUID est celui de l'intention immuable, et sa date de
  création est déterministe (date de l'intention). La nouvelle EXI
  repart de `Draft / NotStarted / NotVerified`. La provenance est
  inscrite dans sa fiche. L'EXI source reste byte-exacte et son
  historique d'intentions reste dans `Management/Passations.md`.
- **A — transfert de l'original : encore aperçu uniquement.**
  Déplacer réellement l'original et son GUID exigerait le traitement
  des backlinks et des références humaines : il n'y a aucun
  déplacement implicite ou remplacement silencieux.
- Le nouveau numéro `EXI-xxxx` reste une **proposition** jusqu'à
  l'exécution. Un second préflight vérifie le projet source, le
  destinataire, les sources, les empreintes et la référence disponible.
  La création utilise exclusivement `WriteNewAllTextAsync`
  (`CreateNew`) : un fichier apparu entre la prévisualisation
  et la publication ne sera jamais écrasé.

### Journal durable

`ProjectRequirementSuccessorTransactionService` conserve sous
`Imports/.exi-successor-transactions/<UUID>/` :

- `journal.json` : identité de l'intention, GUIDs source et cible,
  chemin de la fiche créée, code EXI choisi, SHA-256 source et cible,
  phase `prepared`, `completed`, `rollback-prepared` ou
  `rolled-back` ;
- `successor.md` : copie intégrale figée de la nouvelle fiche,
  persistée **avant** tout acte de création ;
- `withdrawn-successor.md` : fiche retirée, conservée **après**
  restauration, jamais détruite silencieusement.

Un verrou opérationnel workspace sérialise les transactions de
succession EXI B. Une panne après le `CreateNew` peut laisser
`prepared` avec un fichier déjà créé : la récupération explicite
compare son SHA-256 au snapshot et le reconnaît sans le réécrire.
Si le fichier est absent, la reprise ne le crée que si la source
originelle et la prochaine référence destinataire correspondent
encore au plan. Toute divergence ou modification humaine interdit
l'écriture.

Le rollback est **explicite et limité au successeur**. La fiche
d'origine est intacte. Le retrait exige le hash exact du successeur,
puis cherche les liens entrants avant de déplacer le fichier vers
le journal. `ProjectRequirementSuccessorBacklinkGuard` effectue
uniquement sur cette demande une recherche bornée : citations
du GUID dans les Markdown/JSON du workspace actif et citations du
code `EXI-xxxx` dans le projet destinataire. Un lien, une jonction,
un scan dépassant les plafonds ou une édition inconnue bloquent
le retrait. Un rollback interrompu juste après le déplacement est
reconnu grâce au fichier de conservation et à son hash exact.
**Aucune intégrité transactionnelle multifiche n'est revendiquée** :
il n'y a qu'un fichier métier créé ou retiré par cet exécuteur.

### Tests et limites

`ProjectRequirementSuccessorTransactionSmokeTests` vérifie les
confirmations humaines, la création sans modification source,
la non-réutilisation d'une intention, le redémarrage après un arrêt
avant/après `CreateNew`, le rollback interrompu, la restauration
exacte, l'interdiction d'écraser une édition humaine, et le rejet
d'une référence réservée par un autre processus. Les régressions
sur les références GUID et `EXI-xxxx`, critères libres, blocs et
checkboxes intégrés complètent cette couverture.

**Restes à faire :** transfert physique A, requalification des
critères CA et des liens entre objets, recherche des références
humaines plus sophistiquée, UX de navigation et qualification
visuelle sur workspace de démonstration. La publication `main`,
le tag et la release v2.0 ne sont pas déclenchés.
