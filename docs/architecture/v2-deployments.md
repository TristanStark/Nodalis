# Nodalis 2.0 — Déploiements multi-tentatives (#203)

**État :** contrat Core et première persistance sur develop. Aucune
exécution technique de déploiement, aucune release 2.0.

## Une opération, plusieurs tentatives

Un projet conserve une fiche Markdown canonique
Exploitation/Déploiement/Opérations/MEP-xxxx.md par opération.
Le bloc JSON journalisé (marqueur nodalis:deployment-operation:v1)
conserve le GUID de MEP, le propriétaire, les tentatives et
les événements. Aucun second registre de source métier.

- Une consultation se limite au dossier canonique (maximum 500 fichiers)
  et ne crée aucun répertoire.
- La création exige une action et une référence MEP-0001..9999
  recontrôlée, sans écrasement d'un fichier existant.
- La sauvegarde recalcule le SHA-256 des octets lus et effectue une
  écriture atomique conditionnée à cette révision. Ni un instantané
  de tentative ni un événement ancien ne peut être réécrit ou supprimé.
- Fichiers illisibles, GUID dupliqués, sources hors projet, révisions
  concurrentes, arborescences sous liens symboliques et archives
  sont traités sans mutation silencieuse.

## État réel et rollback

Chaque tentative fige une version réellement constatée avant MEP,
la version cible, le périmètre, l'environnement, les SHA-256 des
procédures de déploiement et retour, la décision GO historique
et la relecture humaine de cette décision.

Événements distincts et append-only :
ActionLogged, RollbackRequested, RollbackStarted, RollbackObserved,
FinishedSucceeded, FinishedFailed et FinishedInterrupted.
Un rollback observé garde son résultat
(Succeeded, Partial, Failed, Interrupted) et la version constatée.

Une demande de rollback n'est ni son démarrage ni sa confirmation.
Une tentative ayant demandé un rollback ne peut pas être déclarée
réussie, même en cas de retour en arrière réussi. Une seconde tentative
nécessite la clôture explicite de la précédente et un nouvel état
réel saisi, jamais déduit de l'ancienne version cible.

## Réutilisation prudente d'un GO

À l'enregistrement de chaque nouvelle tentative, le service vérifie
la décision Gate dans le même projet, son verdict d'approbation et
qu'aucune décision plus récente du même gate ne l'a remplacée.
Il compare l'environnement, la version et le périmètre exacts,
réévalue les empreintes de toutes les preuves Gate et re-hache les
procédures de déploiement et rollback.

Ce contrôle **ne produit pas d'autorisation automatique** :
les incidents, les risques, l'état réel de l'environnement, les
conditions opérationnelles et l'étendue de la revue doivent encore
être examinés par une personne. Le statut d'un jalon et les décisions
historiques sont indépendants.

## Régressions et étapes restantes

ProjectDeploymentSmokeTests couvre les tentatives, les événements,
les retours partiels, les champs obligatoires, le format canonique
et les historiques immuables. ProjectDeploymentPersistenceSmokeTests
vérifie le workspace synthétique, la création, la relecture,
les écrasements refusés, les conflits SHA, le GO sur version différente
et la modification des procédures.

À construire : interface WPF de préparation/journal/bilan, contrôle
transversal incidents/risques, preuve du contexte MEP, stabilisation
et passation exploitation. Qualification Windows et gate #205 nécessaires
avant merge main, tag et release.


## Interface WPF de première tranche

Dans Pilotage > Mises en production · 2.0 >
Opérations MEP et tentatives, le registre permet de consulter la liste
canonique, l'historique des tentatives et les événements (y compris
demande, démarrage et constat du rollback), puis d'ouvrir la fiche
Markdown dans l'éditeur Nodalis.

Le bouton Créer une MEP demande un intitulé et une confirmation distincte
avant d'ajouter un unique document sans tentative et sans autorisation.
Actualiser relit explicitement le dossier ; les listes et les détails
sont recalculés en mémoire sans rescans à la sélection ou au scroll.
Le mode archive/lecture seule conserve uniquement la consultation.
L'UI n'implique ni GO automatique ni exécution réelle de déploiement.

La saisie assistée des tentatives, la capture du contexte de mise en
production, les contrôles d'incidents/risques et le bilan demeurent
les incréments suivants de #203.


### Saisie guidée depuis le registre MEP

Le registre WPF comprend désormais Nouvelle tentative et Consigner
un événement, activés seulement quand la transition est pertinente.
Pour une tentative, l'utilisateur choisit une décision GO historique
du projet, confirme les versions et le périmètre, saisit la version
réellement constatée avant la MEP, choisit les procédures depuis le
workspace et renseigne les auteurs ainsi que les motifs explicites.
Les empreintes des fichiers sont capturées au commit ; le service
recontrôle le GO et refuse un historique obsolète ou altéré.

L'événement est consigné séparément (action, demande de rollback,
démarrage, constat, réussite/échec/interruption). Un résultat de
rollback et la version réellement constatée ne sont pas inventés :
la personne les saisit explicitement. La validation Core rejette
les ordres impossibles et les réécritures ; le service refuse les
révisions concurrentes. Une action UI n'est jamais un déploiement réel.


### Contrôles pré-MEP consultatifs

Dans **Projet > Mises en production…**, sélectionner une MEP avec
au moins une tentative puis choisir **Contrôles pré-MEP…**. Le service
inspecte uniquement les registres canoniques du projet, à la demande.
Il vérifie l'empreinte SHA de la MEP et du registre des gates avant
et après l'inspection, le dernier verdict GO et ses preuves, la
procédure de déploiement et le plan de rollback.

Les risques du projet sont présentés à la revue avec leur qualification
humaine et leur criticité résiduelle. Les risques non qualifiés, non
évalués ou élevés sont signalés ; le périmètre d'applicabilité à cette
MEP ne peut pas être inféré. Le nombre d'incidents à ID canonique et
les enregistrements incomplets sont montrés sans supposer leur statut
« ouvert/clos » : cette information doit être contrôlée dans la
fiche de maintenance.

Même lorsque toutes les empreintes concordent, les contrôles réels
(sauvegardes, procédure applicable, état du serveur, supervision,
fenêtre autorisée, sécurité, support, risques et incidents pertinents)
restent **humains et explicites**. Aucun GO ni autorisation ni événement
MEP n'est écrit par cette consultation.

Les smoke tests vérifient les avertissements en présence de risques
non qualifiés, l'absence d'autorisation implicite, la détection de
preuves/procédures modifiées et le refus d'une révision MEP périmée.
