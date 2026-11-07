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
