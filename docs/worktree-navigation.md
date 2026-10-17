# Explorateur compact — refonte worktree (#122)

Priorité P0. Référence visuelle : [maquette anonymisée](mockups/navigation-worktree-anonymisee.svg).
Tous les exemples de ce document sont fictifs. Ne jamais publier de captures ou de
noms issus d'un workspace réel sans anonymisation préalable.

## Navigation visible

Dans le panneau gauche, les sections commencent directement par **Global** puis
les **applications**. Les conteneurs purement techniques « Nodalis »,
« Applications » et « Projets » sont **visuellement transparents**, y compris
pour les sous-projets. Les modules, sections, dossiers et documents réels restent
visibles et navigables. La hiérarchie métier, les GUID, les chemins, le stockage
Markdown/JSON et les filtres de contexte ne changent pas.

La projection `NavigationNodeViewModel.VisibleChildren` est séparée de
`Children`, qui reste la hiérarchie métier complète. Tous les mécanismes
existants de recherche, tâches, liens, tâches projet, palette et dossiers
utilisent les vrais nœuds.

## Repères visuels

- Global : en-tête semi-gras, pictogramme globe.
- Application : texte et icône bleus, espacements distincts.
- Projet : carte sobre légèrement contrastée, liseré et coins arrondis,
  titre semi-gras, pictogramme violet et guide vertical visible sous
  les descendants quand il est développé.
- Dossiers : pictogramme dossier neutre ; documents Markdown : document
  neutre, variantes visibles pour Notes, Glossaire, Jalons, Tâches, Calendrier.
- Indentation : 11 pixels par niveau visuel, avec chevron compact.
- Lignes sélectionnées : fond bleu nuit + bord gauche accent, sur toute
  la largeur utilisable, **y compris lorsque le clavier n'a plus le focus**.
  Survol et focus restent distincts ; pas de fond blanc système.
- Noms longs : troncature/retour à la ligne (jusqu'à deux lignes pour un projet),
  infobulle rapide avec libellé complet.

Les couleurs sont définies dans `Themes/Dark.xaml`, pas dans les données.
Les styles ne créent aucun objet métier ni fichier de projet supplémentaire.

## Commandes et mémoire

- En haut du panneau : bouton **+** pour créer une application (l'ancien
  conteneur « Applications » étant masqué) et bouton **↗** pour révéler
  le document actif dans l'arborescence.
- Palette de commandes : les actions existantes restent disponibles.
- Clic droit sur projet : **Replier les autres projets** ; ne modifie
  que l'état visuel, pas les contenus ni les autres documents.
- Clic droit sur document/projet : **Révéler dans l'explorateur**.
- Déploiement, repli et sélection courante enregistrés localement via
  `ExpandedNodeIds` et `ActiveNavigationNodeId` (champ facultatif
  rétrocompatible dans les préférences locales). La fenêtre ne change pas
  de workspace pour révéler un nœud.
- La sélection est restaurée après reconstruction légitime de la navigation,
  mais le TreeView n'est pas reconstruit au simple survol ni au clic.
- Sélection d'une ligne : révélation dans la zone visible, sans scroll
  horizontal parasite. Panneau redimensionnable au splitter existant.

## Checklist de validation Windows

À effectuer dans Nodalis en session Windows interactive, **avant de clôturer
l'issue**. Utiliser un workspace de démonstration exclusivement fictif avec
2 applications, 5 projets dont un ultra-simple, dossiers imbriqués et titres
très longs.

- [ ] Panneau gauche à 240, 300 et 400 px (avec fenêtre étroite et large).
- [ ] DPI Windows 100 %, 125 % et 150 % : aucune troncature de boutons,
      superposition ou fond blanc ; calendrier workspace accessible.
- [ ] Contrôler les cartes de projets, leur guide et les en-têtes de
      Global/Application ; fichiers/documents distincts des dossiers.
- [ ] Développer/replier les niveaux par souris, clavier et chevron ; vérifier
      la sélection sur ligne entière, le focus actif et inactif, le scroll.
- [ ] Ouvrir Présentation, Technique, Tests, Décisions, Réunions, Glossaire,
      Jalons, Notes rapides et Tâches selon les projets présents.
- [ ] Effectuer création application/projet, clic droit, renommage,
      déplacement vers corbeille et restauration, palette et favoris.
- [ ] Tester « Révéler dans l'explorateur » depuis le document actif
      et « Replier les autres projets » sur des projets imbriqués.
- [ ] Fermer/rouvrir Nodalis et confirmer sélection et expansions.
- [ ] Tester 1 000+ nœuds, 30+ documents et noms de 100 caractères ;
      aucun blocage ou reconstruction pendant survol.
- [ ] Joindre **captures anonymisées du rendu réel sous Windows** à l'issue.

`NavigationWorktreeSmokeTests` vérifie la projection et l'immuabilité
de la structure canonique. La CI valide style C#, build WPF, régressions,
packaging et smoke-test de lancement, mais **ne remplace pas** les contrôles
visuels Windows et leurs captures.

## P1 optionnel (hors sortie P0)

Filtre instantané dans le worktree ; modes compact/confortable ;
filtrage à l'application courante ; blocs favoris/récents repliables.
Conserver comme améliorations distinctes si nécessaires : les vues favorites
et récents existantes ne sont pas dupliquées dans l'arbre.
