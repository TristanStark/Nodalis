# Nodalis 2.0 — Contrat Parcours projet (EPIC #195)

## État de l'incrément

Le socle Core et la persistance Infrastructure sont disponibles sur `develop`.
**Ce lot ne crée pas encore l'onglet WPF ni les commandes de palette** : il
stabilise d'abord un format documenté, portable et testé. Aucun parcours
n'est matérialisé à la simple ouverture, à l'indexation ou à la création
d'un projet 1.9/2.0.

## Sources canoniques

- Modèles intégrés en mémoire : `ProjectJourneyDefaults.CreateCatalog()`.
- Surcharge explicite propre au workspace :
  `Templates/project-journeys.json` (schéma `schemaVersion: 1`).
- Parcours réellement approuvé d'un projet :
  `<projet>/.project-journey.json` (schéma `schemaVersion: 1`).

Le JSON est lisible et transportable, sans base externe, IA ni connexion réseau.
La surcharge workspace remplace le catalogue intégré lorsque ce fichier existe :
aucune migration implicite ne modifie le fichier d'un utilisateur.

Chaque parcours porte le `projectId` de `.project.json` et la clé du
modèle choisi à l'origine. Ce modèle sert de **provenance** : modifier le
catalogue ne réécrit pas les étapes d'un projet existant.

## Profils initiaux

| Complexité | Étapes proposées | Comportement |
| --- | --- | --- |
| Ultra-simple | Aucune par défaut | Aucune création implicite ; possibilité d'opt-in sur un modèle personnalisé |
| Simple | Cadrage, Réalisation, Clôture | Proposition en lecture seule |
| Moyen | Cadrage, Conception, Réalisation, Tests, Clôture | Proposition en lecture seule |
| Complexe | Cadrage, Exigences, Risques, Conception, Préparation développement, Réalisation et changements, Tests, Mise en production, Clôture | Proposition en lecture seule, exactement 9 étapes |

Chaque étape contient des définitions d'opérations non exécutables
et peut ensuite prendre un état distinct des tâches :
`todo`, `inProgress`, `completed`, `notApplicable`.

Les opérations du projet ont des GUID indépendants, une clé de modèle
optionnelle, un libellé, une future clé de prompt et des références vers
des sources du workspace, jalons et contrôles éventuels. Ces références
ne dupliquent pas les objets métiers et ne créent pas de preuve implicite.
La complétion d'une étape n'arrête pas la mise à jour des documents
qu'elle référence.

## Contrats de service

`ProjectJourneyService` expose :

- `LoadTemplatesAsync` : lecture des modèles, sans écriture.
- `SaveTemplatesAsync` : enregistrement explicite et conditionnel d'une
  surcharge workspace, avec SHA-256 requis pour une mise à jour.
- `PreviewDefaultAsync` : modèle proposé par la complexité initiale, ou
  `null` pour Ultra-simple ; aucune écriture.
- `ReadAsync` : lecture d'un parcours existant et de son empreinte exacte,
  ou `null` si aucun parcours n'a été approuvé.
- `InitializeAsync` : matérialisation **explicite** avec GUID frais,
  `CreateNew` et absence d'écrasement d'une création concurrente.
- `SaveAsync` : remplacement conditionnel sur SHA-256 sous un handle
  protégé, puis `File.Replace` ; un éditeur externe ne peut pas
  être écrasé silencieusement par un état obsolète.

Les fichiers et manifestes de projet traversant des liens symboliques
ou sortant des répertoires Applications/Archives sont rejetés. Les
identifiants vides ou dupliqués, versions non supportées, clés ambiguës,
sources relatives contenant `..` et ordres d'étapes dupliqués sont
refusés. Un projet archivé est consultable mais son parcours ne peut
pas être modifié avant sa réouverture.

Les `SourcePaths` sont des chaînes workspace-relatives seulement :
leur interprétation métier, l'intégrité des GUID référencés, la
traçabilité des opérations et les liens UI seront développés dans
les prochains incréments. Ces seules références ne constituent ni
approbation ni preuve d'exécution.

## Tests Windows

`ProjectJourneySmokeTests` vérifie :
- Aucun fichier créé à la lecture ou au preview ; Ultra-simple inchangé.
- Modèles Simple/Moyen/Complexe et neuf étapes Complexe.
- Création opt-in, IDs non vides/uniques, relecture après redémarrage.
- Mise à jour d'état et réordonnancement sans réallocation de GUID.
- Refus d'une création concurrente, d'une révision périmée, d'un GUID
  en collision et de l'écrasement d'un fichier modifié à l'extérieur.
- Surcharge explicite des modèles et opt-in Ultra-simple.
- Conservation du JSON exact, des GUID et de la révision pendant
  archivage puis réouverture ; lecture seule à l'état archivé.

## Première interface WPF — édition guidée (31/10/2026)

La palette de commandes contient désormais **« Parcours projet 2.0 »**, à
ouvrir depuis un projet sélectionné ou un document du projet. Une fenêtre
modale à thème sombre permet de lire l'état des étapes et opérations sans
créer ni modifier aucun fichier lors de son ouverture. Ce premier accès
n'ajoute aucun nœud au Worktree et ne crée pas d'onglet de document.

- **Initialiser** : proposition du modèle selon la complexité. L'utilisateur
  confirme la création, qui utilise une écriture `CreateNew`. Ultra-simple
  reste sans initialisation par défaut.
- **Étapes** : ajout, renommage, retrait, ordre (monter/descendre) et état
  À faire / En cours / Terminée / Non applicable.
- **Opérations** : ajout, renommage, retrait et réordonnancement à l'intérieur
  d'une étape. Les GUID ne sont pas recalculés lors des éditions.
- **Confirmation et protection** : chaque changement passe par un plan Core
  pur (`ProjectJourneyEditor`), puis une confirmation explicite. Le service
  ne publie que le JSON de parcours avec le SHA-256 lu initialement et refuse
  les révisions externes plus récentes. En cas de conflit, l'utilisateur doit
  recharger depuis le disque ; aucune fusion implicite.
- **Lecture seule** : un parcours archivé ou un workspace en lecture seule
  reste consultable, sans action d'écriture. Les documents, tâches, jalons
  et preuves externes ne sont jamais supprimés par cet éditeur.

La suite `ProjectJourneyEditingSmokeTests` vérifie les opérations Core,
les GUID, la conservation des preuves et liens associés, le refus de statut
invalide et de version obsolète, ainsi que la persistance après redémarrage.
La CI Windows vérifie également la compilation et les ressources XAML.

**Limites de ce lot :** la fenêtre est modale, et non encore un onglet
réutilisable par projet. Il reste à développer l'onglet intégré,
les liens cliquables vers les sources, l'édition des modèles du workspace
dans l'interface et la qualification visuelle WPF par l'utilisateur.

## Onglets Parcours intégrés (31/10/2026)

L'éditeur du parcours est maintenant un composant WPF réutilisable
(`ProjectJourneyView`), également hébergeable dans la fenêtre historique.
La commande de palette **Parcours projet 2.0** et le bouton discret **▦**
dans l'en-tête de l'Explorateur ouvrent désormais une **vue intégrée
dans la zone centrale de Nodalis**, avec un onglet par GUID de projet.

Comportement et invariants :

- Deux ouvertures du **même projet** réactivent son seul onglet : il n'y
  a ni deuxième éditeur ni deuxième session de sauvegarde.
- La bande des Parcours est adjacente à la bande Markdown, sans
  transformer le JSON du Parcours en document Markdown éditable.
  Les onglets Markdown conservent leurs sessions et contrôleurs
  d'autosave existants ; basculer de l'un à l'autre n'écrit rien.
- Un clic sur un autre document, projet ou accueil quitte seulement
  l'affichage du Parcours. L'onglet reste réutilisable et sa fermeture
  explicite dispose de la vue, sans suppression de données.
  `Ctrl+W` ferme l'onglet Parcours actif.
- La reconstruction du Worktree sans navigation explicite conserve
  le Parcours actif et évite de forcer un changement de document.
- Une collision de GUID entre plusieurs projets **bloque l'ouverture**.
  Un renommage, déplacement ou retrait de projet détecté lors de la
  reconstruction du Worktree invalide l'onglet lié à l'ancien chemin.
- L'archivage et la réouverture demandent à fermer les onglets Parcours
  concernés (ainsi que les onglets documents) avant toute relocalisation.
- Le changement de workspace et la fermeture de l'application libèrent
  toutes les vues Parcours. La création des vues est paresseuse, sur
  ouverture explicite. Aucun rescan de workspace n'est lancé sur le
  scroll de l'Explorateur.

L'éditeur réutilise les mêmes méthodes de confirmation, empreintes
SHA-256 et validations que le premier dialogue modal. Ce n'est pas un
service IA ni un planificateur automatique : états et opérations
restent contrôlés manuellement.

**Qualification :** la compilation WPF/XAML et les régressions existantes
doivent être vertes sur le dernier HEAD ; une vérification visuelle
sur Windows (création, fermeture, tabulation, grand workspace, DPI,
archivage, lecture seule et conflits externes) demeure indispensable.

## Personnalisation graphique des modèles (31/10/2026)

La commande de palette **« Personnaliser les modèles de Parcours »**
et le bouton **« Modèles… »** de l'onglet Parcours ouvrent
`ProjectJourneyTemplatesDialog`, éditeur WPF du catalogue commun au workspace.

- **Lecture et édition sans effet de bord** : `ReadTemplatesSnapshotAsync`
  retourne la définition et le SHA-256 des **mêmes octets** du fichier
  `Templates/project-journeys.json`. En l'absence de personnalisation,
  les modèles Simple (3), Moyen (5) et Complexe (9) sont proposés en
  mémoire, sans création de fichier.
- **Choix du modèle** : modifier le nom des profils ; créer/retirer un
  profil personnalisé pour une complexité explicite, y compris Ultra-simple ;
  ajouter/renommer/réordonner/retirer étapes et opérations ; associer une
  clé de prompt optionnelle à une opération (aucune IA n'est exécutée).
  Les clés logiques ne changent pas lors d'un renommage ou d'un déplacement.
- **Sauvegarde volontaire unique** : les transformations du Core
  `ProjectJourneyTemplateEditor` produisent des copies sans mutation des
  listes d'origine. L'utilisateur dispose des actions **Enregistrer**,
  **Recharger** (avec avertissement avant de perdre les changements) et
  **Fermer** (avec avertissement si modifications non enregistrées).
  Une confirmation récapitule le catalogue avant l'écriture.
- **Concurrence** : la création est effectuée sans écraser un catalogue
  apparu entre-temps ; une modification ultérieure exige l'empreinte exacte
  obtenue lors de la lecture. Une édition externe invalide la révision
  et impose une relecture explicite ; aucun merge implicite n'existe.
- **Non-rétroactivité** : les projets déjà dotés de leur propre
  `.project-journey.json` ne sont **jamais** réinitialisés ni modifiés
  lorsque le catalogue du workspace est personnalisé.
- **Initialisation explicite** : sur un nouveau Parcours, la liste des
  modèles est filtrée selon la complexité du manifeste. Les projets
  Simple/Moyen/Complexe pré-sélectionnent le modèle par défaut
  (s'il existe) et peuvent choisir un modèle personnalisé. Les projets
  Ultra-simples n'ont **aucun modèle sélectionné par défaut** ; un utilisateur
  peut volontairement choisir un modèle Ultra-simple personnalisé.
- **Mode lecture seule** : l'éditeur reste consultable sur un workspace
  non modifiable ; la sauvegarde et les commandes de mutation sont désactivées.

Les tests `ProjectJourneyTemplatesSmokeTests` vérifient la lecture
sans écriture, les changements de profil/étapes/opérations/clés de prompt,
la stabilité des clés, le redémarrage, l'opt-in Ultra-simple,
la conservation des fichiers des projets déjà initialisés et les
conflits de sauvegarde optimiste.

**Suite** : références de sources et preuves navigables depuis les
opérations ; préparation de prompts non exécutés ; qualification visuelle
et ergonomique sous Windows.

## Suite de l'EPIC #195

1. Ajouter édition guidée des étapes/opérations (ajouter, déplacer, ignorer)
   avec aperçu/diff, confirmation et contrôle de révision.
2. Relier les futurs prompts et contrôles sans dupliquer les tâches/jalons.
3. Ajouter un onglet WPF unique par projet, ouverture depuis palette/worktree,
   dark mode, clavier, DPI et chargement paresseux.
4. Qualifier visuellement sous Windows, les fermetures/archives et les
   performances sur gros workspaces avant toute publication 2.0.
