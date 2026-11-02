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
