# Recherche sémantique Nodalis 1.7 — adaptateur embeddings local (#142)

**État : aperçu fonctionnel sur `develop` ; ni release 1.7 ni validation Windows/DPI définitive.**

## Confidentialité par défaut

- `UserPreferences.SemanticSearch.IsEnabled = false` sur tous les workspaces existants.
- Cette activation est **distincte** de `Ai.IsEnabled`, réservée à la génération de texte.
- Chaque lot exige également une approbation explicite avant de transmettre son contenu au moteur configuré.
- Aucun fournisseur n'est sollicité au démarrage, aucun serveur distant n'est autorisé par la couche d'exécution Nodalis.
- Si un moteur est absent, la recherche exacte/fuzzy historique continue de fonctionner sans modification.
- La palette donne accès à la configuration, à la recherche sémantique et à la suppression du cache. Une confirmation explicite précède chaque indexation de périmètre ; l'absence de modèle désactive les commandes actives de recherche.

## Contrat local sans dépendance NuGet

Le modèle d'embeddings doit être exposé par un exécutable installé **localement**, parlant JSON sur son stdin/stdout (pas de requête HTTP réalisée par Nodalis).

Entrée :

```json
{"modelId":"modele-embedding-local","texts":["Phrase 1","Phrase 2"]}
```

Réponse :

```json
{"modelId":"modele-embedding-local","dimensions":3,"vectors":[[0.5,0.2,0.1],[0.1,0.2,0.9]]}
```

Les identifiants et dimensions doivent correspondre exactement à la configuration. Valeurs NaN/Inf et vecteurs nuls sont rejetés. Batches limités à 32 textes de 8 192 caractères, dimensions à 4 096, délai à 300 secondes. Les sorties d'erreur et contenus ne sont jamais recopiés dans les diagnostics.

Un LLM conversationnel **n'est pas** automatiquement un modèle d'embeddings. Le fournisseur local doit générer des vecteurs pertinents pour des recherches sémantiques ; un adaptateur CLI compatible est à configurer explicitement. Les exécutables distants ou chemins UNC et URL sont refusés.

## État des développements

Les moteurs #143 (segmentation), #144 (cache local reconstructible), #145 (cosinus et provenance) et le premier écran #146 sont implémentés sur `develop`. L'indexation **n'est jamais automatique**. La qualification visuelle Windows et les benchmarks représentatifs (#147) restent à effectuer avant sortie stable.
