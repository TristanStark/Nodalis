# Réunions et décisions

## Rédiger un compte rendu

L'assistant de réunion permet de créer un compte rendu Markdown, structurer son contenu et faire émerger les actions et décisions proposées.

Les projets peuvent disposer d'une section **Réunions**. Lorsqu'elle ne figure pas dans le profil initial, elle peut être créée à la première utilisation de la fonctionnalité.

Pour faciliter le suivi, distinguez dans le compte rendu les informations, les décisions effectivement validées et les tâches attribuées.

## Decision Records

Une décision validée peut devenir un **Decision Record** en Markdown, associé au compte rendu source. Les décisions sont consultables et recherchables par contexte.

Le cycle de vie documenté inclut notamment :

- **Active** : décision applicable ;
- **Deprecated** : décision abandonnée ;
- **Superseded** : décision remplacée par une autre.

Les liens vers les sources s'appuient sur l'identité des documents et restent navigables après un renommage.

## Modèle de compte rendu conseillé

~~~markdown
# Réunion — 2026-10-17

## Participants
- Alice
- Bob

## Points abordés
- ...

## Décisions actées
- ...

## Actions
- [ ] Mettre à jour la documentation | Responsable: Alice | Échéance: 2026-10-25
~~~

Ce modèle est **illustratif** : les templates peuvent être personnalisés. Ne confondez pas une décision simplement évoquée avec une décision formellement validée.

**Référence :** [Guide utilisateur Nodalis](https://github.com/TristanStark/Nodalis/blob/develop/docs/README-UTILISATEUR.md).
