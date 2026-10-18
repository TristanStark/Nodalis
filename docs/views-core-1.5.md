# Modèle de lecture et requêtes des vues dynamiques (v1.5)

**Statut : socle en développement sur `develop`.** La version stable reste
v1.4.0 après publication de son tag ; aucune fonctionnalité de vue dynamique
n'est annoncée comme prête dans la release 1.4.

## Sources, portée et champs

La projection construit des éléments **en mémoire**, à partir des modèles
existants : documents répertoriés dans `.nodalis-links.json`, tâches Markdown,
jalons, décisions, incidents et interventions. Les six catégories conservent
leur référence au document GUID, son chemin et, le cas échéant, la ligne et
l'identifiant du bloc. Les documents absents ou ambigus restent des
**diagnostics**, et ne sont pas liés artificiellement à un homonyme.

Les propriétés sont **typées** : texte, date `DateOnly`, booléen et nombre.
Un champ absent est réellement absent ; il n'est pas remplacé par une chaîne
vide, une date inventée ou un statut supposé. Les tags sont une collection
distincte. Une date de tâche est une échéance, alors qu'une date d'incident
est son horodatage métier : la vue n'interprète pas ces significations comme
équivalentes. Les sources absolues de maintenance doivent être résolues
relativement à la racine du workspace ; aucun chemin extérieur n'est admis.

## Moteur de requêtes

Les prédicats autorisés comprennent égalité, inégalité, contient, date
antérieure/postérieure/entre deux bornes inclusives, présence/absence,
appartenance à un tag, et conditions groupées **ET/OU**.

- Aucun SQL, script, regex libre ou expression C# saisie par l'utilisateur.
- Jusqu'à 8 niveaux de groupes et 80 conditions/nœuds.
- Champ de tri et de regroupement choisi dans une liste blanche.
- Tri déterministe avec clé d'identité stable en cas d'égalité.
- Pagination limitée à 500 résultats par page, scan borné et
  avertissement si le maximum est atteint.
- Les valeurs inconnues ne correspondent pas à `NotEquals` ;
  `IsEmpty` peut les cibler explicitement.
- Scope par identité explicite, sans similitude textuelle entre noms.
- Aucune écriture au cours de la projection ou de la requête.

## Architecture et suites

- `src/Nodalis.Core/Views/WorkspaceViewProjector.cs`
- `src/Nodalis.Core/Views/WorkspaceViewQueryEngine.cs`
- `tests/Nodalis.SmokeTests/WorkspaceViewsSmokeTests.cs`

À venir dans les issues [#130](https://github.com/TristanStark/Nodalis/issues/130)
à [#134](https://github.com/TristanStark/Nodalis/issues/134) :
définitions sauvegardées portables, constructeur UI, dashboard et actualisation.
La projection n'a pas d'interface grand public tant que ces étapes ne sont pas
intégrées et qualifiées.

## Première interface WPF (aperçu non qualifié)

Depuis **Ctrl+P → Vues dynamiques (aperçu 1.5)**, ouvrir une fenêtre avec
recherche, filtre de type, statut, tri et regroupement ; choisir les onglets
**Liste / Tableau / Cartes**. Double-cliquer un résultat pour ouvrir le document
canonique à sa ligne d'origine. Le constructeur de vues peut sauvegarder les
définitions privées ou partagées, les dupliquer et les supprimer après action
explicite. Aucune source Markdown n'est changée lors d'une consultation.

**Évolution de l'aperçu :** la collecte des décisions traverse désormais
les projets et applications. Le dashboard, l'explorateur compact « Mes vues »,
la palette et le champ `Pinned` sont intégrés. Les requêtes WPF utilisent
annulation et pagination ; un cache en mémoire évite les rescans si aucune
source n'a changé, et les événements du workspace déclenchent le rafraîchissement.
Les collecteurs métiers nécessitent encore une granularité par document
pour terminer entièrement #133. Une source illisible donne un diagnostic. Il s'agit donc d'un **aperçu develop**, pas de la
version 1.5 stable.
