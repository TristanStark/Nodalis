# v1.7 — Gate de sortie de la recherche sémantique locale (#147)

**Validation utilisateur des issues #142–#147 acquise le 18 octobre 2026.** La release v1.7.0 peut être publiée après CI Windows verte et packaging reproductible. Les cases ci-dessous non vérifiées automatiquement demeurent des contrôles manuels à documenter et ne sont pas présentées comme réalisées.

## Validation automatique

- [x] Test provider fake : aucun appel sans activation et consentement, refus hors-local, modèle/dimension incompatibles.
- [x] Segmentation déterministe : positions source, titres répétés, YAML, code fenced, transclusions, dossiers cachés et longs documents.
- [x] Cache vectoriel local : écriture atomique, reconstruction si corrompu, suppression ciblée, invalidation modèle/dimension/segmentation, purge.
- [x] Cosinus réel : score borné, NaN/Inf/zéro, scope, dédoublonnage par GUID et tie-breaking.
- [x] Test de bout en bout fake : autorisation explicite, incrémentalité, suppression, sources modifiées et comparaison de requête sémantique.
- [ ] Dernière CI Windows Release vérifiée pour l'ensemble des modifications UI, publication portable reproductible et démarrage RC.
- [ ] Exécutable `Nodalis.EmbeddingBridge.exe` présent dans le package Windows ; `--self-test` JSON exécuté pendant le packaging sans connexion.
- [ ] Provider local *réel* Ollama compatible testé avec des embeddings multidimensionnels et des requêtes synonymiques françaises.

## Qualification UI et confidentialité

- [ ] Configuration Ctrl+P et recherche Ctrl+P entièrement au clavier, focus de retour, navigation des résultats.
- [ ] Dark mode et menus contrastés, DPI 100/125/150 %, fenêtres réduites, Windows 10 et 11.
- [ ] Aucun processus d'embedding créé lors de l'ouverture du workspace / lecture de notes / recherche classique.
- [ ] Consentement d'indexation pour chaque périmètre, annulation et reprise sans blocage.
- [ ] Erreurs fournisseur (absent, timeout, retour malformé) visibles sans données documentaires dans les logs.
- [ ] Exclusions supplémentaires pour les noms/dossiers à politique sensible, périmètre application/projet.
- [ ] Suppression du cache local et réindexation reproductible, workspaces 1.0–1.6 intacts.

## Performances et pertinence

Premier relevé synthétique et limites : [mesures CI 1.7](semantic-1.7-benchmarks.md).


- [ ] Corpus de référence français avec paires de synonymes et requêtes sans mot commun.
- [ ] Mesurer precision@5, recall@10 et latence, sans présenter les cosinus comme des probabilités.
- [ ] Benchmarks Small/Medium/Large : durée, mémoire, nombre de fragments, stabilité d'annulation.
- [ ] Comparer recherche exacte/fuzzy avec fonctionnalité sémantique désactivée ; pas de régression notable.

## Retour arrière

Revenir à v1.6.0 ne change pas le contenu Markdown du workspace. Le cache vectoriel est local, dérivé et peut être supprimé sans restauration de sources. Les nouveaux champs de préférences sont facultatifs et désactivés par défaut.
