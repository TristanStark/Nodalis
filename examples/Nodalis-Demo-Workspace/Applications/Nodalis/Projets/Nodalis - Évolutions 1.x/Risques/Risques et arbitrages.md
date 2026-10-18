---
type: risk
status: active
tags: risque, securite, stabilite
---
# Risques et arbitrages

| Risque | Probabilité | Impact | Mitigation | Statut |
| --- | --- | --- | --- | --- |
| Perte des GUID du registre de liens | Faible | Élevé | Sauvegarder `.nodalis-links.json` | Surveillé |
| Modification concurrente d'une note | Moyenne | Élevé | Détection de conflit et sauvegarde atomique | Maîtrisé |
| Rendu d'une inclusion récursive | Faible | Moyen | Bornes de profondeur et détection de cycle | Maîtrisé |
| Fausse disponibilité d'une fonction 1.7 | Moyenne | Moyen | Marquer les scénarios comme fictifs | Ouvert |

## Références

[[Spécification des liens]] · [[Documents composites 1.6]] · [[Plan de recette]]
