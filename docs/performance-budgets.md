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


## Dashboard affiché : snapshot partagé et révisions (#183, #185, 28 octobre 2026)

À chaque ouverture de l'accueil, une seule reconstruction du catalogue de
liens est demandée pour les panneaux Tâches et Jalons. Les deux projections
métier partagent ce snapshot ; elles ne lancent plus deux reconstructions
concurrentes du workspace. Un compteur de révision propre à l'accueil empêche
un chargement antérieur de publier ses listes après un nouvel affichage ou
une navigation ailleurs. Les échecs I/O de chaque panneau restent indépendants
et ne masquent pas la réussite de l'autre.

Après une sauvegarde d'onglet, le rafraîchissement de l'index n'entraîne plus
une reconstruction des favoris, historiques et vues sauvegardées **si l'accueil
est masqué**. Les listes sont recalculées lors de la prochaine ouverture.
Les flux qui doivent actualiser les backlinks dans l'éditeur conservent leur
indexation. Les deux panneaux de l'accueil restent à qualifier sur Windows,
en particulier avec plusieurs changements d'onglets rapides.

Pour évaluer : ouvrir l'accueil à plusieurs reprises sur un workspace de
5 000 documents, vérifier tâches/jalons récents, puis ouvrir une note et
saisir en continu. Comparer les événements > 200 ms dans Diagnostics locaux.

Lorsqu'un onglet non actif termine sa sauvegarde alors que l'accueil est
visible, les panneaux Tâches et Jalons sont désormais reconstruits à partir
du **catalogue déjà actualisé**, sans un nouveau parcours d'index.
Ce cas évite de laisser des cases à cocher ou des jalons obsolètes dans
l'accueil après un changement d'onglet.


## Propriétés Markdown : mise à jour limitée au front matter (#186, 28 octobre 2026)

Le résumé des propriétés du panneau contextuel utilise désormais une signature
correspondant **uniquement à l'en-tête YAML**, bornée par les deux délimiteurs
`---`. Quand une frappe ne change que le corps Markdown, Nodalis ne lance
plus `MarkdownFrontMatterParser.Parse` (qui copie normalement le corps du
fichier), ne retrie plus les propriétés et ne rebinde plus la liste WPF.
La signature est vérifiée à chaque frappe avec lecture du premier en-tête ;
elle est invalidée au changement d'onglet, de contexte ou d'en-tête.
Les documents sans front matter renvoient une signature vide : leurs frappes
ordinaires ne provoquent pas de re-parsing des métadonnées. La modification
des propriétés via le formulaire force toujours l'actualisation attendue.
Smoke tests : LF/CRLF, corps édité, en-tête édité et ouverture non close.
La latence WPF doit encore être quantifiée sur des notes de 100 k / 1 M caractères.

Optimisation complémentaire : l'extraction de la signature rejette en temps
constant le cas courant d'un fichier sans en-tête (premier caractère utile
différent de `---`) et emploie des `ReadOnlySpan<char>` pour contrôler
les délimiteurs, sans allocation de lignes ni copie d'un corps de 1 Mio.


## Qualification ciblée — fermeture de nombreux onglets et reprise de veille (31 octobre 2026)

Reproduction utilisateur : plusieurs documents ouverts, puis menu contextuel
« Fermer les onglets à droite » ; blocage important de l'interface. Ralentissement
également à la reprise de veille.

Analyse du journal Visual Studio : **50** `TaskCanceledException` de première
chance (annulations sans crash démontré), **27** diagnostics WPF
`TreeViewItem.VerticalContentAlignment` / `FindAncestor ItemsControl` et **28**
arrêts normaux de threads avec code `0x0`. La capture (~365 Mo) n'établit pas
une fuite de mémoire à elle seule.

Incrément `develop` :
- Une fermeture multiple prévalide l'autosave de chaque onglet et demande
  confirmation explicite en cas de contenu non enregistré. Elle supprime les
  onglets **en une seule rebinding du ListBox**, au lieu d'une série de
  suppressions impliquant autant de passes de layout. L'onglet de référence
  est réactivé une seule fois et les contrôleurs sont désinscrits/disposés.
- Les événements `Saved` des contrôleurs sont agrégés avec un debounce
  de 220 ms. Un burst de sauvegardes, notamment après veille, produit un
  recalcul liens/contextes commun au lieu de N invalidations indépendantes.
  Les sauvegardes arrivées pendant un recalcul produisent un lot suivant ;
  l'enregistrement disque reste immédiat et indépendant de ce debounce UI.
- Le `TreeViewItem` définit explicitement
  `VerticalContentAlignment=Center` pour éviter la liaison ancestrale
  par défaut lors du détachement/recyclage des conteneurs.
- Les notifications Windows de suspension/reprise interrompent/réarment les
  timers de présentation et backup sans déclencher de reconstruction globale
  immédiate au réveil ; le heartbeat ne confond pas la suspension déclarée
  avec un blocage WPF.
- Smoke test avec 40 contrôleurs d'autosave, rafales de saisies, double flush
  et conflit de modification externe : aucune réécriture d'une version
  concurrente n'est tolérée.

**Qualification requise sur Windows** : 30/100 onglets (mix courts/longs et
édités), « Fermer à droite » / « Fermer les autres » depuis le début et le
milieu, retour du split, annulation sur conflit externe, apparition correcte
du dernier onglet, pas d'onglet fantôme, disparition des diagnostics de binding.
Comparer avant/après le temps de fermeture et le retard Dispatcher p95/p99/max.
Laisser Nodalis ouvert avant mise en veille puis mesurer la reprise et le nombre
de refresh / sauvegardes sans attribuer à priori le ralentissement à la RAM.


## Scroll du worktree conservé lors des changements d'onglets (#187, 31 octobre 2026)

**Régression constatée en vidéo (53 secondes) :** lors des bascules entre notes
déjà ouvertes appartenant à différentes applications/projets, le worktree
effectue de grands sauts de scroll vers le haut ou le bas. Les freezes
précédents sont signalés comme résolus, mais cette navigation automatique
perturbe la lecture du volet gauche.

Correctifs dans `develop` :

- `NavigationTree_SelectedItemChanged` n'enfile plus un `BringIntoView`
  différé après chaque sélection : cliquer une ligne déjà visible ne
  demande pas de nouveau déplacement du viewport.
- Le style `TreeViewItem` intercepte `RequestBringIntoView` **au niveau de
  la ligne**, avant que son ancêtre `ScrollViewer` ne le traite : une
  sélection programmatique depuis l'éditeur ne déplace pas le scroll.
  La navigation clavier lorsque le worktree a le focus reste native ;
  « Révéler dans l'explorateur » et clic sur un en-tête épinglé restent
  les actions explicites de repositionnement.
- Un callback `SelectedItemChanged` tardif ne rouvre plus le même document
  déjà demandé par l'onglet actif.
- Les demandes différées d'affichage ignorent une cible devenue obsolète
  ou invalidée par un scroll ultérieur.
- La projection du filtre du worktree n'est plus recalculée si racine,
  recherche et application visible sont inchangées. Cela évite les
  ré-expansions involontaires de branches et le churn du TreeView virtualisé.
- Les listes Favoris/Documents récents ne sont plus rebondées lorsque les
  lignes de raccourci n'ont pas réellement changé.

**Validation manuelle Windows (sans présumer du résultat) :**
ouvrir 20–50 onglets de différents projets ; placer le worktree à mi-scroll ;
basculer plusieurs fois entre les onglets sans toucher à l'explorateur ;
l'offset et les branches repliées doivent rester stables, même lorsque
le document sélectionné est hors de l'écran du worktree. Vérifier ensuite
que « Révéler dans l'explorateur » scrolle volontairement jusqu'au document,
que le bouton d'en-tête épinglé revient au dossier, que les flèches clavier
font défiler normalement, et que les filtres/replis manuels restent fonctionnels.
