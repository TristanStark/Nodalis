# Nodalis 1.9 — Réimport DOCX : transaction et récupération (#164)

## Statut

Le service `DocxReimportApprovalService` relie désormais le comparateur
trois voies à `DocxReimportTransactionService`, **sur `develop`**. Le bouton
**Préparer le réimport** ne modifie rien : il recharge le manifeste réellement
présent sur disque, compare les fichiers actuels, vérifie tous les arbitrages et
construit une approbation mémorisée (hash du plan, du DOCX temporaire et du
manifeste parent).

Le bouton **Appliquer le plan confirmé**, initialement désactivé, n'est
accessible qu'après préparation. Une fenêtre de confirmation liste les
fichiers Markdown et leurs empreintes avant/après, les nouvelles sections
explicitement exclues et la copie Word/manifeste à créer. Seul un choix
**Oui** déclenche le commit. Immédiatement avant de modifier le workspace,
les fichiers et le Word sont **relus**. Si la projection diffère du plan
approuvé, l'opération est refusée sans écriture.

L'onglet contient aussi les commandes indépendantes d'inspection et de
restauration des journaux interrompus.

## Contrat d'écriture

Ce service prend uniquement un `DocxReimportDecisionPlan` déjà complètement
résolu. L'appelant fournit :

- le projet existant et son identifiant Nodalis canonique ;
- un identifiant d'opération GUID non nul ;
- l'empreinte du plan complet, renvoyée par `Fingerprint(plan)` et **approuvée
  explicitement** après consultation de la projection.

Le service refuse les plans incomplets, les avertissements non levés,
les sections nouvelles sans décision, les doublons de fragments, les
destinations étrangères au projet ou via liens symboliques, les fichiers
manquants et les empreintes SHA-256 obsolètes. Une nouvelle section Word ne
peut être ajoutée qu'après une **affectation explicite à un dossier existant**
du projet, sous un nom de fichier déterministe. Aucun fichier déjà présent
n'est remplacé par cette opération de création.

La transaction ne remplace que les Markdown explicitement projetés.
`CommitWithProvenanceAsync` ajoute dans la **même opération récupérable**
une copie DOCX immuable nommée `Imports/Sources/reimport-<GUID>.docx`
et le manifeste `.nodalis-imports/<GUID>.json` du projet. Elle vérifie
la provenance contre le plan approuvé et l'empreinte binaire du Word.
`CommitAsync` reste disponible pour les tests de transactions Markdown seules,
mais **n'est pas un chemin de réimport utilisateur complet**.

Le constructeur `DocxReimportProvenanceBuilder` prépare la génération suivante
du manifeste : `previousImportId` relie le parent, `reimportOperationId`
identifie l'opération, et chaque fragment conserve le snapshot Word récent
et l'action réellement choisie (`Word`, `Manual`, `Nodalis`). Le texte
Nodalis conservé ne devient pas faussement une mise à jour Word acceptée.
Les sections supprimées du Word restent consultables dans l'historique parent.

## Journal, sauvegarde, commit et rollback

Le répertoire **`.nodalis-reimport-transactions/<operationId>/`**, créé
dans le projet, conserve les fichiers `00000.bak`, etc. **octet par octet**
(y compris un éventuel BOM), ainsi que le journal `journal.json`.

L'ordre est :

1. Verrou exclusif `.nodalis-reimport-transactions/.lock` et contrôle de
   l'identité du projet.
2. Validation de toutes les destinations et de leurs SHA-256 avant écriture.
3. Écriture et synchronisation des sauvegardes binaires.
4. Écriture atomique du journal `Prepared`.
5. Vérification des SHA-256 et remplacement atomique des Markdown projetés.
6. Si l'opération inclut de nouvelles sections Word : création
   **sans écrasement** de leurs fichiers Markdown, tous recensés et hachés
   dans le journal `Prepared` avant leur création.
7. Création **sans écrasement** de la copie DOCX et du manifeste versionné,
   eux aussi enregistrés et hachés dans ce même journal.
8. Écriture atomique de l'état `Completed` seulement après les fichiers
   Markdown, les nouvelles sections et les deux artefacts de provenance.

En cas d'erreur, le service tente de remettre **toutes** les destinations à
leur état d'origine à partir des sauvegardes et marque `RolledBack` après
succès. La récupération `RecoverPendingAsync(projectDirectory)` détecte
les journaux `Prepared` après interruption ; elle refuse d'écraser un
changement tiers non reconnu : si un Markdown n'est plus identique **ni à
l'état d'origine ni à l'état projeté**, ou si une copie DOCX / un manifeste
créé a été modifié, la récupération demande une intervention manuelle
et **conserve les sauvegardes**. Les fichiers créés sont retirés uniquement
s'ils portent encore exactement l'empreinte enregistrée dans le journal.

Une même opération `Completed`, rejouée avec son GUID et le même plan,
ne réécrit rien : elle renvoie `AlreadyApplied` après contrôle des
empreintes de destination. Un GUID déjà utilisé avec un autre plan est rejeté.

Un arrêt **avant** la création du journal peut laisser un répertoire ne
contenant que des sauvegardes `.bak` ou le temporaire d'écriture JSON :
aucune destination n'a encore été modifiée à ce moment-là. Ces opérations
sans journal sont visibles dans `InspectAsync`, puis nettoyées par
`RecoverPendingAsync` après vérification des noms, de l'identifiant
d'opération et de l'absence de fichiers inconnus. Une arborescence suspecte
est conservée pour inspection humaine.

**Rétention** : pour cette première étape, les journaux et sauvegardes
restent locaux et ne sont pas purgés automatiquement. Leur nettoyage, la
réconciliation des imports et la reprise guidée constituent des étapes
ultérieures. Les sauvegardes peuvent contenir des informations sensibles et
doivent être protégées au même titre que le workspace. Les snapshots Word récents disposent également d'un
SHA-256 indépendant pour détecter toute altération de l'historique.

## Limites et prochaine intégration

Ce moteur assure un rollback local basé sur les états observables, **pas
une transaction atomique universelle sur plusieurs fichiers** : Windows
n'offre pas ici de remplacement instantané simultané des N Markdown. Une
interruption entre deux fichiers est détectée et récupérable au prochain
passage explicite de `RecoverPendingAsync`. Une écriture concurrente
externe peut encore intervenir entre la dernière vérification et le
remplacement atomique ; la résolution de tels conflits doit préserver la
copie de sauvegarde et exiger une intervention humaine.

La traçabilité, la copie Word versionnée et la confirmation explicite
sont maintenant reliées **de bout en bout**. Pour la qualification de sortie
#164, il reste à tester l'interface WPF en conditions Windows 10/11, les
permissions, les jonctions et la concurrence externe. L'affectation
de nouvelles sections Word à de nouveaux Markdown dans un dossier existant
est disponible. Leur intégration dans un Markdown préexistant n'est pas
encore proposée. Les associations ambiguës et les fragments
initialement importés partiellement continuent de **bloquer** l'application
pour éviter toute perte ou réintroduction de contenu non choisi.

Dans l'assistant, **Vérifier les transactions** liste sans écriture les états
`Prepared`, `Completed`, `RolledBack`. **Restaurer les opérations
interrompues** demande une confirmation puis lance `RecoverPendingAsync`.
Après récupération, les décisions et comparaisons en mémoire sont invalidées :
elles doivent être recalculées sur les fichiers restaurés.



## Utilisation dans Nodalis

1. Dans l'assistant DOCX, choisissez un **projet existant** et ouvrez
   **Réimport · comparer**. Chargez l'historique Word exact et cliquez
   **Comparer**.
2. Réglez les associations incertaines. Pour chaque section concernée,
   choisissez explicitement **Conserver Nodalis**, **Utiliser Word**,
   **Fusion manuelle** ou **Garder comme historique**. Une nouvelle section
   Word doit être **attribuée à un dossier existant** pour être ajoutée
   à un Markdown dédié, ou explicitement exclue du réimport.
3. Cliquez **Préparer le réimport**. Le plan est recalculé depuis les
   fichiers présents sur disque. Une modification depuis l'aperçu exige
   de recommencer. Le bouton d'application devient actif uniquement
   si tous les arbitrages sont complets.
4. Cliquez **Appliquer le plan confirmé**, relisez les fichiers et leurs
   empreintes, puis validez **Oui**. Le moteur journalise les sauvegardes
   avant toute modification et crée l'historique immuable.
5. Après succès, rechargez l'historique pour examiner la nouvelle version.
   En cas d'interruption, utilisez **Vérifier les transactions** puis
   **Restaurer les opérations interrompues** avec confirmation.

Les approbations sont **éphémères** : modifier un choix, une association,
le texte d'une fusion, l'historique ou la comparaison désactive l'application.
Un plan approuvé est lié à son GUID ; rejouer une transaction déjà terminée
vérifie son journal et ses empreintes finales sans réinterpréter le Markdown
écrit lors du premier commit.

## Régressions intégrées

`DocxReimportTransactionSmokeTests` vérifie : approbation obligatoire,
fichier extérieur refusé, transaction à deux destinations, relecture
idempotente, fichier modifié après le plan, erreur injectée après une
première écriture et rollback, restauration d'un état `Prepared` simulé,
conservation exacte du BOM et conservation d'une modification tiers au
moment de la récupération. Les tests sont exécutés dans la suite smoke
sans dépendances additionnelles.

La suite `DocxReimportReviewSmokeTests` vérifie également le commit
conjoint du Word, des Markdown et du manifeste, l'idempotence, la restauration
des artefacts après une erreur injectée en fin de transaction et la
version d'historique sans écriture Markdown (`Conserver Nodalis`).
`DocxReimportTransactionSmokeTests` couvre une interruption simulée après
création du DOCX mais avant celle du manifeste, ainsi que leur nettoyage
par la récupération.


La suite couvre maintenant le refus d'une destination de nouvelle section
occupée entre préparation et commit, le rejeu idempotent après création,
l'inscription de la nouvelle section dans l'historique chaîné et le rollback
d'une nouvelle section ainsi que de sa copie DOCX et de son manifeste à
la suite d'une erreur injectée.
