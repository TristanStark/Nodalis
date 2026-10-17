# Premiers pas : votre premier projet

## 1. Ouvrir un workspace unique

Au premier démarrage, Nodalis demande un dossier de workspace. Il regroupera l'ensemble des applications et projets. Vous pourrez passer d'un projet à l'autre dans la même fenêtre, sans changer de workspace.

Le dashboard d'accueil permet de retrouver les éléments récents, favoris, tâches ouvertes et jalons à venir.

## 2. Créer une application

Une **application** représente un produit, un système ou un périmètre de travail. Elle peut contenir directement des projets et, si nécessaire, des **modules** pour regrouper les projets.

Exemple d'organisation :

~~~text
Application Métier
├── Module Paiements
│   ├── Projet Refonte API
│   └── Projet Recette V2
└── Projet Maintenance quotidienne
~~~

Les modules servent à classer : ce ne sont pas des workspaces distincts.

## 3. Créer un projet

Utilisez la commande de création de projet ou `Ctrl+Shift+N`, choisissez sa destination, puis son profil :

- **Ultra-simple** : une seule référence / un nom, puis `Notes rapides.md` et `Fiche de Maintenance.md`. Parfait pour une fiche ponctuelle.
- **Simple** : jalons, technique, glossaire et tests.
- **Moyen** : sections supplémentaires de documentation, réunions et risques.
- **Complexe** : structure plus détaillée pour les grands projets.

Les sections de départ restent modifiables. Voir [[Profils-de-projets]].

## 4. Écrire et retrouver l'information

1. Créez une note avec `Ctrl+N`.
2. Écrivez en Markdown et consultez l'aperçu.
3. Référencez une autre note avec `[[Nom du document]]`.
4. Appuyez sur `Ctrl+S` pour forcer l'enregistrement, même si l'autosave est actif.
5. Retrouvez la note avec `Ctrl+F` ou ouvrez une commande avec `Ctrl+P`.

## 5. Capturer une idée sans interrompre son travail

`Ctrl+Alt+N` ouvre la capture d'une **note rapide**. Elle appartient au contexte Global, Application ou Projet. `Ctrl+Shift+Q` ouvre la vue des notes rapides agrégées.

## 6. Suivre le travail

Dans un document Markdown, écrivez par exemple :

~~~markdown
- [ ] Vérifier la recette | Responsable: Alice | Échéance: 2026-10-25 | Priorité: Haute | Statut: En cours
- [x] Relire la spécification
~~~

La vue des tâches et le Kanban lisent ces checkboxes. Les données ne sont pas dupliquées dans une base inaccessible.

## À consulter

[[Organisation-du-workspace]] · [[Editeur-et-liens]] · [[Taches-Kanban-et-calendrier]]
