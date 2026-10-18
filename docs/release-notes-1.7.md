# Nodalis v1.7.0 — Recherche sémantique locale facultative

**Windows 10/11 x64 · .NET 10 · archive autonome · Markdown et JSON locaux**

## En bref

La recherche par le sens complète désormais la recherche historique exacte/fuzzy. Cette première version utilise un **modèle d'embeddings installé volontairement en local**, sans modèle téléchargé par Nodalis et sans indexation automatique.

Exemple : une requête « anomalies de mouvements » peut rapprocher un passage sur des « doublons d'inscriptions » selon la qualité du modèle d'embeddings choisi. Les scores sont des similarités cosinus, **pas des probabilités ni des conclusions métier**.

## Nouveautés (issues #142 à #147)

- **Fournisseur local facultatif** : pont `Nodalis.EmbeddingBridge.exe`, protocole JSON stdin/stdout, adaptateur Ollama limité à `127.0.0.1:11434`, test de disponibilité sans divulguer des documents.
- **Consentement explicite à deux niveaux** : activation de la fonctionnalité puis confirmation du périmètre avant chaque indexation ; aucune exécution du provider au lancement.
- **Segmentation Markdown** : passages bornés, exclusion du front matter, code et contenus virtuellement inclus ; provenance vers document, section et ligne.
- **Cache vectoriel dérivé** : stockage local sous `%LOCALAPPDATA%/Nodalis/SemanticIndexes`, invalidation au changement de modèle, de dimension ou des exclusions ; purge sans suppression des fichiers Markdown.
- **Recherche par similarité cosinus** : classements stables, source et extrait actuels, navigation vers la ligne du document, suppression des résultats périmés.
- **Configuration sans packages tiers** : programme .NET natif ; fonctionnalité désactivée par défaut, indépendante du réglage IA pour la génération de texte.
- **Changement de workspace** via Système ou Ctrl+P, par sélection d'un dossier existant contenant `.workspace.json` ; relance de l'application pour basculer.
- **Workspace de démonstration** fourni dans `examples/Nodalis-Demo-Workspace` (répertoire du dépôt), avec notes, jalons, relations, décisions, illustrations et projet Ultra-simple.

## Démarrage de la recherche sémantique

1. Installer Ollama localement et télécharger **manuellement** un modèle d'embeddings compatible, par exemple `ollama pull embeddinggemma`.
2. Dans **Ctrl+P → Configurer la recherche sémantique locale**, sélectionner le pont Ollama proposé, tester facultativement le modèle, **activer explicitement** la fonctionnalité puis enregistrer.
3. Dans **Ctrl+P → Rechercher par le sens (local)**, limiter si besoin le dossier cible, choisir **Indexer ce périmètre** puis confirmer l'envoi des passages au fournisseur **local**.
4. Saisir une requête et ouvrir les résultats sur leur source Markdown. **Effacer le cache sémantique** le supprime sans modifier le workspace. Le mode exact/fuzzy reste accessible en permanence.

Lire [le guide complet](semantic-1.7-guide.md), [le contrat du fournisseur](semantic-1.7-provider.md) et [les données de validation et leurs limites](semantic-1.7-benchmarks.md).

## Confidentialité et compatibilité

Sans activation, index approuvé et fournisseur réellement compatible, aucune recherche sémantique n'est simulée : utiliser la recherche exacte/fuzzy. Le pont fourni communique uniquement avec le serveur Ollama sur la boucle locale ; un exécutable CLI personnalisé est sous la responsabilité de son utilisateur. Le cache n'est pas une source métier et se reconstruit ; les workspaces des versions 1.0 à 1.6 restent des fichiers Markdown/JSON lisibles.

Pour tester le workspace de démonstration après extraction du dépôt, suivre [son guide d'ouverture](../examples/README-workspace-demo.md).

## Limites connues et qualification

- **Pas encore de fusion lexicale + sémantique dans une même liste** : prévue pour la 1.8 (#148). Le classement cosinus dépend du modèle et ne prouve jamais une relation causale.
- L'indexation est **déclenchée par l'utilisateur**, non en tâche de fond. Les 20 premiers documents au maximum sont affichés, avec un passage par document.
- Les relevés chiffrés existants concernent des **tests synthétiques** : ils n'incluent ni démarrage d'Ollama ni indexation d'un grand corpus réel. La validation réelle de synonymes français, la latence Ollama, la recette multi-DPI et multi-OS nécessitent des mesures sur le poste cible.
- Les tests automatisés Windows Release, les smoke tests, la compilation du pont, le packaging reproductible et le lancement portable sont assurés par la CI ; ils **ne remplacent pas** tous les contrôles manuels affichés dans [la checklist](release-checklist-1.7.md).

## Installation et retour arrière

Télécharger `Nodalis-win-x64.zip` et vérifier `SHA256SUMS.txt`, sauvegarder son workspace puis remplacer les fichiers de l'application. Conserver **les deux exécutables** `Nodalis.exe` et `Nodalis.EmbeddingBridge.exe` pour utiliser Ollama. Un retour en 1.6 conserve les notes : seul le mode de recherche sémantique et la commande de changement de workspace disparaissent. Le cache vectoriel dérivé peut être effacé sans effacer les notes.

**Suivi** : EPIC [#127](https://github.com/TristanStark/Nodalis/issues/127), issues [#142–#147](https://github.com/TristanStark/Nodalis/issues/142). Les prochains travaux sont #148, #149 et #150 (version 1.8).
