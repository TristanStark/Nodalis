# Nodalis 1.8 (develop) — Recherche hybride lexicale et sémantique

**Fonction en cours de qualification (#148)**. Cette page ne décrit pas une release publiée. La dernière release stable reste v1.7.0.

## Modes du panneau Ctrl+P → Rechercher par le sens (local)

Le dialogue de développement propose désormais trois modes :

- **Mixte : exacte/fuzzy + sémantique** : recherche les textes avec le moteur existant, recherche des passages vectoriels réels dans le cache approuvé, puis fusionne par document.
- **Texte seul : exacte/fuzzy (sans modèle)** : fonctionne même si l'option d'embeddings est désactivée, si Ollama est arrêté ou si aucun index n'existe. Ce mode **ne lance jamais de fournisseur**. Les recherches classiques Ctrl+F restent inchangées.
- **Sens seul : embeddings locaux** : résultats vectoriels uniquement ; requiert un modèle compatible installé, l'option activée et un cache indexé volontairement.

Le panneau affiche pour chaque résultat « lexical », « sémantique » ou « mixte », le fichier et la ligne source. Une similarité cosinus n'est affichée que lorsqu'un vrai résultat sémantique la fournit. Les liens ouvrent les documents Markdown canoniques.

## Pourquoi le fusionneur RRF ?

Les scores lexicaux existants (poids des titres, headings, contexte) et les cosinus d'embeddings n'ont **pas la même échelle**. Nous les fusionnons par *rangs*, sans les comparer numériquement :

`score(d) = 1.25 / (60 + rang_lexical(d)) + 1 / (60 + rang_semantique(d))`

Un canal absent contribue **zéro**. Un identifiant exact trouvé dans le titre de document reçoit un bonus fixe de `0.04` pour ne pas être masqué par une paraphrase. Les doublons sont fusionnés par **GUID stable** du document. L'ordre des égalités utilise le chemin puis le GUID pour rester reproductible. Le score fusionné est un **score de rang**, pas une probabilité.

Implémentation : `src/Nodalis.Core/Search/HybridSearchRanker.cs`. Les constantes sont explicitement versionnées et pourront être rendues configurables dans la suite de #148, après mesure de pertinence.

## Filtres, confidentialité et défaillances

Les filtres structurés `tag:`, `status:`, `owner:`, `project:`, `date:` et les champs `@personnalisés:` reprennent le parseur lexical d'origine. Seul le texte libre est envoyé au moteur d'embeddings local ; les résultats vectoriels doivent ensuite satisfaire **les mêmes filtres exacts** (front matter / projet / date) que la recherche classique.

Un périmètre de dossier restreint les résultats aux chemins de ce dossier. Les exclusions de documents de l'index vectoriel ne doivent **pas** retirer ces documents de la recherche classique. Une recherche Texte seul ne crée jamais d'index.

Si le provider tombe en erreur dans le mode Mixte, les résultats lexicaux restent affichés avec un message de retour au mode sans IA. Le dialogue n'invente aucun résultat sémantique et ne modifie jamais les Markdown.

## Tests et limites restant ouverts

Des smoke tests déterministes couvrent les identifiants exacts, les synonymes retrouvés **par un faux classement vectoriel**, la déduplication par GUID, le fallback sans fournisseur, le filtrage du front matter et la séparation texte/filtres. **Ces fixtures ne démontrent pas la qualité d'un vrai modèle d'embeddings**.

Avant fermeture de #148 et passage à #149, vérifier : recherche de titres techniques et de synonymes avec Ollama réel, pertinence du top 5, UX clavier et dark mode, interactions entre filtres et scope, annulation, perte du modèle, tests Windows CI et comparaison sans régression du moteur historique. Une recherche mixte sur un workspace non indexé revient naturellement aux seuls documents lexicaux ; elle ne déclenche jamais une indexation implicite.
