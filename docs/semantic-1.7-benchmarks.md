# Recherche sémantique 1.7 — premier état des performances (CI Windows)

## Relevé automatisé vérifiable

Exécution GitHub Actions [Build #39143220462](https://github.com/TristanStark/Nodalis/actions/runs/39143220462), Windows runner, 18 octobre 2026. Les mesures proviennent de la sortie des tests `SemanticPerformanceSmokeTests` :

| Fixture synthétique | Mesure observée |
|---|---:|
| 50 vecteurs × 32 dimensions, recherche cosinus + dédoublonnage | 0 ms (en dessous de la résolution affichée) |
| 500 vecteurs × 32 dimensions, recherche cosinus + dédoublonnage | 0 ms (en dessous de la résolution affichée) |
| 5 000 vecteurs × 32 dimensions, recherche cosinus + dédoublonnage | 3 ms |
| Document Markdown 320 021 caractères, 5 000 passages | 17 ms |

Ce sont des **ordres de grandeur de tests unitaires synthétiques, pas des mesures utilisateur ni des garanties de performance**. Elles n'incluent pas le chargement du modèle local, l'appel au processus Ollama, les opérations d'indexation persistante ou la lecture des sources sur disque. Ces chiffres ne suffisent pas à valider le budget réel 1.7.

## Mesures encore à produire avant release

- Corpus représentatif Small / Medium / Large avec documents de projets variés, sections imbriquées, pièces jointes et changements externes.
- Nombre de passages et taille du cache, mémoire maximale, p95 indexation initiale / reprise et p95 recherche requête courte/longue.
- Même environnement Windows et configuration DPI pour comparer l'interface, modèle en mémoire ou froid, et annulations.
- Modèle local réel multilingue, par exemple embeddinggemma dans Ollama local, avec mesures de **precision@5** et **recall@10** sur des paires françaises proches par le sens et lexicalement différentes.
- Comparaison de latence de recherche exacte/fuzzy avec sémantique désactivée. Aucune régression du mode 1.0 ne doit être acceptée.

## Méthode proposée de calcul

Pour un ensemble de requêtes françaises annotées avec leurs documents pertinents `R(q)` :

- `precision@k = |top_k(q) ∩ R(q)| / k` (ou nombre de résultats retournés si le jeu le définit explicitement).
- `recall@k = |top_k(q) ∩ R(q)| / |R(q)|` quand `R(q)` est non vide.
- Présenter la moyenne macro par requête et un relevé des requêtes ratées, sans confondre similarité cosinus et probabilité.

Les modèles et corpus de test doivent rester **hors données métier canoniques** et le projet doit rester utilisable sans embeddings.

## Vérifications de packaging

Le pipeline `scripts/publish-portable.ps1` publie désormais le pont facultatif `Nodalis.EmbeddingBridge.exe` et exécute son `--self-test` **hors réseau**. Le port est fixé dans le programme à `127.0.0.1:11434`. Aucune requête d'embedding n'est exécutée pendant la CI et aucun modèle n'est téléchargé.

La recette réelle Ollama, les DPI 100/125/150 % et Windows 10/11 restent des **gates manuelles non vérifiées**.
