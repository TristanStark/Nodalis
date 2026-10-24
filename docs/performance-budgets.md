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
La mutualisation interservices et la synchronisation des refresh concurrents
restent à effectuer dans #183.


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
