# Nodalis 1.1 — Release candidate / checklist (#105)

**État : en qualification**. La compilation et les tests automatisés sur
`develop` ne valent **pas** homologation sur poste Windows client.
Aucun tag stable `v1.1.0` ne doit être créé sur la seule base de cette CI.

## Gate automatique sur develop

Le workflow `Build` doit être **vert sur le commit candidat exact** :

- [ ] contraintes locales, absence de dépendances tierces et de réseau applicatif ;
- [ ] audit statique de l'accessibilité WPF et style C# ;
- [ ] compilation .NET 10 Windows x64 ;
- [ ] suite complète : Inbox #99–104, migration, sauvegarde, restaurations,
      rollback de l'updater, notes rapides et projets ultra-simples ;
- [ ] profils synthétiques **Small (8 documents), Medium (64),
      Large (256)** : capture, détection de doublon, notes et structure 1.0
      inchangées, conversion sans doublon, tâche, tri/archivage,
      corbeille/restauration, index recherche/navigation, backup ZIP
      vérifié puis restauration complète avec Inbox et cible convertie ;
- [ ] archive Windows x64 portable complète, manifeste des empreintes
      SHA-256, contrôle de reproductibilité du package ;
- [ ] lancement du **binaire publié** sur copie de
      `tests/ReleaseCandidateWorkspace`, recherche et liens fonctionnels.

Les scénarios de #103 et #104 testent également écriture atomique,
interruption/reprise, pièces jointes supprimées, métadonnées malicieuses,
collisions, annulation, ouverture simultanée de six lecteurs/six
écrivains, vérification de hash et avertissement de doublon.

Pour consulter les résultats, ouvrir
**Actions → Build → dernier run de la branche develop**. Ne pas
confondre un build historique vert et un commit plus récent non testé.

## Matrice Windows interactive — à remplir pour une RC

Enregistrer la version RC, le SHA du commit, la version de Windows,
le DPI et les anomalies. Essayer un **workspace fictif** plutôt qu'un
workspace professionnel. Tester les deux systèmes clients :

| Scénario | Windows 10 x64 | Windows 11 x64 |
| --- | --- | --- |
| Démarrage portable, ouverture d'un workspace 1.0 existant | ☐ | ☐ |
| Projet simple et ultra-simple, Notes rapides inchangées | ☐ | ☐ |
| Capture texte puis lien, menu et `Ctrl+Shift+I` | ☐ | ☐ |
| Raccourci global avec fenêtre réduite / en arrière-plan | ☐ | ☐ |
| Raccourci déjà utilisé, erreur visible, commande menu toujours utilisable | ☐ | ☐ |
| Import clipboard texte, bitmap PNG, fichier choisi | ☐ | ☐ |
| Boutons Annuler/Enregistrer au clavier, **aucune lecture automatique** | ☐ | ☐ |
| Doublon : choix Oui et Non, corbeille prise en compte | ☐ | ☐ |
| Recherche/filtres Inbox, lot, aperçu, archivage | ☐ | ☐ |
| Corbeille Inbox : supprimer/restaurer, collision sans overwrite | ☐ | ☐ |
| Convertir vers Notes rapides Global/Application/Projet | ☐ | ☐ |
| Convertir vers document, tâche et Fiche de Maintenance existante | ☐ | ☐ |
| Réessai d'une conversion : aucun doublon ; source conservée | ☐ | ☐ |
| Capture fichier disparu / disque plein / droits : fenêtre reste ouverte | ☐ | ☐ |
| Pièce jointe trop grande ou extension interdite : message lisible | ☐ | ☐ |
| Sauvegarder, restaurer, migration legacy, rollback updater | ☐ | ☐ |
| Recherche, liens, glossaire, Jalons, Kanban 1.0 après test Inbox | ☐ | ☐ |

Pour **chaque OS**, vérifier aussi l'ensemble des points d'interface
suivants :

- [ ] DPI 100 %, 125 % et 150 % (fenêtre 1024×768 et taille normale).
- [ ] Fenêtres Inbox, capture et conversion : **aucun texte ou bouton rogné**,
      focus accessible, entrée, Tab, Échap, ordre des contrôles.
- [ ] Dark mode : pas de menu déroulant, dialogue, hover, DatePicker ou
      surbrillance illisible ; infobulles et messages explicites.
- [ ] Fenêtre minimisée, active, inactive : raccourci global et
      remise au premier plan, absence de capture involontaire.
- [ ] Après une erreur récupérable, Nodalis reste utilisable et une
      entrée contextualisée est visible dans les diagnostics locaux.
- [ ] Vérifier qu'aucun accès réseau de l'application n'est effectué ;
      l'updater automatique éventuel est **hors périmètre** de
      l'absence de réseau du *workflow Inbox*.

**Résultat attendu :** joindre à #105 une synthèse de cette grille
et, si nécessaire, des **captures anonymisées**. La CI ne simule
pas des écrans Windows 10/11 interactifs et ne peut pas certifier
à elle seule les combinaisons DPI.

## Blocage obligatoire du tag stable

La publication d'une **version stable `1.1.x`** passe par un gate
supplémentaire dans `.github/workflows/release.yml` :

1. aucun ticket au label `blocker` n'est ouvert (gate commun) ;
2. l'EPIC **#96** et les issues Inbox **#99–#105** sont **fermés**
   après la recette interactive ;
3. aucun P0/P1 bloquant découvert dans la matrice n'est laissé ouvert ;
4. le commit candidat est celui qui vient de réussir la CI et la RC ;
5. release manuelle RC `1.1.0-rc.1` permise **avant** la clôture
   pour fournir un ZIP testable, sans publier un tag stable ;
6. après validation complète, tag `v1.1.0` et vérification de
   `Nodalis-win-x64.zip` et `SHA256SUMS.txt`.

Le nouveau gate empêche automatiquement la stable tant que les issues
nommées restent ouvertes. Les étapes manuelles de validation ne sont
pas « cochées » par un processus de CI : la responsabilité de
qualification appartient aux testeurs Windows.

Consulter aussi `release-checklist.md` pour les invariants 1.0 et
`release-notes-1.1.md` pour la présentation des changements.
