---
type: test
status: active
owner: Validation
tags: regression, qualite
---
# Non régression

## Contrôles automatisés

Le dépôt fournit `tests/Nodalis.SmokeTests` et des workflows CI Windows. L'existence de ces contrôles ne remplace pas la validation ergonomique interactive.

| Famille | Exemple |
| --- | --- |
| Liens | Résolution par GUID et renommage |
| Maintenances | Ajout et modification sans perte des notes |
| Vues | Filtres, pagination, annulations |
| Composites | Cycle, section ambiguë, export local |
| Stabilité | Annulation, erreur de fichier, sauvegarde |

## Vérifications manuelles de la démonstration

- [ ] Parcourir le workspace avec le clavier | Responsable: Validation | Échéance: 2026-10-22 | Priorité: Normale | Statut: À faire | Tags: ergonomie, recette
- [ ] Tester zoom et thème sombre | Responsable: Validation | Échéance: 2026-10-22 | Priorité: Normale | Statut: À faire | Tags: dpi, recette
- [ ] Ouvrir et sauvegarder un document composite | Responsable: Validation | Échéance: 2026-10-23 | Priorité: Haute | Statut: À faire | Tags: composite, recette

> Relation: teste | Cible: Architecture des évolutions | ID: bb000000-0000-4000-8000-000000000004

Voir [[Plan de recette]] et [checklist v1.6](https://github.com/TristanStark/Nodalis/blob/develop/docs/release-checklist-1.6.md).
