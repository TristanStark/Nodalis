# Vues dynamiques — guide utilisateur 1.5

La version 1.5 est publiée avec les vues dynamiques. Ouvrir
**Ctrl+P → Vues dynamiques (aperçu 1.5)**, puis enregistrer ou sélectionner une vue.

## Découvrir et filtrer

L'écran rassemble les documents et les tâches, jalons et enregistrements
de maintenance lus depuis les sources du workspace. Les décisions sont maintenant collectées dans les projets et applications du
workspace. La collecte peut être longue sur de très grands espaces de travail.

1. Choisir le **type d'élément** ou conserver « Tous ».
2. Saisir un mot du **titre** ou du **statut**.
3. Choisir le **tri** et le **regroupement** (projet, type, statut).
4. Passer de **Liste** à **Tableau** ou **Cartes**. Les mêmes résultats
   sont affichés dans chaque présentation.
5. Double-cliquer une ligne pour **ouvrir sa source Markdown**, en respectant
   le GUID et la ligne d'origine. Un lien manquant n'est pas redirigé vers
   un document homonyme.

Les données affichées restent en lecture seule ; le formulaire ne modifie
pas les tâches ni les documents d'origine.

## Sauvegarder une vue

Donner un nom à la vue et choisir où l'enregistrer :

- **Workspace** : définition JSON portable dans `.nodalis-views.json`,
  incluse dans l'espace partagé et ses sauvegardes.
- **Privée** : préférences locales, séparées du workspace (et donc de
  ses sauvegardes/partages automatiques).

Cocher **Épingler** pour retrouver la vue sur l'accueil et dans **Mes vues**,
sans changer de workspace. Depuis **Ctrl+P**, rechercher « Vue · nom » pour
l'ouvrir directement, ou « Vues dynamiques » pour créer une vue temporaire.

Cliquer **Enregistrer** pour créer ou modifier la définition sélectionnée ;
**Dupliquer** crée une autre identité GUID ; **Supprimer** retire uniquement
la définition après confirmation, jamais les résultats ou les notes.

La persistance est atomique, les modifications externes concurrentes donnent
un conflit visible et un fichier d'une version future n'est jamais réécrit.

## Limites connues de la version 1.5

- Le formulaire visuel expose les filtres simples. Le moteur accepte également
  des groupes ET/OU et des comparaisons typées de dates (voir documentation
  technique) ; l'édition graphique avancée reste à terminer.
- Le rendu est paginé, par défaut à 200 éléments, avec boutons
  **Précédent / Suivant**. Les requêtes sont exécutées hors du fil WPF,
  annulées lors du changement de filtre, puis seules les plus récentes
  peuvent actualiser l'écran.
- Les modifications externes des fichiers Markdown et métadonnées sont
  détectées ; plusieurs notifications rapprochées sont regroupées avant
  rechargement. La collecte de maintenance utilise l'index de liens actualisé
  pour retrouver les fiches déplacées. Une projection en mémoire évite les scans lors des
  simples réouvertures sans modification.
- L'index des liens réutilise ses empreintes incrémentales existantes ;
  le collecteur des tâches réutilise lui aussi les documents inchangés grâce
  à ces empreintes et invalide ceux qui ont changé de contexte. Les jalons,
  décisions et fiches maintenance peuvent encore être rescannés après une
  modification : optimisation granulaire #133 à poursuivre.
  Les résultats ne sont jamais utilisés comme source canonique.
- Les rendus et parcours clavier/dark mode/DPI ont été acceptés lors de la
  validation de sortie ; conserver la checklist de qualification pour toute
  régression future.

Références :
[requêtes et projection](views-core-1.5.md) ·
[JSON des vues](views-storage-1.5.md) ·
[EPIC #125](https://github.com/TristanStark/Nodalis/issues/125).
