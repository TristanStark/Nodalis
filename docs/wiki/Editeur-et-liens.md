# Éditeur Markdown, liens et flowcharts

## Écrire en Markdown

L'éditeur travaille directement sur des fichiers `.md`. Il propose la saisie, l'aperçu riche, un enregistrement automatique sûr et la détection des modifications externes.

Raccourcis utiles : `Ctrl+B` (gras), `Ctrl+I` (italique), `Ctrl+S` (sauvegarder), `Ctrl+K` (lien interne).

Un document peut contenir un **front matter optionnel** :

~~~markdown
---
type: specification
status: active
owner: Alice
tags: api, recette
reviewer: Bob
---
# Architecture de l'API
~~~

Les propriétés standards incluent `status`, `owner`, `version`, `environment`, `type` et `tags`. Des champs personnalisés sont également admis. Le front matter n'est jamais obligatoire.

## Liens internes et backlinks

Pour mentionner une autre note :

~~~text
Voir [[Architecture de l'API]] pour le détail.
~~~

La vue des **backlinks** montre les documents faisant référence à la note actuelle. Un renommage peut préserver la résolution grâce aux identifiants stables et aux alias.

Une relation métier typée est plus explicite qu'un simple lien textuel :

~~~markdown
> Relation: dépend de | Cible: Architecture API | ID: 42e15594-93d4-4318-9d71-5ebccb65c55c
~~~

Types proposés : `dépend de`, `remplace`, `implémente`, `teste`, `documente`, `bloque`, `est lié à`. L'ID doit correspondre à une cible existante : **ne copiez pas un GUID d'exemple comme s'il s'agissait d'une vraie relation**.

## Flowcharts Mermaid, rendus hors ligne

Nodalis accepte un sous-ensemble portable de Mermaid dans des blocs Markdown :

~~~mermaid
flowchart TD
    A[Demande] --> B{Validée ?}
    B -->|Oui| C[Traitement]
    B -->|Non| D[Retour]
~~~

Le rendu utilise WPF localement, **sans Mermaid.js, JavaScript ni réseau**. Les orientations TD/TB/BT/LR/RL et plusieurs formes de nœuds et liens sont disponibles.

Les sous-graphes avancés, styles/classes Mermaid et actions `click` ne sont pas pleinement pris en charge : une construction non supportée doit afficher un avertissement, pas modifier le texte enregistré.

**Références :** [Guide des flowcharts](https://github.com/TristanStark/Nodalis/blob/develop/docs/markdown-flowcharts.md) · [Format Markdown et relations](https://github.com/TristanStark/Nodalis/blob/develop/docs/workspace-format.md).

## Analyse d'impact (v1.3 / v1.4 sur develop)

Les relations `dépend de`, `implémente`, `teste` et `documente` alimentent
l'analyse des impacts potentiels. Voir [[Analyse-d-impact]] pour la vue liste,
le graphe WPF et les références aux incidents/interventions.
