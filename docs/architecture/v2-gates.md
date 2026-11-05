# Nodalis 2.0 — Registre des gates et décisions humaines (#202)

**Statut :** première tranche sur develop ; qualification visuelle Windows nécessaire avant la publication 2.0.

## 1. Stockage canonique et règles de sécurité

Chaque projet dispose, après la première sauvegarde explicite seulement,
du fichier **.project-gates.json**, indépendant de **.project-journey.json**,
de **Jalons.md**, des Decision Records, des tâches et de la documentation.
La consultation initiale ne crée aucun fichier.

- ProjectGateRegistry : version de schéma, GUID du projet, définitions,
  décisions et historique append-only.
- ProjectGateDefinition : GUID stable, nom du contrôle, critères
  personnalisables avec GUID, caractère obligatoire ou facultatif,
  relations optionnelles avec les jalons existants.
- ProjectGateDecision : GUID de décision, gate et nom gelé, date UTC,
  acteur humain, périmètre, version/environnement optionnels, verdict,
  justification, évaluations figées (noms et facultativités compris),
  chemins et SHA-256 des preuves, réserves identifiées.
- Les projets archivés sont consultables mais aucune écriture n'est permise.
  Les chemins de preuve traversant une jonction, un lien symbolique ou
  sortant du workspace sont refusés.
- Chaque sauvegarde est conditionnée au SHA-256 de la version lue.
  Une révision concurrente est refusée, sans merge ni réécriture implicite.
  Les décisions existantes ne peuvent être effacées, modifiées ou réordonnées.

La modification d'un critère ne change que les futures décisions. Une
décision passée conserve ses libellés et évaluations au moment de la revue.

## 2. Utilisation dans le Parcours

1. Ouvrir le Parcours projet 2.0, choisir l'étape puis une opération.
2. Développer « RELATIONS · JALONS ET GATES » et cliquer **Gates…**.
3. Créer un point individuel ou activer volontairement **Proposer les
   5 standards** (cadrage, prêt développement, recette, production,
   clôture). Cette proposition est confirmée et idempotente ; elle ne
   produit aucune décision.
4. Ajouter des critères obligatoires/facultatifs au gate sélectionné.
5. Ajouter une preuve locale existante ou utiliser les références déjà
   liées à l'opération. Dans le dossier de décision les preuves sont
   décochées par défaut : une personne les choisit explicitement.
6. Cliquer **Consigner une décision…** ; renseigner auteur, périmètre,
   justification, version/environnement si nécessaire, qualifier tous
   les critères, puis cocher les preuves à figer.
7. Confirmer après lecture du résumé final. La fenêtre de registre
   permet de consulter les décisions immuables et leurs empreintes.
8. Cliquer **Lier au Parcours** pour ajouter le GUID du gate existant
   à l'opération. La sauvegarde du Parcours est confirmée séparément
   et vérifiée par son propre contrôle SHA-256.

Le bouton **Actualiser** du Parcours recharge le registre seulement
sur demande. Il affiche le nom du gate et le dernier verdict
**historique**, sans en déduire de nouvelle autorisation de MEP.
L'action **Ouvrir** affiche le registre et sa décision archivée.
L'action **Délier** supprime seulement le lien, sans supprimer le gate.

## 3. Verdicts humains et instantanés

Les verdicts sont **GO**, **GO sous réserves**, **NO-GO** et **Reporté**.
Ils ne sont attribués ni par IA, ni par présence d'un document, ni
par état d'un jalon ou d'une tâche.

Toute décision exige auteur, périmètre, justification explicite et
qualification de chaque critère : **Conforme**, **Écart**, **Inconnu**
ou **Non applicable**.

- **GO** : au moins un critère, au moins une preuve, aucun écart ni
  inconnu ; les critères obligatoires doivent être conformes.
- **GO sous réserves** : critères qualifiés, preuve et au moins une
  réserve identifiée. Les écarts et inconnues restent apparents.
- **NO-GO / Reporté** : justification toujours obligatoire, même
  lorsque les preuves ne sont pas encore disponibles.

Pour chaque preuve sélectionnée, Nodalis calcule le SHA-256 des octets
du fichier local (taille maximale de 25 Mio pour cette tranche).
Le registre mémorise le chemin relatif et l'empreinte. Aucun fichier
n'est importé, copié ou envoyé à une IA. Changer un fichier par la suite
ne modifie jamais la décision et son ancienne empreinte.

Un GO ne termine pas un jalon, un GO sous réserves ne lève aucune
réserve et un verdict ne rend pas le Parcours techniquement bloquant.
Les réserves et les décisions sont des objets distincts.

## 4. Levée et réouverture des réserves — deuxième tranche

Après une décision **GO sous réserves**, chaque réserve porte un GUID
propre et reste **ouverte** jusqu'à l'ajout d'un événement humain.

La décision d'origine et son verdict ne sont **jamais réécrits**.
Le registre conserve désormais, dans le champ JSON facultatif
`ReservationEvents`, une liste **append-only** d'événements distincts :

- `Lifted` : levée par une personne identifiée, avec motif détaillé,
  horodatage UTC, GUID d'événement et **au moins une preuve locale**
  dont le SHA-256 est figé au moment de la qualification.
- `Reopened` : réouverture par une personne identifiée avec motif
  détaillé ; une nouvelle preuve peut être ajoutée mais reste facultative.
- L'état courant n'est pas stocké dans la décision : il est **projeté**
  à partir de l'historique. Sans événement, la réserve est ouverte ;
  après `Lifted`, elle est levée ; après `Reopened`, elle est ouverte.

Les transitions répétées et incohérentes (lever deux fois, réouvrir
une réserve déjà ouverte), les GUIDs orphelins, les preuves dangereuses
et les événements modifiés ou supprimés sont rejetés. Les événements
sont ajoutés uniquement après **confirmation humaine** et sauvegarde
optimiste SHA-256. Les anciens registres, dépourvus de
`ReservationEvents`, restent lisibles et affichent leurs réserves
initiales comme ouvertes, sans migration implicite.

### Qualification via WPF

Dans **Gates…**, sélectionner le gate puis **la décision historique**
qui contient la réserve. La zone **RÉSERVES · état courant calculé**
affiche l'état et le dernier auteur. Sélectionner la réserve :

1. Pour une réserve ouverte, cliquer **Lever la réserve…**.
   Renseigner l'auteur, un motif d'au moins 10 caractères et
   sélectionner un fichier de preuve existant du workspace.
2. Pour une réserve déjà levée, cliquer **Réouvrir la réserve…**.
   Renseigner l'auteur et le motif, et éventuellement une nouvelle preuve.
3. Contrôler la demande dans la boîte de confirmation, puis valider.
   Le dossier de décision reste sélectionné. L'historique séparé
   conserve chaque acteur, motif, date et empreinte de preuve.

La levée n'implique **ni GO définitif, ni clôture du jalon, ni validation
automatique d'une MEP**. Une réouverture ne change pas le verdict
historique GO sous réserves. La synchronisation d'une éventuelle tâche
ou d'un risque lié à la réserve restera un développement séparé, jamais
une transformation automatique de verdict.

## 5. Tests et qualification Windows

La suite ProjectGateRegistrySmokeTests est branchée dans
tests/Nodalis.SmokeTests/Program.cs. Elle vérifie la lecture sans
écriture, les standards facultatifs, les GUID stables, les preuves
SHA-256, les chemins hors workspace, les gros fichiers refusés, les
GO sans preuve/inconnu refusés, les GO conditionnels historisés, les
révisions concurrentes, l'immutabilité des décisions, les références
non propriétaires et la conservation des anciennes empreintes.
Elle couvre également la lecture de l'ancien JSON sans historique,
le refus de lever sans preuve, le rejet des transitions répétées
ou vers une réserve étrangère, la levée/réouverture, le refus
d'effacer ou de modifier un ancien événement, les conflits SHA et
la conservation du verdict initial après relecture du registre.

Qualification visuelle restant à effectuer :
- Ouvrir sans créer de registre, puis proposer les cinq standards.
- Ajouter un critère, tenter un GO sans preuve ou avec inconnu,
  puis enregistrer une décision positive après une vraie revue.
- Ajouter un autre critère et enregistrer un GO sous réserves avec
  un inconnu, une réserve et une preuve.
- Vérifier que la première décision conserve ses critères, son
  verdict et son SHA même lorsque le gate ou le document a changé.
- Lier, ouvrir, délier un gate depuis le Parcours : aucun document
  ni état du jalon ne doit être affecté.
- Sur une décision GO sous réserves, sélectionner une réserve
  **Ouverte**, la lever avec motif et preuve, vérifier le verdict intact.
  Réouvrir avec un autre motif puis vérifier la chronologie des deux
  événements et les SHA figés ; retenter une transition identique :
  elle doit être refusée.
- Recharger/redémarrer et vérifier l'état courant calculé, la
  sélection du dossier, les projets archivés et les conflits SHA.
- Tester conflit SHA externe, archive/lecture seule, DPI, fenêtre
  étroite, gros workspace et changement de documents.

## 6. Vue transversale des réserves du projet — troisième tranche

La fenêtre **Gates…** propose **Vue des réserves…**. Cette commande
charge une fois le registre canonique du projet courant, puis construit
une projection **en lecture seule** des réserves de toutes les décisions
historiques, tous gates confondus.

L'écran affiche les compteurs **Ouvertes**, **Levées**, **En retard**
et **Sans responsable**. La recherche textuelle porte sur la
description, le gate et le responsable. Les filtres permettent de
voir uniquement les réserves ouvertes, levées, en retard ou ouvertes
sans responsable. Une réserve levée n'est pas comptée en retard.

Chaque ligne indique description, gate, responsable, date cible et état
projeté. Le détail conserve les GUID du gate, de la décision et de la
réserve, le verdict historique et le dernier événement de qualification.
**Ouvrir le dossier…** ou le double-clic revient à la décision d'origine
dans le registre et sélectionne la réserve correspondante : aucune
décision ni tâche n'est créée.

### Contrat de performances

- Un seul chargement explicite du registre ; aucun balayage du Worktree
  ni accès disque pendant recherche, filtre, sélection ou défilement.
- ProjectGateReservationOverviewBuilder valide une seule fois le
  registre et projette les événements en une passe indexée par
  (DecisionId, ReservationId), puis trie les lignes.
- Ordre : ouvertes, en retard, échéance la plus proche, décision la
  plus récente, GUID de départage.
- Le dépassement d'échéance dépend de la date locale fournie au calcul.
  Une réserve est en retard uniquement si elle est ouverte et si sa
  date cible est strictement antérieure au jour ; ce statut n'est pas
  enregistré comme modification de décision.

ProjectGateReservationOverviewSmokeTests vérifie la projection sur
plusieurs gates et décisions, les réouvertures successives, le comptage
sans doublon, les échéances, l'absence de responsable et l'immutabilité
des décisions. Ce test est branché à la suite smoke Windows.

### Liens vers tâches, incidents et risques

Les tâches Markdown sont actuellement identifiées à partir de leur
document, leur ligne et leur texte, ce qui peut être fragile lors
d'une réorganisation. Les risques sont souvent de simples documents
Markdown sans ID métier stable. Certains incidents structurés disposent
de leur propre identifiant, mais nécessitent une résolution contextualisée.

Aucun lien métier fragile n'est créé dans cette tranche. Les futurs
liens réserve → tâche / risque / incident exigeront un identifiant
garanti ou une provenance réconciliable, avec diagnostics des références
déplacées ou ambiguës. La vue transversale actuelle concerne le
**projet ouvert**, et non un index global recalculé à chaque frappe.

### Qualification humaine

1. Créer des décisions sur plusieurs gates, avec échéances
   passées/futures et responsables renseignés/non renseignés.
2. Lever et réouvrir une réserve ; vérifier les quatre compteurs.
   Une réserve levée ne doit plus être en retard.
3. Rechercher par description/gate/responsable et ouvrir son dossier ;
   vérifier la bonne décision et la bonne réserve sélectionnées.
4. Tester grande liste, fenêtre étroite, DPI, projet archivé/lecture
   seule, et fermeture du dialogue sans mutation.

## 7. Liens guidés gates ↔ jalons (01/11/2026)

Le gate permet maintenant d'**associer** et **dissocier** un jalon
existant du projet depuis le registre WPF. Le sélecteur est partagé
avec le Parcours : seules les lignes avec un GUID réellement persisté,
unique et provenant du projet peuvent être liées. Les anciennes lignes
sans ID et les GUID dupliqués ne sont pas modifiés automatiquement.

- L'affichage initial n'effectue aucun scan des Markdown. Le bouton
  **Actualiser les jalons** (ou **Associer un jalon**) effectue une seule
  lecture explicite ; noms/statuts et références devenues ambiguës
  sont alors affichés.
- `ProjectGateRegistryService.SaveAsync` revalide les GUID **nouveaux**
  avant écriture, dans une seule consultation du catalogue. Une
  référence étrangère, absente, non persistée ou ambiguë est refusée.
  Le contrôle de concurrence SHA-256 du registre reste obligatoire.
- Une relation existante peut devenir obsolète après réorganisation
  du fichier : elle est conservée avec diagnostic pour permettre une
  dissociation ou une édition non liée. Aucun GUID n'est remplacé
  automatiquement ; la source du jalon reste intacte.
- La relation est non propriétaire : ni jalon terminé, ni nouvel
  historique GO, ni changement de statut du Parcours. Aucun
  verrou multi-fichiers n'est prétendu entre le contrôle de la source
  Markdown et l'écriture JSON.

`ProjectGateMilestoneLinksSmokeTests` couvre les GUID uniques,
doublons, références étrangères/inventées, idempotence, relecture,
lien obsolète, édition et dissociation sans mutation du jalon.
À qualifier visuellement sous Windows : fenêtre étroite, DPI, clavier,
confirmations, archivage et retour à l'état sans lien.

## 8. Première tranche des relations réserves ↔ incidents (01/11/2026)

Une réserve d'un **GO sous réserves** peut être reliée manuellement à
un incident structuré `INC-<GUID>` de la **Fiche de Maintenance.md**
du même projet. L'action est déclenchée depuis **Incidents liés…** sur
la réserve sélectionnée : liste des incidents admissibles, diagnostic
des sources manquantes, auteur et justification exigés, puis
**confirmation distincte** avant la sauvegarde.

- La décision, ses réserves initiales, les statuts de jalons et la
  fiche Markdown sont inchangés. Les actions de lien et dissociation
  sont enregistrées dans le registre via `IncidentLinkEvents`, une
  chronologie **append-only** avec identités stables, acteur, motif
  et horodatage. Ancien JSON sans champ : liste vide.
- L'index des candidats lit **une seule fiche connue, uniquement à la
  demande**. Aucun parcours récursif du workspace, ni rafraîchissement
  à chaque frappe ou scroll. Les enregistrements incomplets, GUID
  dupliqués ou étrangers au projet sont exclus. L'accès aux fichiers
  suit les contrôles de confinement existants.
- Le service de sauvegarde requalifie **tous les nouveaux événements
  d'association**, à partir des incidents réellement présents, avant
  de persister. Le contrôle SHA-256 et l'interdiction de modifier les
  anciens événements sont toujours en vigueur. Un lien devenu obsolète
  est visible et **peut être dissocié**, sans réécriture historique ni
  blocage des autres modifications du registre.
- Aucun incident n'est clos ni ne provoque la levée d'une réserve ;
  l'humain conserve une décision de levée avec preuve distincte.
  Les tâches checkbox et les dossiers de risques étaient
  historiquement sans identité relationnelle fiable ; ces identités
  ont maintenant un contrat distinct dans la version 2.0.
  Les liens risques ↔ réserves utilisent exclusivement les GUIDs
  de fiche RIS et une validation humaine décrite dans
  [v2-gate-risk-links.md](v2-gate-risk-links.md).

La suite `ProjectGateIncidentLinksSmokeTests` vérifie les GUID
canoniques, le refus d'une provenance inter-projet, la non-mutation
des sources, les liens dupliqués, la suppression historique interdite,
la désassociation et le refus de relier un incident disparu.

**Recette visuelle Windows restante :** présence/absence de fiche,
projet archivé en consultation, sélection au clavier et au DPI élevé,
rafraîchissement du registre et conservation du verdict.

## 9. Risques canoniques et réserves de Gates (01/11/2026)

Le nouveau lien **réversible, historisé et soumis à confirmation**
associe les GUIDs de `(décision, réserve, risque RIS)`. Depuis
une réserve de Gate, **« Risques liés… »** affiche les fiches
canoniques et autorise `Linked/Unlinked` avec auteur et motif.
L'enregistrement valide l'unicité et la propriété projet des
nouveaux RIS, préserve les événements passés et utilise le SHA
du registre Gate. Une cible absente peut être dissociée.

La fiche RIS propose à son tour **« Réserves Gates… »**, projection
en lecture seule distinguant le statut de la relation, la levée
de la réserve et le verdict initial. Le bouton **« Ouvrir le Gate »**
revient à la décision et à la réserve historisées. Depuis le Gate,
**« Ouvrir la fiche RIS »** navigue vers le Markdown sécurisé.

Ces liens ne valident ni les Gates, ni les risques, ni les tâches,
ni les mesures, ni les levées de réserve. Voir
[contrat complet et recette Windows](v2-gate-risk-links.md).

## 10. Synthèse transversale des réserves et des risques (02/11/2026)

Le registre **Gates → Synthèse réserves / risques…** permet une
consultation projet en lecture seule. Deux lectures explicitement
demandées (registre Gate et catalogue RIS canonique) produisent
une projection indexée ; **filtrer, chercher, sélectionner et défiler
ne provoquent aucun nouveau scan du workspace ou du Worktree**.

La synthèse distingue les réserves ouvertes/levées, les réserves
ouvertes sans risque associé, les RIS de lien actif dont la source
est supprimée/invalide/dupliquée, les mesures humainement déclarées
bloquées/non terminées, et les scores initiaux/résiduels non évalués.
Une évaluation absente ne devient jamais une criticité « faible ».
Chaque fiche RIS affiche le nombre de **références GUID de tâches
liées au plan courant** : ce nombre n'indique ni leur existence
actuelle, ni leur état d'exécution.

Les boutons **« Ouvrir le dossier Gate »** et
**« Ouvrir la fiche RIS »** naviguent uniquement vers les
identités historiques ou les sources existantes et vérifiées.
Aucun lien n'est créé/levé automatiquement. Les anciennes
dissociations restent historisées mais ne sont pas comptées
comme des liens actifs. Les réserves levées conservent leurs
risques liés tant qu'aucun avis humain distinct ne les dissocie.

La suite `ProjectGateRiskOverviewSmokeTests` vérifie la synthèse
en projet réel : réserves ouvertes et levées, RIS bloqués et non
évalués, tâche liée par GUID, absence de RIS, suppression et
duplication de source, dissociation ciblée, et
**absence d'écriture lors des projections**.

Voir [contrat et qualification de cette vue](v2-gate-risk-overview.md).

## 11. Travaux restants (#202)

Les incréments actuels n'épuisent pas l'EPIC. Restent notamment :
l'extension éventuelle de la vue à tout le workspace, les liens
vérifiés vers tâches (distincts des RIS), la qualification
visuelle des liens guidés Gates/jalons/risques, les
synthèses issues des futurs registres exigences/risques/tests/incidents
(#198–#201), les requalifications conditionnelles au
périmètre/version/procédure/rollback/environnement et la qualification
finale des interactions WPF.

**Aucun verdict automatique, aucune réécriture de décision historique,
aucune fermeture de jalon, aucun merge ou tag 2.0 avant recette.**


## Requalification consultative d'un GO avant une nouvelle tentative (#202 → #203)

Le moteur Core ProjectGateReuseReview.Assess compare une décision historique
sélectionnée par GUID avec le périmètre exact, la version, l'environnement
et les empreintes SHA-256 **actuelles** de toutes les preuves figées.
Il n'écrit aucun fichier, ne clôture pas les jalons et ne modifie aucun
verdict. Les comparaisons de version et de périmètre ne sont jamais floues.

- NO-GO / Reporté, contexte historique incomplet, changement de version,
  d'environnement ou de périmètre, preuve non revue, modifiée, ajoutée ou
  absente imposent une nouvelle décision humaine.
- Le GO sous réserves conserve les réserves ouvertes dans la prévisualisation.
  Une levée ou réouverture explicite change le diagnostic actuel, jamais
  le verdict d'origine.
- Même avec WithinHistoricalConditions=true, l'attribut
  ExplicitHumanApprovalRequired est **toujours vrai** : il ne constitue
  jamais une autorisation de déployer ou de réutiliser automatiquement un GO.
- L'appelant doit capturer les empreintes à partir des vrais fichiers du
  workspace. Un inventaire non fourni vaut « preuves non recontrôlées ».
- Le registre Gate historique ne fige pas encore l'intégralité des
  conditions MEP (procédure, rollback, état réel, incidents, risques,
  durée de validité). Elles restent à examiner **séparément et humainement**
  sous #203, même si le contexte vérifiable concorde.

La suite ProjectGateReuseReviewSmokeTests vérifie les cas de modification
du contexte, des preuves, de NO-GO, de réserves ouvertes/levées/réouvertes,
de données historiques partielles et d'inventaires ambigus. L'UI de revue
MEP et son journal de décision explicite restent à développer.


### Accès à la comparaison depuis l'interface Gates

Dans le **registre Gates**, sélectionner une décision historique, puis
cliquer **Réexaminer ce GO…**. La fenêtre affiche son dossier figé,
préremplit la version, l'environnement et le périmètre, que l'utilisateur
peut remplacer par le contexte envisagé. **Comparer les sources**
réouvre le registre et vérifie son SHA avant et après la capture des
preuves locales, sans nouveau scan global du workspace. Un changement
concurrent bloque le diagnostic. Les preuves non accessibles sont
signalées comme manquantes, jamais comme conformes.

Cette comparaison est disponible aussi en **lecture seule / archive**,
ne produit aucune écriture et ne crée pas d'autorisation de déploiement.
Les conditions de procédure, rollback, incidents et risques restent une
étape de qualification humaine distincte avant #203.
