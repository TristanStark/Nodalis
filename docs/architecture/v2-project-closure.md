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


## Huitième incrément — déplacement EXI A avec GUID stable (07/11/2026)

Le mode **A** n'est plus seulement une intention : depuis
**Projet > Préparer une passation… > Prévisualiser une passation EXI…**,
une EXI *simple et sans relations détectées* peut être déplacée
réellement vers un autre projet actif après un second accord humain.
La même fenêtre expose les transactions A interrompues, leur reprise
explicite et le rollback de celles terminées. Aucun nouveau sous-menu
`2.0` n'a été ajouté.

### Propriété et périmètre des fiches

`ProjectRequirementHandoverCodec` conserve le **GUID historique**
pour A, sa date de création et ses états explicitement validés, tout
en inscrivant le `project-id` cible et la nouvelle référence
`EXI-xxxx` calculée dans son registre. Un court marqueur de provenance
dans le document destinataire relie l'intention A à l'identité et à la
référence de l'origine. L'ancienne fiche source est déplacée telle
quelle dans le journal ; elle n'est ni réécrite, ni effacée.

**A et B refusent** dans ce premier périmètre les documents contenant
des critères d'acceptation (y compris texte libre), des blocs
identifiés, des tâches/checkboxes, des liens Markdown ou d'autres
relations qui nécessitent une requalification humaine préalable.

### Contrôle des références

`ProjectRequirementMoveReferenceGuard` est lancé uniquement à
l'exécution/reprise/restauration demandée par une personne, pas au
scroll du Worktree. Il inspecte de façon **bornée** les sources
Markdown et JSON sous `Applications` pour trouver :

- le GUID original (`D` et `N`) dans d'autres documents ;
- la référence `EXI-xxxx` d'origine ;
- la future référence `EXI-xxxx` du destinataire avant toute
  publication, et cette dernière avant toute restauration.

Les simples déclarations de référence dans la fiche *indépendante*
`Exigences/EXI-xxxx.md` d'un autre projet ne sont pas traitées comme
des liens, mais sa prose reste inspectée. Les registres d'intentions
`Passations.md` et bilans historiques sont exclus. Toute autre
mention, un lien symbolique, un fichier non lisible ou un dépassement
de limites empêche une mutation automatique. Les formats autres que
Markdown/JSON n'étant pas analysés par cette première version,
leur présence éventuelle n'est **pas** une preuve d'absence de liens :
une requalification reste nécessaire en cas de relations hors périmètre.

### Transaction A et reprises

`ProjectRequirementMoveTransactionService` conserve sous
`Imports/.exi-move-transactions/<UUID>/` :

- `journal.json`, incluant intention, projets source/cible, GUID,
  références et SHA-256 des deux versions et phase ;
- `original.md`, snapshot octet exact de la fiche source ;
- `destination.md`, snapshot complet de la nouvelle fiche ;
- `parked-original.md`, fichier source original **déplacé** hors
  du projet avant toute publication chez le destinataire ;
- `withdrawn-destination.md`, exemplaire exact de la version publiée
  conservé pendant une restauration.

Le journal et les deux snapshots sont persistés **avant toute
mutation métier**. L'ordre d'exécution est strict :

1. Préflight de l'intention, des deux propriétaires et de la prochaine
   référence disponible ; audit à la demande des références ;
2. déplacement de l'original dans son journal (GUID actif absent
   momentanément, jamais dupliqué entre les deux projets) ;
3. création **CreateNew** de la fiche destinataire (jamais d'écrasement),
   vérification de sa SHA et du GUID ;
4. phase `completed` enregistrée durablement.

Une panne après n'importe quel checkpoint laisse une phase
`prepared` inspectable. La reprise approuvée humainement reconnaît
la présence du fichier déplacé ou de la cible créée par leurs SHA.
Une nouvelle occupation de référence, une édition externe ou un
fichier source inattendu bloque la reprise au lieu de forcer l'écriture.

Le rollback, lui aussi humain, vérifie le SHA de la cible et les
backlinks ; puis déplace **d'abord** la cible dans le journal,
et **ensuite** restaure les octets originaux dans le projet initial.
La phase `rollback-prepared` rend les deux checkpoints récupérables,
et la version destinataire reste consultable dans le journal.

**Limites explicites :** pas de réécriture automatique de liens
entrants, pas de déplacement avec critères CA, pas de garantie
d'atomicité multi-fichier ni de blocage des éditeurs externes ; seuls
les fichiers Markdown/JSON inspectés dans le périmètre du workspace
établissent le résultat des scans. Les échecs sont conservateurs.
Le code et les smoke tests doivent passer les deux workflows Windows,
et une qualification visuelle reste obligatoire avant un merge vers
`main`, un tag ou une release v2.0.


## Neuvième incrément — requalification des critères CA en passation EXI A (07/11/2026)

Le mode A permet désormais de préparer **et d'exécuter** le déplacement
d'une EXI possédant des critères d'acceptation, mais uniquement si tous
les CA sont non ambigus, équipés d'un GUID stable et révisés
**individuellement par une personne**. Le modèle B reste interdit
pour les exigences avec CA existants : il nécessiterait de nouvelles
identités ainsi qu'une requalification des preuves et verdicts,
qui ne sont pas déduits automatiquement.

### Préflight structurel des fiches

`ProjectRequirementHandoverCodec` utilise
`ProjectRequirementCriterionCodec` et
`MarkdownBlockIdentityCodec` pour distinguer :

- Une fiche EXI sans critère : comportement A/B antérieur maintenu.
- Une fiche EXI avec CA identifiés : **A uniquement**, à condition
  que chaque CA-xx possède un GUID stable, sans doublon, sans diagnostic
  structurel, et que chaque marqueur de bloc concerne précisément
  l'un des CA identifiés. Le texte du critère et son GUID ne changent
  pas pendant la mutation. Aucun texte hors d'un critère identifiable
  n'est accepté dans la section avant son premier CA.
- Une fiche avec CA sans GUID, CA dupliqués, bloc identifié hors CA,
  checkbox/tâche, lien Markdown `[[...]]` ou `[...](...)` :
  transfert refusé, en attendant la requalification correspondante.

Le prévisualiseur `ProjectRequirementHandoverPreviewService`
expose la **liste complète des CA et GUID** de sa source SHA-256 :
les diagnostics et les critères sans identité empêchent de produire
un candidat exécutable.

### Requalification humaine et audit

L'interface **Projet > Préparer une passation…**, sans nouveau menu,
ouvre `ProjectRequirementCriteriaReviewDialog` lorsque la
prévisualisation A contient des CA. La boîte de dialogue demande
de **cocher chaque critère séparément** et de fournir une
justification écrite. Son bouton d'approbation reste désactivé
tant que la liste est incomplète ou la justification absente.
L'étape suivante conserve la confirmation générale avant mutation.

`ProjectRequirementMoveTransactionService.ExecuteAsync` impose
indépendamment de WPF un ensemble *exact* de GUID approuvés.
Il refuse les approbations implicites, incomplètes ou dupliquées
et les motifs vides. La transaction conserve dans le journal
`ReviewedCriteria` (code local et GUID), `CriteriaReviewRationale`
et `CriteriaReviewedUtc`. Le journal et les snapshots sont
vérifiés à chaque étape de reprise : les CA originaux et déplacés
doivent avoir exactement les mêmes GUID, codes et intitulés.

**Important :** la revue n'approuve ni un nouveau verdict de test,
ni un changement de preuve, ni un changement de relation. Elle
autorise seulement la conservation des identités après contrôle
de l'absence de références entrantes non résolues. Les verdicts
existants ne sont pas réécrits.

### Liens entrants CA : audit explicite à la demande

`ProjectRequirementMoveCriteriaGuard` vérifie en une seule
inspection bornée tous les GUID des critères contre les sources
Markdown/JSON du workspace actif. Il recherche aussi les mentions
textuelles `CA-xx` dans les projets source et destinataire ;
seules les déclarations `### CA-xx ...` d'autres fiches EXI
indépendantes peuvent être ignorées en tant que définitions locales.
Une référence potentiellement entrante, un fichier illisible,
une jonction ou un plafond dépassé bloque la mutation. Le scan
s'exécute lors du déplacement, de sa reprise et du rollback,
pas en tâche de fond ni pendant les interactions du Worktree.

La transaction d'origine reste inchangée : l'EXI source est
mise à l'abri avant publication de la destination avec son GUID,
puis une restauration retire d'abord la cible. Les critères CA
voyagent à l'intérieur du document : aucun ID ou corps CA n'est
copié indépendamment et aucune preuve n'est remplacée.

### Tests et limites

`ProjectRequirementMoveCriteriaSmokeTests` vérifie le refus de
mutation sans revue, avec revue partielle ou sans justification,
la conservation d'identité de deux CA, les snapshots et motifs
historiques, la reprise après une interruption, la restauration
octet exacte, le rejet des backlinks GUID et des références
`CA-xx`, le refus des critères sans GUID ou des blocs hors CA,
et le maintien du blocage B.

Les relations externes structurées qui demandent un déplacement
coordonné, les critères sans identité stable et les successeurs
avec CA **ne sont pas migrés automatiquement**. La présence
de sources non-Markdown/JSON ou de liens non structurés nécessite
encore une revue humaine spécifique. Aucune mutation multifiche
n'est dite atomique. La qualification visuelle humaine et la
validation des deux workflows Windows sont requises avant release.


## Dixième incrément — succession EXI B avec CA requalifiés (07/11/2026)

Le **mode B** peut désormais proposer et exécuter une **nouvelle
exigence avec ses propres critères d'acceptation**, sans prétendre
qu'un critère du projet source a déjà été vérifié dans le projet
destinataire. L'exigence originale et ses campagnes historiques
demeurent intactes. Cette extension concerne les CA qui possèdent
déjà des GUID stables et des références `CA-xx` non ambiguës.

### Résultat : nouvelles identités et aucune preuve héritée

`ProjectRequirementCriterionSuccessorCodec` fabrique, sans écrire de
fichiers, un **brouillon entièrement neuf** à partir du modèle
`ProjectRequirementMarkdownCodec.Format`, et non d'une copie de
la fiche source. Il conserve uniquement le titre d'exigence,
le type de fiche, les codes locaux `CA-xx` et les intitulés des
critères. Il n'hérite **d'aucun corps de critère**, résultat de test,
preuve, verdict, état d'implémentation ou contenu historique libre.
Ses états reviennent à `Draft / NotStarted / NotVerified`.

Les GUID des CA successeurs sont déterministes et distincts de
leurs sources : `DeriveId(intentId, originalCaId)` calcule un
SHA-256 avec un préfixe de domaine et retient une identité GUID.
Le brouillon présente une filiation détaillée
`CA-xx : GUID source → GUID successeur · À requalifier` dans
la section **Filiation et requalification**.
Le plan est répétable octet pour octet à partir de l'intention
persistée et de son horodatage, sans recours à un générateur
de GUID au moment de l'exécution.

Les sources mal identifiées (GUID CA absent/dupliqué, marqueur
de bloc hors CA, diagnostics de structure, titre CA contenant
des liens Markdown non requalifiés) restent bloquées.
Les éventuels contenus métier et liens présents dans la
source ne sont **jamais** recopiés silencieusement dans le
successeur B : la personne devra renseigner et relier
explicitement les éléments pertinents après création.

### Étapes humaines et journal durable

Depuis **Projet > Préparer une passation… > Prévisualiser une
passation EXI…**, le mode B propose l'ensemble des nouvelles
identités au regard des anciennes. La même fenêtre de revue
que pour A possède un mode B, affichant **ancien GUID / nouveau
GUID** pour chaque CA, et l'absence de preuves héritées.
Chaque case doit être cochée individuellement et une justification
écrite est obligatoire. Une **seconde confirmation** valide
ensuite la création de l'exigence dans le destinataire.

Le service `ProjectRequirementSuccessorTransactionService`
refuse également, indépendamment de l'interface, une validation
partielle, dupliquée ou sans motif. Il persiste, avec les
snapshots `successor.md` et `journal.json`, le mapping ordonné
des CA, `CriteriaReviewRationale` et `CriteriaReviewedUtc`.
Son protocole reste une **création atomique CreateNew d'un
unique document** ; aucun lien ni document du projet source
n'est modifié.

À la **reprise** d'un journal `prepared`, le contenu stagé
est vérifié par SHA-256, puis les GUID CA / références / intitulés
sont comparés au journal d'origine avant de reconnaître ou
rejouer la publication. La restauration explicite refuse tout
document édité depuis création. En plus du contrôle des liens
vers le GUID de l'exigence elle-même, un scan borné bloque le
retrait si une autre source mentionne un **nouveau GUID CA**
ou sa référence `CA-xx` dans le projet destinataire.
La fiche retirée reste conservée dans
`withdrawn-successor.md`.

### Régressions et frontières

`ProjectRequirementSuccessorCriteriaSmokeTests` couvre :
accord humain requis pour **tous** les CA, justification,
répétabilité des nouveaux GUID, absence des canaris de preuve
et verdict source, brouillon non vérifié, source byte-exacte,
reprise après une interruption avant/après CreateNew,
rollback bloqué par liens GUID et `CA-xx`,
restauration après désassociation, et refus des CA
sans identité source fiable.

Cette opération n'entraîne **aucune revalidation de tests** :
des définitions de critères nouvelles et vides ont été créées,
mais les résultats de campagne, Go/No-Go, procès-verbaux,
provenances croisées et liens vers exigences restent à
requalifier explicitement. Les formats hors Markdown/JSON,
les tests et incidents reliés nécessitent encore une démarche
humaine ; aucun traitement atomique multi-document n'est affirmé.
La qualification WPF visuelle est indispensable avant release.


## Onzième incrément — revue guidée des relations EXI/CA/TEST/REC/INC (07/11/2026)

Le parcours de passation **Projet > Préparer une passation…**
présente maintenant, lors de la sélection d'une intention EXI A/B,
une **revue des dépendances réelles à requalifier** avant toute
confirmation de création/déplacement. La lecture est explicitement
demandée par la personne, avec opérations de registre, rafraîchissement
de l'index partagé et lecture des campagnes REC exécutés **hors du
Dispatcher WPF**. Aucun scan permanent n'est ajouté au Worktree.

### Sources déjà existantes et vérifications

`ProjectRequirementHandoverRelationReview.Build` rejoint sans écrire :

1. le catalogue de fiches EXI du **projet source** : carte et
   critères `CA-xx` ancrés à la SHA de la prévisualisation ;
2. le `LinkIndexCatalog` partagé, rafraîchi à la demande :
   sources documentaires, identifiants de blocs, relations typées
   entrantes/sortantes et collisions éventuelles ;
3. la projection existante `ProjectRequirementTraceability` :
   liens `TEST/TST → CA` seulement lorsque leurs GUID, chemin,
   bloc et source sont reconnus, uniques et à jour ;
4. la projection `ProjectRequirementCampaignTraceability` :
   historique des passages REC figés, avec environnement/version,
   résultat déclaré et absence de résultat, sans promotion de
   conformité ;
5. les seules associations REC/incident explicitement approuvées,
   reconstituées avec `ProjectTestIncidentLinkPolicy.Active`
   depuis les événements du catalogue REC source.

Le service construit une liste limitée à **1 200 entrées**,
sans reconstruire un index spécifique aux passations ni rescanner
tous les fichiers pour chaque CA. Les erreurs de source, de SHA,
d'index, d'identité ou de résultat et les listes tronquées
deviennent des **diagnostics visibles**, pas des liens supposés
valides. Une exigence devenue différente de la SHA affichée ne
reçoit pas un état de revue « terminé ».

### Interface et décisions laissées à une personne

`ProjectRequirementHandoverRelationsDialog` affiche une liste
défilante regroupant des actions distinctes selon le type :
**critère**, **test lié**, **ancien passage de campagne REC**,
**incident explicitement associé à un résultat**, **relation
typée** et **diagnostic**. Les GUID et chemins des sources
sont indiqués avec leurs contextes et les démarches suggérées.

L'utilisateur peut **copier la liste des actions** pour la suivre
dans Nodalis ou ailleurs ; cette fonctionnalité n'écrit aucun
document du workspace. « Poursuivre sans migrer les liens »
signifie seulement prendre connaissance de l'audit. Ce bouton
**n'accepte pas les critères comme vérifiés**, ne modifie ni
relations, ni tests, ni incidents, ni bilans. Les confirmations
spécifiques A/B et leurs garde-fous transactionnels demeurent
obligatoires et indépendants :

- A conserve le GUID d'origine, mais les scanners de backlinks
  existants refusent toujours un déplacement lorsque les liens
  présents ne peuvent pas être requalifiés ;
- B crée une nouvelle exigence et, s'il y a des CA, de nouveaux
  GUID. Les anciens liens, REC, résultats et incidents restent dans
  la source ; le rapport indique ce qu'il faut explicitement
  recréer ou associer plus tard.

Le rapport peut être consulté même si le préflight EXI contient
déjà un **blocage de structure** : il aide à comprendre les
dépendances à traiter avant de relancer une intention valide.

### Tests et limites actuelles

`ProjectRequirementHandoverRelationReviewSmokeTests` assure
sur données synthétiques :

- liaison d'un GUID CA unique à un TEST indexé de son propre
  projet, jamais une conclusion de réussite ;
- conservation du résultat REC historique et du contexte de
  passage ;
- référencement d'un incident uniquement via une association
  REC explicite (aucun incident inféré d'un échec) ;
- présence de la relation typée d'origine et une liste d'actions
  stable pour les mêmes entrées ;
- refus de prétendre que l'audit est complet en cas de SHA
  source changée ou d'index obsolète.

**Limite volontariste de cet incrément :** il s'agit d'une
**projection et d'une checklist, pas encore d'un moteur de
réécriture transactionnelle** pour les liens TEST/REC/INC.
Elle ne peut pas remplacer la validation humaine de preuves
ni garantir que des formats non indexés contiennent zéro lien.
La qualification visuelle WPF reste nécessaire avant une
release 2.0.

 
## Incrément 12 — inventaire borné et replay REC mutualisé (07/11/2026)

La checklist EXI/CA/TEST/REC/INC s'arrête dès l'atteinte de **1 200
entrées**, y compris au milieu des boucles de tests, passages et incidents.
Si d'autres éléments existent, un diagnostic de troncature marque le rapport
incomplet ; aucune gigantesque liste n'est d'abord construite puis raccourcie.

Les décisions humaines REC→incident sont désormais validées et rejouées une
seule fois par campagne, puis regroupées par GUID de résultat. Un événement
de dissociation retire le lien actif, sans effacer son historique et sans
transférer de résultat ou présumer de la résolution de l'incident.
L'API historique de lecture pour un seul résultat reste compatible.

Les smoke tests synthétiques vérifient le plafond de 1 200 éléments et la
séparation de deux résultats avec liaison/déliaison. Aucun document métier
n'est modifié par la revue ; qualification CI Windows et visuelle requises.

 
## Incrément 13 — Préflight RIS A/B, sans exécution (08/11/2026)

Une intention de passation **RIS** déjà approuvée est désormais accessible dans
**Projet > Préparer une passation > Risques**. Le service relit la source
et le destinataire actifs, leurs inventaires, l'intention append-only et
l'empreinte SHA-256 exacte de la fiche de risque. Toute divergence source,
cible, projet propriétaire, référence ou journal bloque le préflight.

- **A :** propose la même identité RIS, sans déplacer le fichier ; les
  évaluations, mesures, tâches associées et liens doivent être audités avant
  la mise au point d'un exécuteur transactionnel, de sa reprise et de son rollback.
- **B :** prépare uniquement en mémoire un Markdown de successeur au GUID et
  à la référence du projet cible. Le titre est la seule donnée métier reprise.
  Qualification = ToReview, cycle = Open, cotations initiale/résiduelle non
  évaluées, aucune preuve, mesure, revue, traitement ni événement historique.
  Aucun lien de provenance n'est persisté avant une future transaction approuvée.
- Les dossiers RIS absents ne sont pas créés en consultation. Une référence
  existante, des diagnostics de registre ou un projet archivé interdisent
  une préparation présentée comme fiable.

Le bouton RIS affiche un aperçu sombre via la fenêtre de passation existante ;
il **n'existe pas** de commande « exécuter RIS » dans cet incrément.
La suite `ProjectRiskHandoverPreviewSmokeTests` couvre le mode B vierge,
le mode A non mutateur, les sources devenues obsolètes, les références et
la propriété des fichiers sans données métier réelles.
