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

## 6. Travaux restants (#202)

Ces deux tranches n'épuisent pas l'EPIC. Restent notamment :
la vue transverse des réserves par projet, leurs liens vers tâches,
risques et incidents avec responsables/échéances, les associations
guidées avec jalons dont le GUID est réellement persisté, les
synthèses issues des futurs registres exigences/risques/tests/incidents
(#198–#201), les requalifications conditionnelles au
périmètre/version/procédure/rollback/environnement et la qualification
finale des interactions WPF.

**Aucun verdict automatique, aucune réécriture de décision historique,
aucune fermeture de jalon, aucun merge ou tag 2.0 avant recette.**
