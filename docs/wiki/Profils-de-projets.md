# Profils de projets

Un profil définit **uniquement les fichiers et sections proposés au départ**. Il n'impose pas la structure pour toute la vie du projet. Les sous-projets disposent des mêmes profils que les projets.

## Ultra-simple : une fiche, pas une usine à gaz

Après le choix de l'emplacement et du profil, la seule donnée métier demandée est **Référence / nom**.

Nodalis crée uniquement :

- `Notes rapides.md` ;
- `Fiche de Maintenance.md` ;
- `.project.json`, le manifest technique.

La fiche de maintenance s'ouvre immédiatement. Il n'y a **ni jalon, ni réunion, ni présentation, ni dossier de tests** créé automatiquement. L'utilisateur peut ensuite ajouter librement des sections et documents.

Ce profil convient à une intervention, une petite maintenance, un suivi ponctuel ou une note qui mérite d'être rattachée à une application.

## Simple, Moyen et Complexe

| Section créée ou prévue | Simple | Moyen | Complexe |
| --- | :---: | :---: | :---: |
| Jalons | ✓ | ✓ | ✓ |
| Technique | ✓ | ✓ | ✓ |
| Glossaire | ✓ | ✓ | ✓ |
| Tests | ✓ | ✓ | ✓ |
| Documentation | — | ✓ | ✓ |
| Réunions | à la demande | ✓ | ✓ |
| Décisions | à la demande | ✓ | ✓ |
| Risques | — | ✓ | ✓ |
| Fonctionnel | — | — | ✓ |
| Exploitation | — | — | ✓ |
| Déploiement | — | — | ✓ |
| Dépendances | — | — | ✓ |

Pour les profils classiques, **Jalons** et **Glossaire** correspondent à des fichiers (`Jalons.md` et `Glossaire.md`), non à des dossiers.

Le profil Simple vise les projets légers ; Moyen ajoute des éléments de structuration ; Complexe propose des espaces détaillés de conception, exploitation et validation.

## Personnaliser les templates

La fenêtre **Templates et profils de projet** permet de modifier, prévisualiser, dupliquer et restaurer les templates, ainsi que de personnaliser les sections des profils.

Ces réglages sont persistés dans `Templates/templates.json`, `Templates/project-profiles.json` et des fichiers Markdown du workspace. Modifier un profil agit sur **les futurs projets**, sans restructurer ceux déjà existants.

**Référence :** [Documentation complète des profils](https://github.com/TristanStark/Nodalis/blob/develop/docs/project-profiles.md).
