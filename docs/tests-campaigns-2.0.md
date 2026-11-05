# Tests et campagnes de recette — Nodalis 2.0

## Référentiel de tests identifiable (incrément 1)

Les tests sont des **blocs Markdown à GUID stable** dans de grands cahiers
ou des fiches individuelles. Un titre explicite `## TEST-0001 — Libellé`
ou `## TST-001 — Libellé` est suivi du marqueur déjà employé par Nodalis :
`<!-- nodalis:block:<GUID> -->`. Les titres de niveaux 1 à 6 sont pris
en charge ; les titres et marqueurs placés dans des blocs de code sont
ignorés. Les titres/numéros ne remplacent jamais l'identité GUID.

Le codec `ProjectTestDefinitionCodec.Inspect` est en lecture seule :
il construit une section **exacte** du titre du test jusqu'au prochain
titre de niveau égal ou supérieur, y compris ses sous-titres Étapes et
Attendu, avec son empreinte SHA-256. L'édition d'un autre test dans le
même grand document ne change pas cet instantané. Les doublons de GUID,
références répétées, marqueurs manquants ou malformés sont diagnostiqués
et ne deviennent pas des tests « gelables ».

Les smoke tests créent un cahier entièrement synthétique de **180 tests**,
vérifient les frontières des sections, les renommages, les duplications,
les empreintes indépendantes et l'absence de mutation du Markdown.

Cet incrément **n'exécute aucun test**, ne crée aucune campagne et
ne valide aucun critère/exigence. Les passages de campagne, résultats
et preuves sont à implémenter séparément dans l'EPIC #200.

## Campagnes REC et passages figés

Le contrat portable représente une campagne REC dans un fichier Markdown
contenant un titre et un bloc JSON versionné. La campagne a un GUID stable,
un projet propriétaire, une référence REC et un cycle de vie indépendant.

Un passage conserve son numéro, son GUID, son environnement, la version
applicative, le responsable et les définitions de tests exactement telles
qu'elles étaient lors du lancement, avec leur empreinte SHA-256 de section.

Un résultat est un événement humain distinct de la couverture. L'absence de
résultat signifie « non exécuté » ; les verdicts explicites sont Passed,
Failed, Blocked et NotApplicable. Pour relancer un test, il faut un nouveau
passage. Les anciennes définitions et verdicts restent consultables.

Le codec valide les GUID, le lien des résultats à un test réellement présent,
les empreintes de section, les identifiants dupliqués et le format canonique.
Un commentaire de preuve n'est jamais assimilé à une preuve vérifiée.

## Persistence contrôlée, résultats et passage suivant (incrément 3)

Le service de projets vérifie le manifeste, la présence du dossier Tests,
les chemins sans jonction, le code REC réservé et le SHA-256 de la fiche.
Les appels à la lecture ne créent ni dossier ni registre. La création
requiert un titre et une référence explicitement approuvée.

Le lancement d'un passage accepte une sélection de GUID de tests :
- chaque GUID doit être unique dans l'index partagé et appartenir au
  projet exact (jamais à un projet voisin partageant son préfixe) ;
- le cahier doit être placé dans Tests ou Recette, pas Campagnes ;
- son SHA documentaire doit correspondre à l'index actualisé ;
- un grand cahier est lu/parsé UNE seule fois, même avec 150+ tests ;
- chaque section est conservée exactement dans la campagne, y compris
  les sous-titres et l'empreinte de son contenu.

Enregistrer un résultat est une action humaine explicite : l'ancien
résultat ne peut pas être changé ; un nouveau passage doit être créé
pour rejouer un test. Le service ne réécrit jamais les fichiers source
des définitions de tests. Il publie une nouvelle révision de la campagne
via comparaison SHA atomique avant remplacement ; les sélections ou
actions devenues obsolètes sont refusées et doivent être rechargées.

Le premier lancement passe la campagne de Brouillon à En cours sans
affirmer aucune réussite. Les étapes administratives de qualification,
clôture et réserves restent à créer. Les tests d'intégration synthétiques
vérifient notamment 155 tests d'un grand cahier, l'isolation entre
projets, les actions périmées, les doublons, deux passages à versions
différentes et la préservation des résultats précédents.

## Lecture exacte et consolidation prudente (incrément 4)

La projection pure GetPassageRows restitue exactement les tests choisis
et leur résultat propre dans un passage donné ; un résultat absent reste
null (non exécuté), et jamais Passed par défaut.

Consolidate regroupe par **GUID de test + environnement + version
applicative + empreinte du test figé**. Une exécution en recette d'une
version ancienne n'est jamais assimilée à la réussite en production ou
sur une version différente. Dans un groupe comparable, le dernier
passage l'emporte, y compris quand il ne possède encore aucun résultat :
une ancienne réussite ne peut pas être recyclée comme verdict courant.

Le regroupement est uniquement un historique de déclarations humaines,
pas une preuve de conformité ni un verdict de validation de campagne.
Une vérification du cahier actuel doit encore se faire séparément.

## Interface de qualification dans Nodalis (incrément 5)

Une action **Campagnes et passages de tests** est accessible dans
la palette de commandes du projet. Le gestionnaire affiche à gauche
les campagnes REC, au centre leurs passages figés, et à droite les
résultats individuels en distinguant « NON EXÉCUTÉ » des verdicts
déclarés. Les listes de tests sont virtualisées ; les calculs du
référentiel et des passages sont effectués hors Dispatcher WPF.

Les actions disponibles, après approbation explicite :
- créer une campagne depuis un titre court et une référence prévisualisée ;
- sélectionner 1 à 500 blocs de test du projet (boutons tout sélectionner /
  tout désélectionner), l'environnement, la version et le responsable ;
- lancer un nouveau passage figé sur ces tests revalidés au moment de
  l'écriture (index + empreintes de fichiers) ;
- enregistrer une fois un résultat humain (conforme déclaré, non conforme,
  bloqué, non applicable), avec exécutant, justification et note de preuve
  facultative. Un test déjà qualifié dans ce passage ne peut être réécrit ;
- ouvrir la fiche REC dans l'éditeur Markdown en conservant son identité.

Un projet archivé est consultable, sans commandes de modification.
La création ne transforme jamais un ancien cahier, et l'application ne
déduit aucun résultat de la présence d'un lien, d'un marqueur ou d'une
case cochée. Ces écrans exigent une qualification visuelle Windows.

## Requalification des définitions après exécution (incrément 6)

La commande de campagnes compare désormais, sur demande et hors
Dispatcher WPF, chaque section de test figée aux sources actuelles. Une
analyse de cahier ne se fait qu'une fois pour tous les passages de la
campagne, même lorsqu'un cahier contient 150 tests.

Les états purement documentaires sont : **inchangé**, **modifié**,
**source absente**, **identité ambiguë** ou **index obsolète**. Le contrôle
exige le GUID de bloc globalement unique, l'appartenance au bon projet,
le contenu du cahier compatible avec le SHA de l'index partagé et un
unique titre TEST-/TST- identifiable dans le Markdown. Les changements
de titre ou de contenu modifient l'empreinte de la section, sans toucher
l'ancien passage. Une fiche absente ou un index stale ne peut jamais être
déclaré « inchangé ».

Les lignes de la vue REC présentent la validité de la **source actuelle**
à côté du résultat **historique**. Aucun verdict n'est modifié. Un test
auparavant conforme mais désormais modifié est signalé « à requalifier »
plutôt que reconduit comme conforme sur la version courante. Les
régressions testent la modification d'un seul test, la source supprimée,
l'index désynchronisé et les collisions globales de GUID.

## Qualification humaine et événements immuables (incrément 7)

Une campagne passe explicitement de Brouillon / En cours à En qualification,
puis à Clôturée après un verdict humain. Une clôture est Validée, Validée
avec réserves (au moins une réserve écrite), ou Non validée. Une campagne
Clôturée ou Annulée peut être rouverte avec une justification ; une campagne
en cours peut être annulée avec justification. Toute transition conserve
un événement horodaté avec auteur, motif et éventuel verdict/réserves.

Le service enregistre la nouvelle décision par comparaison atomique du SHA
de la campagne avant remplacement : une décision fondée sur une ancienne
révision est refusée. Les anciennes décisions et résultats sont conservés.
Les avertissements de qualification (passages absents, tests non exécutés,
non conformes, bloqués, non applicables, sources modifiées ou incertaines)
sont consultatifs : une clôture reste possible après justification humaine.
Aucune exigence EXI ou preuve n'est automatiquement approuvée.

Compatibilité : le champ optionnel decisionHistory est omis lorsqu'il vaut
null. Un ancien document REC v1 demeure lisible et sa sérialisation reste
strictement identique tant qu'aucun événement n'est ajouté. Les transitions
invalides, réserves absentes, doublons et horodatages incohérents sont refusés.

## Écran de qualification et de clôture guidée (incrément 8)

Le gestionnaire REC propose désormais quatre commandes selon l'état de
la campagne : Mettre en qualification, Clore avec verdict, Réouvrir
et Annuler la campagne. Chacune demande un décideur et une justification.
La clôture demande également un verdict explicite (Validée, Validée avec
réserves, Non validée) et les réserves humaines, si pertinentes.

Avant l'enregistrement, un récapitulatif présente l'état initial et
final, le décideur, le motif, le verdict et les avertissements non
bloquants concernant les tests non exécutés, bloqués, non conformes
et les sources à requalifier. Une seconde confirmation est nécessaire.

L'historique des décisions apparaît dans la fiche de passage. La
réouverture n'efface jamais le verdict de clôture précédent.
Les commandes sont indisponibles pour les projets archivés ou en
lecture seule. Toute modification reste soumise au SHA-CAS de la
campagne ; aucun fichier de définition de test ni exigence EXI n'est
modifié. Qualification visuelle Windows/dark mode à réaliser.

## Proposition de reprise ciblée (03/11/2026)

Le composant pur `ProjectTestRetestPlanner.Propose` examine **uniquement
le dernier passage figé** d'une campagne REC. Il suggère de reprendre les
tests non exécutés, ceux déclarés non conformes ou bloqués, ainsi que les
identités dont la définition courante a explicitement été signalée
modifiée/absente/ambiguë. Lorsque le contrôle des définitions n'a pas
été fait, il l'indique sans supposer que les sources sont inchangées.

Il n'écrit rien : les GUID proposés sont des **valeurs de présélection**
pour le sélecteur de passage existant. Le responsable peut les retirer,
en ajouter ou annuler. Un nouveau passage ne sera enregistré qu'après
la confirmation humaine et les contrôles habituels du service REC
(index, SHA, projet, versions). Un ancien verdict Passed n'est jamais
reporté comme résultat d'une réexécution.

Les tests synthétiques couvrent les issues Passed/Failed/Blocked/NotRun,
les changements de définition, l'absence de contrôle des sources,
un passage ultérieur entièrement non exécuté et une campagne sans passage.

## Proposition de reprise dans le gestionnaire REC (03/11/2026)

L'interface propose maintenant **« Proposer une reprise ciblée… »** pour
une campagne en cours possédant au moins un passage. Au clic, Nodalis
actualise l'index partagé, réévalue les empreintes de toutes les
définitions du dernier passage puis construit la proposition via
`ProjectTestRetestPlanner` sur un worker (pas sur le Dispatcher WPF).

Les suggestions comprennent les tests non exécutés, déclarés non
conformes/bloqués et ceux dont les sources courantes sont modifiées,
absentes ou ambiguës. Si un test source n'est plus identifiable dans
l'index, il apparaît dans le récapitulatif comme **indisponible** : il
n'est pas présélectionné artificiellement. Une proposition vide n'écrit
aucun passage.

L'utilisateur doit d'abord accepter de consulter la proposition, puis
peut désélectionner les tests suggérés ou en ajouter dans le sélecteur
multi-tests existant. L'environnement, la version et le responsable
restent à renseigner ; une **confirmation définitive** précède tout
enregistrement. Le service de campagne revérifie le projet, les GUID,
la définition source et l'index avant de créer le nouveau passage.
**Aucun résultat historique n'est effacé ni reporté automatiquement**.
La qualification visuelle en thème sombre reste à effectuer.

## Validation des instantanés répétés (03/11/2026)

La validation du contrat REC v1 réutilise, **uniquement pendant la lecture
ou l'écriture d'une même campagne**, le contrôle SHA-256 et l'analyse des
sections Markdown identiques déjà effectués. Les champs propres à chaque
occurrence (GUID, référence, chemin, ligne et sélection) restent vérifiés,
y compris quand l'empreinte réapparaît dans un autre passage. Un même
SHA avec un texte ou une identité différents est refusé.

La lecture vérifie maintenant le format canonique sans relancer une seconde
fois l'analyse complète des blocs : l'enveloppe Markdown/JSON reste
**strictement identique** aux anciennes campagnes, sans migration implicite,
stockage dérivé ou état métier nouveau. Une fixture synthétique de 80
passages répétés vérifie l'aller-retour, les GUID, les résultats absents et
le refus des contenus, identités et chemins falsifiés.

Cette optimisation réduit les analyses redondantes en mémoire ; elle ne
déduplique **pas encore** les textes des snapshots stockés sur disque et
ne dispense pas des mesures de performance WPF sur de grandes campagnes.

## Impact CA → tests lors de l'import IA (04/11/2026)

La projection consultative **ProjectCriterionChangeImpact** exploite
les liens vérifiés EXI→CA→TEST et les campagnes REC du projet.
Elle distingue explicitement les états d'analyse Unresolved,
NoLinkedTest, LinkedWithoutResult et HistoricalResults : ce ne
sont pas des états métier, des verdicts ni des approbations.

La confirmation avant modification de titre/corps CA énumère les
tests liés (huit au plus dans la boîte), les passages et les résultats
déclarés. Un ancien Passed suivi d'un passage NON EXÉCUTÉ reste un
résultat historique et non une conformité actuelle. Une source
déplacée, un index périmé ou un historique REC indisponible
sont des limites visibles, jamais des confirmations implicites.

La lecture a lieu hors du Dispatcher WPF, et le SHA approuvé est
recontrôlé à l'écriture par la transaction du critère. Aucun ancien
passage, lien, état ou preuve n'est modifié. Une reprise nécessite
toujours de créer un nouveau passage et un verdict humain distinct.

Qualification Windows : un CA sans tests ; un test non exécuté ;
un ancien résultat Passed ; plusieurs CA liés au même test ;
source déplacée ; index et REC indisponibles ; changement
concurrent ; refus de confirmation ; préservation de l'historique.


## Requalification CA observable après édition directe (04/11/2026)

La matrice EXI→CA→TEST présente désormais un **historique de
références CA**, activé explicitement par l'utilisateur.
Le service `ProjectCriterionBaselineService` stocke seulement
les empreintes de titre et corps CA, et les identités stables
des EXI/CA. Les références successives sont conservées dans
`.nodalis-ca-baselines/baseline-000001.json`, etc.

Une modification directe du Markdown d'un critère est donc
détectable lors de la prochaine ouverture de la matrice,
**à condition qu'une référence antérieure ait été enregistrée**.
Cette indication sert à identifier les tests à revoir ; elle
n'efface ni ne rectifie les passages de campagne. Des titres
CA renommés gardent leur GUID ; des CA nouveaux ou disparus
sont distingués, et un fichier EXI corrompu entraîne un état
indéterminé, jamais une suppression automatique.

L'action « Acter nouvelle référence… » exige une confirmation
et un motif humain. Elle représente un nouvel état
d'observation, **pas un verdict REC** ni une validation d'exigence.
L'écriture est append-only, atomique, liée à la révision des EXI
et conservatrice face aux collisions. Le stockage est local,
portable et ne nécessite aucune IA.

Les prochaines tranches pourront exploiter les CA marqués
`Changed / Added / Missing` pour préparer une **sélection de
reprise REC**. La réexécution, l'attribution d'un verdict,
la vérification de preuves et toute décision de campagne
restent explicitement humaines.


## Reprise REC ciblée depuis les CA modifiés — 05/11/2026

Le gestionnaire des campagnes propose désormais **« Proposer tests des CA modifiés… »** sur une campagne REC en brouillon ou en cours. Il ne dépend pas de l'existence d'un passage précédent. Cette commande relit une fois les exigences EXI du projet, le dernier instantané humain CA enregistré et l'index partagé de liens EXI→CA→TEST. Le calcul s'effectue hors du Dispatcher WPF.

**ProjectCriterionRetestPlanner** produit une proposition pure et non autorisante depuis les CA identifiés **Changed** ou **Added**. Chaque test candidat doit avoir un GUID de bloc unique, une source actuelle et un lien de CA appartenant au même projet, avec la même identité EXI parente. Les mêmes tests liés à plusieurs CA sont **dédoublonnés par GUID** ; leurs différents motifs sont conservés. Une source de test déplacée, une identité en collision, une couverture périmée ou un lien de projet étranger n'est jamais présélectionné automatiquement.

Les CA **Missing / Unresolved**, les références absentes et les index ou catalogues partiellement illisibles produisent des **avertissements visibles**. Le moteur ne reconstruit pas arbitrairement les anciens liens des CA disparus. Sans instantané humain préalable, aucune évolution historique n'est inférée. Une proposition vide n'écrit rien et oriente vers la sélection de passage manuelle.

La liste est affichée **avant** l'ouverture du sélecteur, avec référence de baseline, nombre de CA en écart et motifs par test. L'utilisateur peut **ajouter ou retirer n'importe quel test** dans le sélecteur REC existant, saisir environnement, version et responsable, puis confirmer séparément le nouveau passage. Le service REC contrôle une dernière fois le projet, les identités de blocs et les sources avant de figer les définitions. **Aucun verdict, preuve, conformité ou décision de campagne n'est recopié depuis un passage précédent.**

Smoke tests synthétiques : deux CA modifiés liés à un même test, dédoublonnage, lien et test déplacés, source périmée, doublon de GUID test, référence absente, CA disparu/illisible, projet et EXI étrangers, aucune modification réelle de données et erreurs partielles conservées comme avertissements.

Qualification visuelle à réaliser : scénario sans baseline, campagne brouillon/en cours, CA modifiés via éditeur Markdown, projet contenant plusieurs CA liés au même TEST, présélection modifiable, annulation avant et après le sélecteur, source déplacée, campagne clôturée, lecture seule et thème sombre.


## Suivi consultatif après préparation d'une reprise CA — 05/11/2026

Dans **Campagnes et passages de recette**, l'action
**« Suivi des reprises CA… »** est disponible en lecture seule sur
la campagne sélectionnée, y compris en consultation. Elle effectue
une nouvelle lecture de la référence CA persistée, du registre EXI,
des relations EXI→CA→TEST, des définitions TEST actuelles et de la
fiche REC actuellement enregistrée.

La projection pure `ProjectCriterionRetestProgress` compare uniquement
les **tests actuellement proposés depuis les CA modifiés** au
**dernier passage** de la campagne choisie. Chaque test est classé
sans réécriture : aucun passage, passage antérieur à la référence
CA, absent du dernier passage, figé sans résultat humain, résultat
humain déclaré ou source de définition à revoir (déplacée,
modifiée, absente ou ambiguë). Les GUID de tests sont uniques ;
le même test concerné par plusieurs CA n'est compté qu'une fois.

Le contrôle réutilise
`ProjectTestCampaignService.AssessSourceValidityAsync` : les SHA
des sections TEST figées sont comparés aux sources actuelles,
indépendamment des résultats REC. Une source dont le contrôle
est impossible reste **incertaine**, jamais automatiquement
« inchangée ». Le traitement se fait hors thread WPF et vérifie
que l'utilisateur n'a pas changé de campagne pendant l'analyse.

**Sémantique impérative :** « résultat humain déclaré » signifie
seulement qu'un résultat existe dans ce passage, et **pas**
qu'une modification CA a été requalifiée. Les instantanés CA
n'enregistrent pas l'heure exacte de chaque édition Markdown ;
un passage antérieur ou même postérieur à la référence ne suffit
donc pas à établir l'ordre entre changement et requalification.
Un ancien `Passed` ne remplit jamais un passage ultérieur non
exécuté ; aucune conformité n'est inférée.

L'action affiche les tests restants dans le panneau détaillé,
avec les comptages d'absents/non comparables, non exécutés,
résultats humains présents et sources à revoir. Une baseline
inexistante renvoie à l'action **« Activer suivi CA… »** de la
matrice, sans inventer d'état antérieur. Aucune donnée EXI, TEST
ou REC n'est créée, approuvée, corrigée ou clôturée par ce suivi.

Scénarios Windows synthétiques : aucun passage ; CA avec test
non sélectionné ; passage figé sans résultat ; résultat historique
`Passed`, `Failed` ou `Blocked` ; passage récent non exécuté
après ancien `Passed` ; version ou environnement différent ;
source TEST déplacée ou SHA modifié ; campagne étrangère ; référence
CA périmée ; index source indisponible ; doublons. Vérifier aussi
les détails et les commandes en mode sombre.

**Limite** : il s'agit d'une **vue d'observation à la demande**,
pas encore d'une décision formelle de clôture d'une obligation
de requalification. Un changement ultérieur de référence CA
modifie la comparaison. La preuve de requalification reste
strictement humaine, dans le workflow REC.
