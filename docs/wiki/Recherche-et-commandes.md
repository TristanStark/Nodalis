# Recherche et palette de commandes

## Retrouver une information

Utilisez `Ctrl+F`. La recherche **fuzzy** est le mode par défaut : elle aide à retrouver des mots incomplets ou comportant une faute. Un mode **exact** permet de rechercher littéralement une chaîne sans tenir compte de la casse.

Les résultats sont contextualisés : **Projet**, puis **Application**, puis **Global** lorsque la pertinence est équivalente.

### Exemples de filtres

| Filtre | Exemple |
| --- | --- |
| Type | `type:specification` |
| Statut | `status:active` |
| Responsable | `owner:alice` |
| Tag | `tag:api` |
| Date | `date:2026-10-05` |
| Projet nommé | `project:"Projet Patate"` |
| Propriété personnalisée | `@reviewer:alice` |

Les valeurs avec espaces peuvent être mises entre guillemets.

## Palette de commandes

`Ctrl+P` affiche la **Command Palette**. Elle centralise la navigation rapide, des actions métier (dont création et ouverture) et des préférences locales. C'est un bon point d'entrée pour piloter Nodalis au clavier.

## Raccourcis de référence

| Raccourci | Action |
| --- | --- |
| `Ctrl+N` | Nouvelle note |
| `Ctrl+Shift+N` | Nouveau projet ou sous-projet |
| `Ctrl+Alt+N` | Capture d'une note rapide |
| `Ctrl+Shift+Q` | Notes rapides agrégées |
| `Ctrl+F` | Recherche |
| `Ctrl+K` | Lien interne |
| `Ctrl+P` | Palette de commandes |
| `Ctrl+S` | Enregistrement |
| `Ctrl+B` / `Ctrl+I` | Gras / italique |
| `Ctrl+` (touche accent grave) | Code inline |

Les raccourcis décrits sont ceux documentés pour la série 1.0 ; leur disponibilité dépend du contexte d'édition et des conflits clavier éventuels.

## Notes rapides

Les notes rapides existent au niveau Global, Application et Projet. La vue agrégée évite de devoir ouvrir tous les fichiers `Notes rapides.md` séparément.

**Référence :** [Guide utilisateur](https://github.com/TristanStark/Nodalis/blob/develop/docs/README-UTILISATEUR.md).
