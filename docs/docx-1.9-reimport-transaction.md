# Nodalis 1.9 — Réimport DOCX : transaction et récupération (#164)

## Statut

Le service `DocxReimportTransactionService` est implémenté sur `develop`
comme **composant interne en cours de qualification**. Il **n'est pas
branché au bouton de validation de l'assistant DOCX**. Le comparateur Word /
Nodalis reste en lecture seule pour les utilisateurs pendant la qualification
de la provenance après réimport, des sections nouvelles et de l'interface de
confirmation.

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
manquants et les empreintes SHA-256 obsolètes. Aucune création silencieuse
de section Word ni de fichier Markdown de destination n'est autorisée.

La transaction garde uniquement les écritures de Markdown explicitement
proposées ; les autres fichiers du workspace restent inchangés. Elle vérifie
le projet sur disque et les empreintes des fichiers avant toute écriture,
puis à nouveau juste avant le remplacement de chaque destination.

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
6. Écriture atomique de l'état `Completed` après le dernier fichier.

En cas d'erreur, le service tente de remettre **toutes** les destinations à
leur état d'origine à partir des sauvegardes et marque `RolledBack` après
succès. La récupération `RecoverPendingAsync(projectDirectory)` détecte
les journaux `Prepared` après interruption ; elle refuse d'écraser un
changement tiers non reconnu : si un fichier n'est plus identique **ni à
l'état d'origine ni à l'état projeté**, la récupération demande une
intervention manuelle et **conserve les sauvegardes**.

Une même opération `Completed`, rejouée avec son GUID et le même plan,
ne réécrit rien : elle renvoie `AlreadyApplied` après contrôle des
empreintes de destination. Un GUID déjà utilisé avec un autre plan est rejeté.

**Rétention** : pour cette première étape, les journaux et sauvegardes
restent locaux et ne sont pas purgés automatiquement. Leur nettoyage, la
réconciliation des imports et la reprise guidée constituent des étapes
ultérieures. Les sauvegardes peuvent contenir des informations sensibles et
doivent être protégées au même titre que le workspace.

## Limites et prochaine intégration

Ce moteur assure un rollback local basé sur les états observables, **pas
une transaction atomique universelle sur plusieurs fichiers** : Windows
n'offre pas ici de remplacement instantané simultané des N Markdown. Une
interruption entre deux fichiers est détectée et récupérable au prochain
passage explicite de `RecoverPendingAsync`. Une écriture concurrente
externe peut encore intervenir entre la dernière vérification et le
remplacement atomique ; la résolution de tels conflits doit préserver la
copie de sauvegarde et exiger une intervention humaine.

Avant d'activer l'interface de commit, il reste à versionner le nouvel
historique de provenance et la copie du Word réimporté, relier cette
transaction aux approbations utilisateur, traiter l'affectation explicite
des nouvelles sections et qualifier la récupération Windows 10/11, les
permissions, les liens, le verrou et le crash pendant le commit.

## Régressions intégrées

`DocxReimportTransactionSmokeTests` vérifie : approbation obligatoire,
fichier extérieur refusé, transaction à deux destinations, relecture
idempotente, fichier modifié après le plan, erreur injectée après une
première écriture et rollback, restauration d'un état `Prepared` simulé,
conservation exacte du BOM et conservation d'une modification tiers au
moment de la récupération. Les tests sont exécutés dans la suite smoke
sans dépendances additionnelles.
