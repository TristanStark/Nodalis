---
type: documentation
status: active
version: 1.5
tags: vues, filtre, projection
---
# Vues dynamiques 1.5

## Fonctionnement

Les vues regroupent les documents, tâches, jalons, décisions, incidents et interventions déjà présents dans le workspace. Elles sont calculées **sans dupliquer les fichiers sources**.

## Démonstration

Le fichier portable `.nodalis-views.json` propose :

- **Tâches à suivre** : tâches de démonstration regroupées par projet.
- **Jalons produit** : jalons classés par date.
- **Maintenance** : incidents et interventions du projet Ultra-simple.

Utiliser Ctrl+P → Vues dynamiques, puis parcourir les vues et ouvrir les documents source.

## Limites

Le filtre reste borné et déterministe. Les vues privées ne sont pas livrées, car elles appartiennent au poste utilisateur.

> Relation: documente | Cible: Présentation 1.x | ID: bb000000-0000-4000-8000-000000000003

Référence : [Notes v1.5](https://github.com/TristanStark/Nodalis/blob/develop/docs/release-notes-1.5.md).
