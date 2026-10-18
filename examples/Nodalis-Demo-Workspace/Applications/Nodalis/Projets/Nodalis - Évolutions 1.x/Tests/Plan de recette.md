---
type: test
status: active
owner: Validation
tags: recette, non-regression
---
# Plan de recette

## Objectif

Exercer une installation autonome du workspace, ses références stables, les projections transversales et la navigation vers la source.

## Matrice de tests

| ID | Fonction | Action | Attendu |
| --- | --- | --- | --- |
| DEMO-01 | Workspace | Ouvrir le dossier du jeu de démo | Projets, modules et documents visibles |
| DEMO-02 | Liens | Ouvrir [[Spécification des liens]] | Navigation et backlinks présents |
| DEMO-03 | Jalons | Ouvrir le calendrier | Dates de la table visibles |
| DEMO-04 | Maintenance | Lire les interventions du projet #122 | Incident associé et historique |
| DEMO-05 | Composites | Ouvrir [[Rapport de qualification 1.x]] | Sections source visibles en aperçu |
| DEMO-06 | Images | Ouvrir [[Architecture Nodalis]] | Illustration locale si moteur SVG supporté et Mermaid rendu |
| DEMO-07 | Vues | Ouvrir Tâches à suivre | Projection sans duplicata |

## Résultats attendus

- Pas de lien typé volontairement orphelin.
- Les jalons du tableau sont chargés dans la timeline.
- Les relations `teste` / `implémente` remontent dans l'analyse d'impact.
- Le rapport composite affiche des sections, sans réécriture des originaux.
- L'illustration SVG est fournie comme fichier local ouvrable hors Nodalis ; son rendu intégré dépend de la prise en charge SVG.

> Relation: teste | Cible: Spécification des liens | ID: bb000000-0000-4000-8000-000000000005

> Relation: teste | Cible: Documents composites 1.6 | ID: bb000000-0000-4000-8000-000000000007
