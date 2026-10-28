# Budgets de performance Nodalis\n\n## Objectif\n\nLes performances doivent rester mesurables avec des fichiers Markdown et JSON ordinaires. Aucun index opaque ou moteur de base de données obligatoire n'est autorisé.\n\n## Benchmark reproductible\n\nCommande de référence :\n\n    dotnet run --project benchmarks/Nodalis.Benchmarks/Nodalis.Benchmarks.csproj -c Release -- --documents 5000\n\nLe programme génère un workspace temporaire déterministe, puis mesure la construction de navigation, l'index de liens à froid, le refresh sans changement, l'invalidation d'un seul document, la recherche globale et le diagnostic d'intégrité. Le résultat est imprimé en JSON pour permettre les comparaisons entre machines et commits.\n\nPour un test de capacité plus lourd, augmenter --documents. La valeur est volontairement paramétrable afin de ne pas masquer les coûts derrière un dataset privé.\n\n## Budgets cibles pour 5 000 documents\n\n| Opération | Budget cible | Garantie algorithmique |\n| --- | ---: | --- |\n| Navigation initiale | 2 500 ms | parcours linéaire du système de fichiers |\n| Index à froid | 8 000 ms | chaque document est lu au plus pour hash + parse |\n| Refresh sans changement | 1 000 ms | 0 hash Markdown, 0 parse Markdown |\n| Invalidation d'un document | 1 500 ms | 1 hash et 1 parse Markdown |\n| Recherche globale | 2 500 ms | parcours transparent des Markdown |\n| Diagnostic d'intégrité | 5 000 ms | lecture déterministe, aucun réseau |\n\nLes temps sont des budgets de qualification sur machine de développement et ne sont pas des assertions CI, car les runners partagés sont variables. Les smoke tests CI vérifient à la place les invariants algorithmiques : zéro relecture de contenu sur un refresh chaud et une seule invalidation pour une modification unique.\n\n## Index de liens incrémental\n\nLinkTargetEntry conserve, en JSON lisible, la longueur, l'horodatage UTC et le SHA-256 du document. Lors d'un refresh :\n\n1. si longueur et horodatage sont identiques, le SHA précédent est réutilisé sans ouvrir le Markdown ;\n2. si le fingerprint change, seul ce document est rehaché ;\n3. si le SHA change, seul ce document est reparsé pour liens et relations ;\n4. les références inchangées sont réutilisées, mais leurs cibles sont re-résolues en mémoire afin de rester correctes après renommage ;\n5. les suppressions disparaissent naturellement lors de la reconstruction du catalogue courant.\n\nLe fichier d'index reste un JSON dérivé supprimable et reconstructible.\n\n## Virtualisation UI\n\nL'arbre de navigation WPF active la virtualisation et le mode Recycling afin de ne pas matérialiser inutilement tous les conteneurs visuels d'un gros workspace.\n

## Résolution indexée des liens (#182)

Pour un rafraîchissement, les cibles précédentes sont indexées une seule fois
par GUID, chemin, scope/chemin et SHA-256. Les cibles courantes sont indexées
une seule fois par GUID, nom qualifié, nom affiché et alias. Les résolutions
répétées sont mémorisées uniquement pendant la reconstruction du catalogue.
Priorités conservées : GUID > qualifié > affiché > alias ; puis pour identifier
un fichier déplacé : chemin > scope/chemin > hash unique du scope > hash unique
global. Les collisions restent ambiguës. Lookup habituel en temps constant
moyen après indexation linéaire, hors collisions et I/O.

Les smoke tests comparent chaque backlink à l'ancien résolveur et vérifient
les collisions de noms ainsi que GUID et alias conservés après déplacement.
Les mesures Windows 5 000/10 000 documents et les p95/p99 du Dispatcher
restent requises pour valider l'issue entière.


## Suppression des écritures JSON inutiles (#183, étape 1)

Un refresh compare explicitement cibles (y compris alias), références et
relations en ordre déterministe. Si le contenu indexé est identique, le fichier
JSON et son horodatage restent inchangés ; l'horodatage UpdatedUtc ne déclenche
pas d'écriture artificielle. Le compteur IndexFileWritten et les tests
cold/warm/single-change garantissent cette propriété sans test de durée CI.
La synchronisation par workspace et la fusion des demandes simultanées
sont maintenant implémentées (voir « Single-flight par workspace »).
Le raccordement systématique des mutations structurelles à l'invalidation
explicite et les mesures réelles Windows restent nécessaires pour #183.


## Isolation du Dispatcher et sérialisation des écritures (#183 / #184, étape 1)

Les opérations d'indexation de liens sont maintenant calculées hors du thread
WPF, sur le pool de threads .NET. Un sémaphore partagé par chemin racine
canonique synchronise les écritures même lorsque l'interface, les tâches et
les jalons créent chacun une instance du service. Les appels annulés cessent
d'attendre et ne démarrent pas une nouvelle reconstruction.

**Limite actuelle** : la sérialisation ne mutualise pas encore les reconstructions
successives déclenchées par plusieurs services ; elle évite les courses disque
et les continuations CPU sur le Dispatcher. Les invalidations ciblées,
la fusion par version et les budgets de latence sont encore à traiter.


## Mutualisation explicite des projections UI (#183, #184, #185)

Les flux **autosave actif**, **ouverture du workspace**, **fiche projet** et
**chargement des vues dynamiques** transmettent le catalogue de liens déjà
rafraîchi aux services tâches et jalons. Ceux-ci n'effectuent pas de seconde
reconstruction de l'index pendant ces flux. Les autres appels conservent le
comportement habituel pour les modifications structurelles/métiers autonomes.

La lecture/analyse des tâches et des jalons s'effectue via un worker .NET :
les boucles de parsing, regex, contextes et tris n'exécutent plus leurs
continuations sur le Dispatcher WPF. La publication WPF reste côté appelant.
Le cache incrémental des tâches reste protégé par son sémaphore.

Les tests vérifient que les deux services n'effectuent pas de nouveau parcours
quand ils reçoivent un snapshot explicite. La coalescence automatique de
reconstructions initiées depuis d'autres flux et les mesures p95/p99 restent
à qualifier avant fermeture des issues.


## Scope des tâches et backlinks (#182 / #184)

Le service Tâches construit une table des chemins des projets/applications en
une passe et retrouve le contexte d'un document en parcourant uniquement
ses segments parents. Le précédent filtrage des projets et applications pour
chaque document (quadratique lorsque beaucoup de projets existent) disparaît.
Le rapprochement respecte le plus proche ancêtre et la frontière des segments.

Le panneau backlinks utilise un dictionnaire GUID local afin de ne pas
rescanner toutes les cibles pour chaque référence entrante.


## Chargement différé du dashboard après autosave (#185)

Lorsqu'un onglet document est actif, le dashboard global est masqué.
Un autosave continue d'actualiser l'index, les backlinks et le contexte
glossaire utiles à l'éditeur, mais **ne relance plus l'extraction des tâches
et jalons du dashboard masqué**. ShowDashboard() réactualise les deux listes
lors de son affichage ; le mode d'affichage visible continue à les rafraîchir.
Cette règle évite des parcours métier inutiles pendant la saisie continue et
ne modifie pas les fichiers sources.


## Heartbeat Dispatcher — diagnostic local (#181, étape 1)

Le module de diagnostic de l'application dispose d'un heartbeat WPF à priorité
Background et à intervalle de 500 ms, actif à l'ouverture de la fenêtre et
désactivable depuis **Diagnostics locaux**. Il calcule pour les retards de
planification p50, p95, p99 et maximum, avec un historique circulaire borné à
2 048 mesures et les 32 derniers événements dépassant 200 ms. Le bouton
« Actualiser les mesures » ajoute les valeurs au rapport technique copiable.

Aucun texte de document, chemin, terme recherché ou réseau n'est enregistré.
Mesures en mémoire de session uniquement. Ce heartbeat mesure les blocages
observables du Dispatcher, pas encore leurs stacks ni l'opération causale.
Il reste à corréler les traitements longs et à mesurer les scénarios Windows
avant de fermer #181.


## Single-flight par workspace (#183)

Les appels simultanés de même génération vers WorkspaceLinkIndexService
partagent désormais **une tâche de reconstruction active**. Un appelant peut
annuler son attente sans annuler les autres ; une invalidation explicite
(`NotifyWorkspaceChanged`, utilisée notamment par `RefreshDocumentAsync`)
incrémente la génération et déclenche une nouvelle reconstruction après celle
en cours. Les écritures demeurent sérialisées et les anciennes générations
ne peuvent pas écraser une écriture plus récente. Les tâches terminées ne
sont pas mises en cache, afin qu'un changement externe reste observable au
prochain rafraîchissement. `JoinedExistingRefresh` indique les demandes
fusionnées ; la suite smoke teste les appels concurrents et l'annulation.

**Limite** : toutes les mutations structurelles qui n'appellent pas encore
`NotifyWorkspaceChanged` devront être raccordées à une invalidation explicite
pour interdire toute fusion avec un refresh déjà en cours lors de la mutation.


## Autosave et annotations de glossaire (#184 / #185)

Le timer d'aperçu exécute désormais `GlossaryTextMatcher.Match` sur un worker
avec **révision de saisie**, onglet source et catalogue de glossaire capturés :
les correspondances obsolètes sont abandonnées au retour sur le Dispatcher.
L'adorner WPF n'est jamais manipulé depuis ce worker.

Un autosave de note ordinaire continue d'actualiser les métadonnées de liens,
mais ne reconstruit plus le même `FlowDocument` déjà rendu par le timer.
Les entrées de glossaire ne sont rechargées que si le fichier sauvegardé
appartient aux scopes de glossaire actuellement actifs (ou au premier chargement).
Les dashboards masqués demeurent différés. Cette optimisation conservatrice
n'évite pas encore toutes les lectures d'index et ne change pas le comportement
d'un fichier de glossaire nouvellement créé hors contexte actif.


## Annulation des annotations obsolètes (#184)

La recherche de termes de glossaire hors Dispatcher accepte désormais un
`CancellationToken`, vérifié à chaque token et occurrence. Chaque frappe,
changement d'onglet ou annotation forcée invalide la recherche précédente.
Une recherche annulée ne publie aucun résultat WPF ; le token est libéré à la
fin de la tâche. Ceci évite l'accumulation de plusieurs scans inutiles pendant
une saisie rapide, sans changer les règles de correspondance métier.


## Corrélation des gels et annulation des annotations (#181 / #184)

Diagnostics locaux agrège des durées p50/p95/p99 pour les opérations d'index,
de contexte autosave, de lecture de glossaire, de rendu Markdown et de
projection des tâches/jalons. Le moniteur conserve au plus 128 durées par
catégorie et 32 événements longs. Pour chaque retard du Dispatcher supérieur
à 200 ms, les opérations en cours ou achevées dans les trois secondes sont
signalées comme activités temporellement proches — **pas une attribution
causale**. Le rapport ne contient ni chemin ni contenu documentaire.

Les recherches GlossaryTextMatcher en arrière-plan vérifient l'annulation
sur chaque terme et chaque occurrence. Une frappe supplémentaire ou un
changement d'onglet annule l'analyse précédente ; les résultats arrivant
après changement de révision sont ignorés côté WPF. Le smoke test couvre
l'annulation explicite.

## Incrément develop du 26 octobre 2026 (#181, #183, #184, #186–#189)

- **DOCX** : une modification de sélection provenant d'un bloc annonce son
  nœud d'origine et ne recalcule que la section correspondante. « Tout
  sélectionner » continue d'invalider toutes les sections. Le compteur de
  blocs retenus est conservé par section et une double notification lors de
  la correction de titre est supprimée. À qualifier sur de vrais DOCX de 100+
  pages et lors d'actions successives (sélection, profil, plan et commit).
- **Vues dynamiques** : la projection des éléments déjà collectés est bâtie
  par un worker ; l'interface reçoit seulement son snapshot terminé. La même
  instance de catalogue de liens est réutilisée au lieu de relire le JSON
  dérivé en fin de collecte. Conserver le contrôle des modifications externes
  avant de qualifier définitivement #188.
- **Aperçu Markdown** : après le debounce, le parsing des blocs se fait hors
  du Dispatcher. Une nouvelle frappe annule l'ancien parsing ; un résultat
  n'est publié que si l'onglet, la révision et le contenu correspondent encore.
  La construction du FlowDocument et tous les objets WPF restent sur le thread
  UI. Les autres demandes synchrones de rendu conservent leur parcours
  historique. Le parsing est coopérativement annulable même dans les longs
  blocs de code. Un smoke test vérifie la parité des blocs et l'annulation.
- **Navigation** : le parcours du système de fichiers et la construction du
  modèle pur sont déplacés sur un worker ; les viewmodels WPF et leur
  publication restent côté Dispatcher.
- **Diagnostics** : catégories fixes « Navigation arborescence », « Sauvegarde
  automatique », « Vidage des onglets » et « Vues dynamiques » intégrées au
  rapport de durée d'opérations ; working set du processus, heap managé et
  collectes GC complètent les percentiles. Ce sont des observations globales,
  non une attribution de consommation aux opérations.
- **Single-flight** : test supplémentaire simulant deux générations
  invalidées pendant des refresh concurrents et vérifiant la présence de la
  dernière génération dans le JSON dérivé.

Ces changements ne constituent **pas** une validation de performance Windows :
les captures p95/p99 avant/après, les 5 000/10 000 documents, les scénarios
multi-onglets et les protections contre les changements structurels externes
restent au gate #190. Aucun budget chronométrique artificiel n'est ajouté
aux tests de CI.


## Recherche classique hors Dispatcher (#188, incrément 28 octobre 2026)

La recherche exacte/fuzzy et le filtrage de chemins pour la recherche
sémantique exécutent désormais **l'ensemble** du parcours, des accès aux
manifestes, du parsing Markdown, du scoring et du tri sur un worker .NET
(unique pour chaque requête, pas un worker par document). Les résultats ne
touchent pas WPF avant leur publication par le dialogue, qui continue
d'annuler la requête précédente et d'écarter les réponses obsolètes.

Un cache **strictement local à chaque requête** mémorise les répertoires
ancêtres ayant un manifeste projet/application, y compris les absences,
pour éviter les sondages `File.Exists` redondants à chaque fichier. Il
n'existe aucun cache persistant de texte ou de métadonnées : la recherche
suivante observe les modifications externes. L'énumération de dossiers et
fichiers vérifie l'annulation entre chaque élément, et le classement reste
inchangé. Smoke tests : annulation préalable, nouveaux documents, mutation
du contenu et des filtres sans résultat périmé.

À qualifier sur Windows : latence p95/p99 Dispatcher lors de 20 requêtes
successives en mode fuzzy, time-to-result sur 5 000/10 000 documents,
contrôle multi-onglets et recherche sémantique. L'index de contenu dérivé
et les optimisations du scoring restent **hors de cet incrément**.


## Imports DOCX/Markdown : réutiliser un seul index (#183, incrément 28 octobre 2026)

Après la validation d'un import DOCX ou Markdown, `RefreshNavigationAsync`
reconstruit **déjà** le catalogue de liens. Les flux de commit n'appellent plus
`RefreshLinkIndexAndContextAsync` immédiatement après. Ils transmettent le
même snapshot `_linkIndex` aux projections tâches et jalons. Si la sélection
reste un document, le contexte de ses liens est actualisé depuis ce snapshot.

Invariants attendus : un seul refresh d'index par validation d'import, aucun
parse métier redondant imposé par les services ; mêmes tâches, jalons,
backlinks et navigation qu'avant. Qualifier en particulier le commit
transactionnel et la réimportation de gros DOCX, ainsi que les erreurs
d'actualisation de l'interface. La transaction source reste inchangée.
