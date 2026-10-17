# Tâches, Kanban, jalons et calendrier

## La source de vérité reste le Markdown

Une tâche est une checkbox Markdown ; la vue consolidée, le Kanban et le calendrier projettent ces données, sans créer une base métier parallèle :

~~~markdown
- [ ] Préparer la recette | Responsable: Alice | Échéance: 2026-10-25 | Priorité: Haute | Statut: En cours | Tags: api, recette
- [x] Valider l'architecture
~~~

Les métadonnées sont facultatives. La case `[ ]` ou `[x]` fait foi pour l'état ouvert / terminé, indépendamment du libellé `Statut`.

### Champs courants

- `Responsable` / `Owner` : texte libre.
- `Échéance` / `Due` : date au format `AAAA-MM-JJ`.
- `Priorité` / `Priority` : Critique, Haute, Normale, Basse ou une valeur personnalisée.
- `Statut` / `Status` : état métier libre.
- `Tags` / `Tag` : liste séparée par des virgules.

La liste consolidée permet de filtrer par contexte et de revenir au document source. Le Kanban représente les mêmes tâches.

## Jalons

Pour les profils classiques, les jalons sont enregistrés dans **`Jalons.md`**, à la racine du projet. Ils peuvent décrire date cible, statut, description, liens et dépendances. Nodalis propose une **liste** et une **timeline** chronologique.

Une dépendance incohérente ou cyclique doit être signalée plutôt que modifiée silencieusement.

## Calendrier

Le calendrier agrège les éléments datés du workspace, notamment les tâches à échéance et les jalons. Les filtres servent à limiter le contexte affiché et la navigation ramène à la source.

## Conseils d'utilisation

Écrivez d'abord les tâches dans le document où se trouve le contexte métier ; utilisez ensuite le Kanban pour les suivre et le calendrier pour les échéances. Pour un projet **Ultra-simple**, aucune vue de jalons ne crée automatiquement un fichier `Jalons.md` à sa création.

**Références :** [Contrats des tâches et jalons](https://github.com/TristanStark/Nodalis/blob/develop/docs/workspace-format.md) · [Guide utilisateur](https://github.com/TristanStark/Nodalis/blob/develop/docs/README-UTILISATEUR.md).
