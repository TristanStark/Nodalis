# Décision — Markdown canonique

**Date :** 2026-10-18
**Projet :** Nodalis - Évolutions 1.x
**Statut :** Active
**Source :** [[Réunion de planification]]

## Contexte

Les projections (tâches, jalons, décisions, vues, analyse d'impact) doivent être reconstruites à partir des données Markdown/JSON du workspace.

## Décision

Conserver le Markdown comme source métier. Ne pas persister les résultats projetés comme s'ils étaient des documents nouveaux.

## Justification

La portabilité et la récupération hors application évitent la dépendance à une base opaque.

## Impacts

La 1.5 enregistre uniquement les définitions des vues ; la 1.6 stocke uniquement les marqueurs d'inclusion. Les sources restent indépendantes.

## Sources et liens

- [[Vues dynamiques 1.5]]
- [[Documents composites 1.6]]
- [[Architecture Nodalis]]

> Relation: documente | Cible: Architecture des évolutions | ID: bb000000-0000-4000-8000-000000000004
