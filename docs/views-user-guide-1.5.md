# Vues dynamiques — aperçu utilisateur 1.5 (develop)

La v1.5 est **en cours de développement**. Le premier écran est accessible
par **Ctrl+P → Vues dynamiques (aperçu 1.5)**. La release stable actuelle
est **v1.4.0** ; ne pas confondre cet aperçu avec une version livrée.

## Découvrir et filtrer

L'écran rassemble les documents et les tâches, jalons et enregistrements
de maintenance lus depuis les sources du workspace. Une collecte des décisions
du projet ou de l'application sélectionnée est également disponible :
l'indexation de *toutes* les décisions du workspace fait partie de #133.

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

Cliquer **Enregistrer** pour créer ou modifier la définition sélectionnée ;
**Dupliquer** crée une autre identité GUID ; **Supprimer** retire uniquement
la définition après confirmation, jamais les résultats ou les notes.

La persistance est atomique, les modifications externes concurrentes donnent
un conflit visible et un fichier d'une version future n'est jamais réécrit.

## Limitations de l'aperçu

- Le formulaire visuel expose les filtres simples. Le moteur accepte également
  des groupes ET/OU et des comparaisons typées de dates (voir documentation
  technique) ; l'édition graphique avancée reste à terminer.
- Le rendu est borné à 200 éléments par page d'aperçu ; les grandes collections
  et la pagination interactive relèvent encore de #131 et #133.
- Les décisions multi-applications, l'actualisation incrémentale et les favoris
  de dashboard sont encore en cours (#132/#133).
- Le rendu dark mode, DPI 100/125/150 % et navigation complète au clavier
  font partie de la qualification finale #134.

Références :
[requêtes et projection](views-core-1.5.md) ·
[JSON des vues](views-storage-1.5.md) ·
[EPIC #125](https://github.com/TristanStark/Nodalis/issues/125).
