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
