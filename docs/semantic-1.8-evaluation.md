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
