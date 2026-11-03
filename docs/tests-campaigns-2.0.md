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
