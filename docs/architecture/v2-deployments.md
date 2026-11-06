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


### Bilan global de MEP — décision et dates distinctes

Une fiche MEP v1 peut porter une clôture métier optionnelle.
Le champ JSON est **omis par défaut**, ce qui conserve la lecture
canonique des anciens fichiers v1 sans conversion ni écriture.
Le bilan est figé séparément des tentatives et événements.

- **Réussie / Réussie avec réserves** : dernière tentative déclarée
  réussie, date réelle de déploiement saisie explicitement, horodatage
  de clôture ultérieur et réserves écrites pour le second verdict.
- **Échouée** : dernière tentative achevée en échec ou interruption.
- **Annulée** : pas de tentative, ou dernière tentative achevée
  sans succès.

Aucun succès déduit automatiquement d'un rollback ou d'un GO.
Une fois la clôture enregistrée, aucune tentative ou action ne peut
être rajoutée ; SaveAsync refuse tout changement d'un bilan existant.
Aucun jalon, incident ni campagne n'est clôturé par ricochet.


### Bilan depuis Projet > Mises en production

Le registre affiche désormais **Bilan MEP…** lorsqu'une opération
n'a pas encore de clôture et que sa dernière tentative est achevée
(ou qu'elle n'a jamais démarré). Le formulaire propose uniquement
les verdicts correspondant au dernier événement humain : Réussie
ou Réussie avec réserves après une tentative réussie ; Échouée
ou Annulée après une tentative non réussie ; Annulée seule
pour une opération sans tentative.

Pour une réussite, la personne renseigne elle-même la **date
effective** au format jj/MM/aaaa HH:mm ; la **date de clôture**
est distincte et horodatée à la confirmation. L'auteur,
le motif et les réserves (pour le verdict avec réserves) sont
obligatoires selon le modèle. La sauvegarde conserve le verrouillage
SHA-256 et refuse une modification du bilan déjà enregistré.
Les archives sont consultables mais jamais modifiables.


### Stabilisation et passation à l'exploitation (#203)

Avant le bilan de clôture, une tentative terminée peut donner lieu à
une ou plusieurs **revues de passation explicites et horodatées** :
Acceptée, Acceptée avec réserves, Refusée ou Reportée. Elles restent
dans la même fiche Markdown MEP, via le champ facultatif
handoverReviews (absent des anciennes fiches v1). Les décisions
précédentes ne sont jamais remplacées, même après un refus.

Chaque revue identifie l'acteur, le destinataire et la tentative
concernée. Sept domaines sont présentés obligatoirement :
documentation ; supervision ; sauvegardes et reprise ; sécurité ;
support et responsabilités ; risques/réserves ; tests de
post-déploiement. Chaque ligne a un statut explicite (confirmé,
réserve, non vérifié, sans objet), une justification et une
preuve facultative (chemin relatif et SHA-256).

Les preuves ne sont jamais fabriquées et l'application ne conclut
pas que le système de production est sain. Une passation acceptée
malgré des vérifications manquantes est signalée par des avertissements
**non bloquants** et nécessite une validation humaine consciente.
Une tentative échouée ne peut pas être déclarée acceptée en exploitation.
Les contrôles ne changent aucun jalon, incident, tâche, campagne ou GO.

La sauvegarde ne permet que l'ajout d'une revue, séparé du journal
de tentative et du bilan de MEP. Les décisions passées et leurs
preuves sont immuables, protégées par SHA-CAS. Un bilan final ferme
également les possibilités d'ajouter des revues ; les archives sont
toujours en consultation seule.


### Interface de revue de passation

Dans **Projet > Mises en production…**, une opération ayant une
tentative terminée et non encore clôturée expose **Passation
exploitation…**. La fenêtre affiche la dernière tentative, les
revues historiques déjà enregistrées et sept contrôles distincts.

Pour chaque contrôle, l'utilisateur choisit un état explicite
(Non vérifié, Confirmé, Réserve, Sans objet), écrit un constat et
peut lier une preuve existante du workspace. L'empreinte SHA-256
est recalculée à l'enregistrement ; seules les preuves issues du
**projet propriétaire** sont acceptées. Aucun état ni aucune
preuve ne sont déduits automatiquement.

Le verdict humain (Acceptée, Acceptée avec réserves, Refusée,
Reportée), l'auteur, le destinataire, la justification et les
réserves sont confirmés après affichage des conditions non vérifiées,
des réserves et des contrôles sans preuve. Les avertissements
n'empêchent pas la décision mais exigent de la prendre en
connaissance de cause. Une revue refusée ou reportée ne supprime
pas les décisions précédentes ; une nouvelle revue est possible.

Le registre MEP expose les revues et leurs constats dans la partie
historique, avec le bilan et les tentatives toujours distincts.
La fenêtre de bilan alerte également lorsque l'opération n'a
aucune revue de passation, sans empêcher sa clôture guidée.
La clôture définitive reste la dernière opération modifiable ;
aucune revue ne peut être ajoutée après coup.


### Contrôles post-déploiement — sources REC et incidents canoniques

Chaque tentative terminée peut recevoir des contrôles post-déploiement
explicites, sans créer de nouveau moteur de tests ou d'anomalies.
Le journal facultatif postChecks de la fiche MEP v1 enregistre :
l'identifiant de campagne REC, le GUID du passage figé, le SHA-256
de la révision source REC, les nombres exacts de tests sélectionnés,
conformes, non conformes, bloqués, non applicables (donc non exécutés
calculables), les éventuels IDs INC liés, l'auteur, l'horodatage,
la justification et le **verdict humain**.

Une nouvelle association est refusée si le passage REC n'appartient
pas au même projet, si sa version ou son environnement diffèrent de
la tentative, si la révision REC a changé ou si les nombres déclarés
ne correspondent pas aux résultats du passage figé. Chaque lien
INC doit exister avec ID canonique unique dans la fiche de maintenance
du projet. Aucun statut « résolu » n'est inféré des incidents.

L'historique post-MEP est append-only, distinct de la décision
Go/No-Go, de la campagne REC, de la revue de passation et du bilan
de MEP. Un résultat REC plus récent n'efface jamais l'ancien.
Les tests non exécutés, non conformes, bloqués ou les incidents
produisent des **avertissements consultatifs**, jamais un verdict
automatique. Les anciens Markdown MEP ne possèdent pas de postChecks
par défaut et restent canoniquement lisibles, sans migration.

La sauvegarde utilise la révision SHA-CAS de la MEP et vérifie les
sources REC/INC uniquement à la demande d'un nouveau contrôle.
Après la clôture de la MEP, aucun nouveau lien n'est accepté.


### Interface de liaison des tests post-MEP

Dans **Projet > Mises en production… > Contrôles post-MEP…**, une
tentative terminée propose uniquement les passages REC existants du
même projet dont la version et l'environnement concordent avec la
tentative. L'utilisateur choisit un passage précis, voit ses résultats
figés, sélectionne éventuellement plusieurs incidents **INC**
déjà présents dans la fiche de maintenance du même projet, puis
renseigne explicitement auteur, motif, verdict et réserves.

La confirmation présente les échecs, blocages, tests non exécutés
et les liens vers incidents. La sauvegarde recalcule le catalogue
REC, vérifie les GUID de campagne/passage, les comptages d'exécution,
la révision REC SHA-256 et l'existence des INC choisis. La MEP
est publiée sous son propre contrôle SHA-CAS uniquement si les
sources sont restées identiques ; sinon la personne doit relire la
nouvelle révision. La fenêtre du registre affiche les contrôles
historiques avec leurs résultats, réserves et liens.

Les campagnes et fiches de maintenance restent entièrement inchangées.
Une nouvelle réussite REC, une intervention terminée ou une revue
« Conforme » dans le journal MEP ne clôture jamais automatiquement
l'incident, la MEP, un jalon ni un risque. Archives en lecture seule.


La liaison indique les **déclarations originales du passage REC** ;
les rectifications administratives éventuelles (journal REC distinct)
sont dénombrées et signalées à la personne avant confirmation. Une
rectification ne constitue jamais une nouvelle exécution. Une
tentative terminée en échec ou interruption ne peut pas recevoir un
verdict post-MEP « Conforme » ni « Conforme avec réserves », même
si certains tests REC sont réussis ; elle peut conserver les
constats « Non conforme » ou « À requalifier ». Les avis restent
indépendants du bilan global de déploiement.


### Suivi des réserves en exploitation (hors MEP clôturée)

Une MEP clôturée est **immuable** : ses décisions et réserves
historiques ne sont jamais réécrites pour signaler leur résolution
ultérieure. Le projet peut posséder un registre Markdown unique
Exploitation/Déploiement/Suivi/Reservations-MEP.md, créé seulement
après action explicite. Sa lecture ne crée pas de répertoire. Il reste
indépendant des incidents, tâches, gates, campagnes REC et jalons.

Chaque réserve RES-xxxx reçoit un GUID stable, le GUID de la MEP
propriétaire, l'origine précise (**passation, contrôle post-MEP ou
bilan**), le GUID de cette décision, le SHA-256 de la fiche MEP et
son extrait historique. Le responsable désigné, la description,
l'auteur, la date de création et une échéance facultative forment
l'identité initiale de l'obligation. Une même décision d'origine
peut produire plusieurs réserves individuelles **validées
manuellement**, sans fractionnement automatique du texte.

Le journal est **append-only** : prise en charge, note, réattribution
avec nouveau responsable, résolution humaine explicite et réouverture.
La progression et la résolution ne ferment jamais implicitement
l'incident, le test, le risque ou le jalon d'origine. Les transitions
sont contrôlées ; une nouvelle intervention après résolution requiert
une réouverture historisée.

Les écritures restent limitées à une action par validation, isolées
au projet propriétaire et protégées par révision SHA-256. Une preuve
facultative conserve un chemin de fichier du projet et son empreinte,
revérifiée avant écriture. La consultation reste permise en archive,
mais l'ajout y est interdit. Les anciennes fiches MEP restent
strictement identiques, y compris après une résolution de réserve.
