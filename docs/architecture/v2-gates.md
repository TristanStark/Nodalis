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

## 4. Tests et qualification Windows

La suite ProjectGateRegistrySmokeTests est branchée dans
tests/Nodalis.SmokeTests/Program.cs. Elle vérifie la lecture sans
écriture, les standards facultatifs, les GUID stables, les preuves
SHA-256, les chemins hors workspace, les gros fichiers refusés, les
GO sans preuve/inconnu refusés, les GO conditionnels historisés, les
révisions concurrentes, l'immutabilité des décisions, les références
non propriétaires et la conservation des anciennes empreintes.

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
- Tester conflit SHA externe, archive/lecture seule, DPI, fenêtre
  étroite, gros workspace et changement de documents.

## 5. Travaux restants (#202)

Cette tranche n'épuise pas l'EPIC. Restent notamment : un cycle de vie
explicite pour lever/réouvrir des réserves avec identité humaine et
historique, les associations guidées avec jalons dont le GUID est
réellement persisté, les synthèses issues des registres futurs
exigences/risques/tests/incidents (#198–#201), les requalifications
conditionnelles au périmètre/version/procédure/rollback/environnement,
ainsi que la qualification finale des interactions WPF.

**Aucun verdict automatique, aucune réécriture de décision historique,
aucune fermeture de jalon, aucun merge ou tag 2.0 avant recette.**
