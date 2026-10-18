# Décision — Liens stables

**Date :** 2026-10-17
**Projet :** Nodalis - Évolutions 1.x
**Statut :** Active
**Source :** [[Réunion de planification]]

## Contexte

Le nom et le chemin d'un fichier changent au cours de la vie du projet, mais les liens doivent conserver leur cible. Le dépôt utilise le registre `.nodalis-links.json`.

## Décision

Conserver les GUID documentaires dans la collection `targets` ; utiliser l'ID pour les relations métier et les inclusions. Les libellés humains ne sont pas des identités.

## Justification

Les renommages et déplacements ne doivent pas rediriger silencieusement une référence vers un homonyme.

## Impacts

Le workspace de démonstration fournit un registre préinitialisé. Les tests d'impact et les composites reposent sur ces IDs.

## Sources et liens

- [[Spécification des liens]]
- [[Architecture des évolutions]]
- [Contrat de persistance Nodalis 1.0](https://github.com/TristanStark/Nodalis/blob/develop/docs/data-contracts-v1.md)

> Relation: documente | Cible: Spécification des liens | ID: bb000000-0000-4000-8000-000000000005
