# Recherche sémantique Nodalis 1.7 — adaptateur embeddings local (#142)

**État : aperçu fonctionnel sur `develop` ; ni release 1.7 ni validation Windows/DPI définitive.**

## Confidentialité par défaut

- `UserPreferences.SemanticSearch.IsEnabled = false` sur tous les workspaces existants.
- Cette activation est **distincte** de `Ai.IsEnabled`, réservée à la génération de texte.
- Chaque lot exige également une approbation explicite avant de transmettre son contenu au moteur configuré.
- Aucun fournisseur n'est sollicité au démarrage, aucun serveur distant n'est autorisé par la couche d'exécution Nodalis.
- Si un moteur est absent, la recherche exacte/fuzzy historique continue de fonctionner sans modification.
- La palette donne accès à la configuration, à la recherche sémantique et à la suppression du cache. Une confirmation explicite précède chaque indexation de périmètre ; l'absence de modèle désactive les commandes actives de recherche.

## Adaptateur Ollama fourni dans l'archive portable

Depuis `develop`, `Nodalis.EmbeddingBridge.exe` est livré à côté de `Nodalis.exe`. Il est entièrement facultatif. Le pont n'autorise **que** l'API `http://127.0.0.1:11434/api/embed` d'Ollama installé localement : aucune URL ni redirection configurable, proxy désactivé, pas de téléchargement de modèle depuis Nodalis.

Pour l'utiliser :

1. Installer **Ollama** sur la machine, puis installer explicitement un modèle d'embeddings, par exemple `ollama pull embeddinggemma` (modèle avec sortie standard de **768 dimensions**).
2. Dans Nodalis, **Ctrl+P → Configurer la recherche sémantique locale → Configurer Ollama local**. Ce bouton renseigne le pont, le modèle et ses dimensions, mais **n'active pas** la case de consentement. Cocher et enregistrer seulement si vous le souhaitez.
3. **Ctrl+P → Rechercher par le sens (local)** : choisir le périmètre, confirmer chaque indexation, puis saisir la requête. L'application démarre le pont uniquement pendant la demande et attend sa réponse JSON.
4. En cas de modèle absent, Ollama arrêté, dimension incorrecte ou expiration du délai : un diagnostic s'affiche sans texte source, et la recherche fuzzy/exacte historique reste disponible.

Le pont a un test hors ligne `Nodalis.EmbeddingBridge.exe --self-test`, utilisé pendant le packaging Windows. Ce test ne contacte pas Ollama ; il vérifie seulement le contrat JSON. Il **ne valide pas la qualité des embeddings**. Le pont est un projet .NET natif isolé sous `tools/`, distinct du cœur de Nodalis et sans aucune dépendance NuGet. Les capacités réseau sont confinées au pont et à une adresse IPv4 de boucle locale : aucun code réseau n'est ajouté dans `src/`.

Il reste nécessaire de faire une recette avec une installation réelle d'Ollama et un modèle chargé, notamment pour la latence et la pertinence en français.

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
