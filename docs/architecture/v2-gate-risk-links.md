# Nodalis 2.0 — relations entre réserves de Gates et risques RIS

**Statut :** développé sur `develop`. Les décisions Go/No-Go, levées de
réserves et évaluations de risques restent **strictement humaines**.
Les opérations de consultation ne modifient aucune source ; la recette
visuelle Windows reste à effectuer avant toute release 2.0.

## 1. Identifiants et périmètre

Une association référence exactement trois GUID stables :

- `DecisionId` : l'identifiant immuable de la **décision humaine de Gate**
  dans laquelle la réserve a été créée.
- `ReservationId` : le GUID de la réserve initialement formulée dans
  ce dossier de décision.
- `RiskId` : le GUID canonique d'une fiche structurée
  `Analyse/Risques/RIS-xxxx.md`, dans **le même projet**.

Un tableau legacy `Risques.md`, le nom d'un risque ou son numéro
`RIS-0001` **ne constituent pas des identifiants relationnels**.
Les entrées illisibles, malformées, de projets étrangers ou avec
un GUID/référence en doublon sont exclues par le catalogue RIS.

Une association ne transporte ni l'état des tâches liées au risque,
ni l'avancement des mesures de traitement. Elle ne convertit jamais
un événement d'exécution en décision de validation du Gate.

## 2. Flux utilisateur — depuis les Gates

Dans **Parcours → Gates**, sélectionner le Gate, puis la décision
historique et la réserve. Le bouton **« Risques liés… »** propose :

- **Associer** un risque canonique du même projet, après saisie de
  l'auteur et d'un motif d'au moins dix caractères.
- **Dissocier** un risque actuellement associé, après justification
  humaine. La dissociation reste possible si la fiche RIS a été
  déplacée, supprimée ou est devenue invalide ; son historique
  est conservé.
- **Ouvrir la fiche RIS** d'un risque effectivement résolu. Le
  document s'ouvre dans l'éditeur Markdown via la navigation
  sécurisée du Parcours, sans lier ou délier automatiquement.
  Une source orpheline n'est jamais proposée comme cible.

Une **seconde confirmation** précède la sauvegarde : elle présente
le risque, le GUID de décision, la réserve, l'auteur et la
justification. L'écriture affecte **uniquement** le registre
`.project-gates.json`. Le panneau des réserves indique le nombre
de risques actuellement associés, calculé en mémoire depuis les
événements, sans rescanner les fichiers à chaque sélection.

## 3. Historique append-only et sauvegarde sûre

La version du schéma Gate reste `1` pour préserver les registres
existants. Le champ JSON facultatif (vide par défaut)
`RiskLinkEvents` est une liste d'événements
`ProjectGateReservationRiskLinkEvent`. Chaque événement contient :

`Id, DecisionId, ReservationId, RiskId, Kind, Actor, Rationale, RecordedUtc`

`Kind` vaut `Linked` ou `Unlinked`, avec refus des transitions
sans changement. La validation rejoue l'historique en vérifiant
la décision et sa réserve d'origine, les GUID, l'auteur, la
justification, le sens de la transition et les dates.

`ProjectGateRegistryService.SaveAsync` exige le **SHA-256 exact**
de la version affichée et contrôle que les événements anciens n'ont
été ni effacés, ni modifiés, ni réordonnés. Un nouvel événement
`Linked` exige une fiche RIS **encore existante et unique** dans
le projet à l'instant de la sauvegarde. L'événement `Unlinked`
ne dépend pas de la disponibilité de la source, pour permettre
la résolution manuelle d'une référence orpheline.

Aucune lecture de registre ne migre, ne réécrit ni ne crée de
fiches RIS. Les anciens registres ne contenant pas
`RiskLinkEvents` restent compatibles et lisibles.

## 4. Navigation inverse — depuis une fiche RIS

Dans **Risques…**, sélectionner la fiche et ouvrir
**« Réserves Gates… »**. Cette action relit à la demande la
source canonique du risque et le registre Gate du même projet.
Elle liste les associations :

- actives ou historiquement dissociées ;
- portant sur une réserve ouverte ou levée **indépendamment** ;
- avec le nom figé du Gate, le verdict initial, le GUID de décision,
  la description de la réserve et la dernière date de liaison.

**« Ouvrir le Gate »** revient au registre habituel en ciblant
le Gate, la décision et la réserve de départ par leurs GUIDs.
Le statut de lien ne requalifie jamais une réserve levée, et le
statut de réserve ne crée ni ne supprime de lien. Aucun changement
d'état du risque ou de ses mesures n'est déduit d'une décision.

## 5. Qualification et limites

`ProjectGateRiskLinksSmokeTests` couvre la création d'un GO sous
réserves explicitement validé, la liaison à deux risques du même
projet, le refus d'un risque étranger ou dupliqué, la sauvegarde
optimiste, la conservation des décisions et des événements,
la dissociation d'un GUID devenu orphelin, et l'indépendance
entre la **levée explicite** d'une réserve et la présence du
lien au risque. Les backlinks du RIS sont aussi vérifiés.

Recette visuelle Windows à réaliser : ouvrir un ancien registre
sans liens, associer puis dissocier, ouvrir RIS et revenir au
Gate d'origine, vérifier archives en lecture seule, fenêtre
étroite, DPI, navigation clavier et plusieurs documents déjà
ouverts. Vérifier l'absence de déplacement involontaire du
Worktree et l'absence de réactualisation en boucle.

**À poursuivre :** synthèse transversale réserves/risques,
alertes sur cibles RIS disparues, liens vers incidents et tâches
via leurs GUID persistants, gestion des preuves du déploiement,
et requalification conditionnelle par décision humaine.

**Aucun merge vers `main`, tag ou release 2.0 avant la qualification.**
