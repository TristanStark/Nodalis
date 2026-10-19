# Nodalis 1.8 (develop) — Évaluer la recherche (#149)

**Travail en cours, pas une annonce de publication.** Cette documentation définit des métriques reproductibles pour comparer le moteur exacte/fuzzy historique, les résultats sémantiques 1.7 et le rang hybride 1.8.

## Jeu de référence annoté

Chaque cas contient un identifiant de question, un ensemble de GUID de documents réellement pertinents, le classement obtenu par **un mode** et une durée mesurée. Les documents doivent être des fixtures neutres ou synthétiques : ne pas publier d'index ni de contenu professionnel confidentiel. Une même question et un même ensemble de sources attendues doivent servir aux trois modes.

Pour les requêtes en français, varier références exactes, paraphrases sans mots communs, noms d'incidents et de tests, réunions, glossaires, ambiguïtés et exclusions par périmètre. Annoter les documents pertinents **avant** de comparer les modes ; un exemple d'embedding simulé ne mesure pas la pertinence d'un vrai modèle.

## Formules et déduplication

Pour une requête avec au moins un document pertinent, après déduplication par GUID :

- `precision@5 = nombre de documents pertinents parmi les 5 premiers / 5` ;
- `precision@10 = nombre de documents pertinents parmi les 10 premiers / 10` ;
- `recall@10 = nombre de pertinents dans les 10 premiers / nombre total de pertinents`.

Les scores sont moyennés **par requête annotée (macro)** ; un cas sans vérité terrain n'entre pas dans le dénominateur. Les GUID nuls ou répétés ne gonflent pas les scores. Une liste plus courte que K garde K comme dénominateur de précision : la méthode doit rester identique entre les moteurs.

Les temps en millisecondes sont des durées mesurées, non extrapolées. La méthode de percentiles est le rang entier supérieur le plus proche : p50 et p95 sont choisis dans la liste ordonnée de temps relevés. La CI actuelle valide **le calcul**, pas la performance réelle d'Ollama.

Contrat C# : `SearchEvaluationMetrics.Evaluate(IReadOnlyList<SearchEvaluationCase>)`. Smoke tests dans `SearchEvaluationSmokeTests`.

## Suite prévue pour #149

- Produire des corpus **Small / Medium / Large** identiques pour les trois modes, avec chiffres de p50/p95 réels, taille d'index, pic RAM et vitesse d'indexation.
- Mesurer le coût d'une reprise après annulation, de modifications d'un seul passage et de suppressions. L'indexation 1.7 réutilise déjà les vecteurs de chunks inchangés ; vérifier son efficacité disque et mémoire à grande échelle.
- Tester un vrai modèle français/multilingue, documenter version/dimensions et les cas ratés ; ne pas confondre amélioration du score sur fixtures et amélioration sur données réelles.
- Vérifier que le mode Texte seul n'instancie pas de process d'embeddings et conserve sa latence historique.

Les critères de réussite chiffrés et leur budget mémoire doivent être décidés à partir de mesures reproductibles, pas d'estimations fabriquées.

## Exécuter le comparatif reproductible en français

Le dépôt inclut `benchmarks/Nodalis.Benchmarks/semantic-fr-corpus.json` (**14 fichiers fictifs et 14 questions annotées** : identifiants techniques, doublons, maintenance, SQL, décisions, jalons et paraphrases). Les sources sont copiées dans un dossier temporaire isolé, jamais dans un workspace utilisateur. Les mesures individuelles sont issues de ce corpus de test et ne constituent pas une estimation sur un projet réel.

Sur Windows avec le SDK .NET 10 :

```powershell
# Sans modèle : recherche fuzzy/exacte réelle, contrôle CI de non-régression.
dotnet run --project benchmarks/Nodalis.Benchmarks -- --semantic-eval

# Avec un véritable modèle local installé et accepté par l'utilisateur.
dotnet run --project benchmarks/Nodalis.Benchmarks -- --semantic-eval `
  --approve-local `
  --adapter "C:\CHEMIN\VERS\Nodalis.EmbeddingBridge.exe" `
  --model embeddinggemma --dimensions 768
```

Le programme **ne démarre aucun processus d'embeddings sans `--approve-local`**. Si l'adaptateur est demandé, il faut fournir le chemin absolu d'un exécutable local de confiance, un modèle d'embeddings (non conversationnel) et le nombre correct de dimensions. Le bridge local demande un service Ollama déjà installé ; les configurations réseaux non locales sont refusées par le fournisseur Nodalis. Les données synthétiques utilisées sont visibles dans le JSON, le code et les logs ne recopient aucun document confidentiel.

Le JSON peut être remplacé par un **corpus de test explicitement préparé** avec `--corpus-file FICHIER.json`, propriétés `documents` (`id`, `relativePath`, `markdown`) et `questions` (`name`, `query`, `relevantDocumentIds`). Les GUIDs attendus sont **annotés avant de lancer** un modèle. Le runner valide les identifiants, les chemins relatifs sûrs, la taille des sources et les documents référencés, puis efface sa copie temporaire à la fin. Pour éviter d'exposer des données professionnelles, ne transmettre ni corpus privé ni sortie brute à un service distant.

Les résultats JSON exposent `lexical` seul hors IA, et `lexical` / `semantic` / `hybrid` avec fournisseur réel : P@5, P@10, R@10 et p50/p95 en millisecondes (relevées pour l'ensemble des requêtes), durée d'indexation initiale/chaude, nombre de vecteurs créés, taille locale et mémoire du **processus benchmark**. La mémoire du modèle lancé dans un processus distinct **n'est pas comptabilisée**, et le p95 sur 14 questions est informatif plutôt qu'une distribution de production. L'index initial est effacé avec le répertoire temporaire après exécution. Les benchmarks sur workspace confidentiel réel restent à effectuer séparément sur un environnement approuvé.

La CI vérifie uniquement le parcours **lexical sans fournisseur** ; elle n'affiche jamais des scores simulés comme une preuve de qualité réelle. Le corpus français fournit le protocole pour effectuer une vraie comparaison locale en recette.
