---
type: guide
status: active
owner: Équipe Nodalis
tags: demo, prise-en-main
---
# DEPART — Visite guidée

Ce workspace est un **jeu de démonstration autonome** qui s'appuie sur l'histoire et l'architecture réelles de Nodalis jusqu'à **v1.6.0**, avec un scénario de travail **fictif** pour v1.7.

## Parcours conseillé

1. Ouvrir [[Présentation 1.x]] pour l'organisation du projet et les tâches.
2. Ouvrir [[Nodalis / Nodalis - Évolutions 1.x / Jalons]] depuis le projet **Nodalis - Évolutions 1.x** pour la timeline et le calendrier.
3. Parcourir [[Architecture Nodalis]] et [[Architecture des évolutions]] pour les documents et le flowchart Mermaid.
4. Depuis [[Spécification des liens]], examiner les relations typées, les backlinks puis l'analyse d'impact.
5. Lire [[Réunion de planification]] puis [[Décision - Markdown canonique]] pour les décisions.
6. Voir [[Fiche de Maintenance]] dans le projet **Refonte explorateur 122** : incident et interventions.
7. Tester [[Rapport de qualification 1.x]] : inclusion de sections Markdown de documents existants.
8. Ouvrir les vues enregistrées **Tâches à suivre**, **Jalons produit** et **Maintenance** depuis les vues dynamiques.

## Démonstrations complémentaires

- Recherche : rechercher `tag:recette`, `type:specification`, `impact`, `owner:Validation`.
- Notes rapides : Global, Application, puis Projet.
- Images : voir le diagramme autonome dans [[Architecture Nodalis]].
- Projet Ultra-simple : [[Fiche de Maintenance]] sous le module *Expérience utilisateur*.
- Sous-projet : *Recherche lexicale 1.7*.
- Glossaires : global, application et projet.

> Les demandes de la 1.7 sont **en préparation** ; les dates futures et les statuts de tâches sont des données de démonstration, pas des engagements de release.

## Qualification réelle de la v1.9 sur develop

Les fiches suivantes contiennent des **tâches à qualifier manuellement** (non fictives, toutes non cochées au départ), visibles dans les tâches du projet **Nodalis - Évolutions 1.x** et dans la vue **Tâches à suivre** :

1. [[Qualification 1.9 - Import DOCX]] — découpage, mapping, réimport, rollback.
2. [[Qualification 1.9 - Performance WPF]] — fluidité, recherche, rafraîchissements, sauvegardes.
3. [[Qualification 1.9 - Fonctionnalites connexes]] — jalons, maintenance, archives et arbitrage des P1.
4. [[Qualification 1.9 - Gate release]] — CI, vérifications, décision de sortie.

**Ne cocher qu'un scénario OK.** Si KO, conserver la tâche ouverte et consigner les conditions de reproduction dans son document puis dans l'issue GitHub indiquée. Tester les pannes et archivages sur une **copie jetable** de ce workspace. Ces tâches ne signifient pas que les développements P1 sont disponibles ou que la release 1.9 est validée.
