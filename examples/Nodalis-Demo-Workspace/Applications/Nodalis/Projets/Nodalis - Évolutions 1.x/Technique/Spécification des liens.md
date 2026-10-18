---
type: specification
status: active
owner: Développement
version: 1.4
tags: liens, guid, impact
---
# Spécification des liens

## Identité

Un document possède un GUID stable via `.nodalis-links.json#/targets`. Les relations typées reposent sur l'ID de la cible et ne doivent pas se rabattre automatiquement sur un nom homonyme.

## Résolution

- Liens lisibles : `[[Architecture Nodalis]]`.
- Liens non ambigus par ID : `[[bb000000-0000-4000-8000-000000000001]]`.
- Relations : `> Relation: TYPE | Cible: LIBELLÉ | ID: GUID`.

## Graphe d'impact

Une relation `implémente` ou `teste` écrite dans un document A vers B signifie : **si B change, A peut être affecté**. Les relations `est lié à` restent contextuelles.

## Critères d'acceptation

- [x] Conserver les GUID de cibles dans le registre | Responsable: Développement | Statut: Terminé | Tags: guid, recette
- [x] Ne pas propager une suggestion neutre | Responsable: Validation | Statut: Terminé | Tags: impact, recette
- [ ] Vérifier les liens sur un renommage manuel du jeu de démo | Responsable: Validation | Échéance: 2026-10-23 | Priorité: Normale | Statut: À faire | Tags: liens, recette

> Relation: documente | Cible: Architecture Nodalis | ID: bb000000-0000-4000-8000-000000000001

Sources : [Contrats de données](https://github.com/TristanStark/Nodalis/blob/develop/docs/data-contracts-v1.md), [Sémantique d'impact](https://github.com/TristanStark/Nodalis/blob/develop/docs/impact-semantics-1.3.md).
