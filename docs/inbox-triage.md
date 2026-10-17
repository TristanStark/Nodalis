# Inbox 1.1 — vue de tri intégrée

Issue #102, à la suite de la capture multiformat #101.

## Accès et présentation

Sans changer de workspace :

- **Connaissance > Inbox** ou **Palette de commandes > Ouvrir Inbox**.
- Bouton discret **Inbox (N)** dans le tableau de bord : nombre de captures
  « À traiter », recalculé à l'ouverture de Nodalis, après une capture et à la
  fermeture de la vue de tri.
- Bouton « + Nouvelle capture » dans la vue Inbox.
- Liste chronologique montrant type, date locale, état, aperçu Markdown,
  provenance et cible de conversion lorsque connue.
- Aperçu intégral en lecture seule à droite, sélection au clavier et
  double-clic pour positionner le focus dans le contenu.

## Tri et récupération

- Recherche locale dans le texte, le type, la provenance, la cible et
  le nom des pièces jointes.
- Filtre Tous / À traiter / Traités / Archivés.
- Sélection multiple au clavier via Ctrl et Maj.
- Traitement en lot : À traiter, Traité, Archiver et Supprimer
  (vers **Inbox/Corbeille**, aucune suppression définitive).
- Mode « Corbeille Inbox » pour consulter et restaurer sans collision.
- Chaque élément du lot est traité séparément : un fichier disparu ou un
  conflit n'empêche pas les autres traitements. Les échecs individuels
  sont affichés et envoyés aux diagnostics locaux.
- Données corrompues : avertissements visibles ; les éléments endommagés
  sont préservés dans Inbox/Quarantaine, jamais supprimés silencieusement.
- Les aperçus sont du texte Markdown brut et ne déclenchent jamais
  l'exécution de contenu HTML, scripts, pièces jointes ou liens.

## Compatibilité et limites

- Aucun compte, service, base opaque ni synchronisation.
- L'Inbox ne change pas les notes rapides, le schéma 1.0 ou les fichiers
  de maintenance ; la conversion métier et son idempotence sont prévues
  dans les issues #103 et #104.
- Le mode de secours en lecture seule n'effectue aucune capture ou
  restauration Inbox.
- Les actions peuvent être utilisées entièrement au clavier. L'interface
  emploie les ressources de thème sombre, le focus WPF et des panneaux
  redimensionnables ; vérifier manuellement DPI 100 %, 125 % et 150 %.

## Qualification interactive

1. Capturer depuis une autre application et vérifier le compteur du
   dashboard, puis ouvrir la vue sans changer de workspace.
2. Rechercher et filtrer, sélectionner plusieurs captures au clavier.
3. Archiver / marquer traité puis recharger la liste.
4. Déplacer plusieurs éléments en corbeille, les restaurer, tester une
   collision isolée et constater que les autres sont restaurés.
5. Vérifier le contraste, le focus, les menus contextuels et l'absence de
   boutons tronqués sur plusieurs résolutions/densités.
6. Corrompre volontairement une capture test, voir le diagnostic et
   contrôler que le Markdown reste récupérable en quarantaine.

Les tests de persistance automatisés restent dans
`InboxPersistenceSmokeTests`. Les événements souris/clavier nécessitent
un contrôle Windows interactif avant de considérer #102 pleinement qualifiée.
