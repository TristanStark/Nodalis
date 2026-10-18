# Nodalis v1.8 — Qualification avant publication

**État : EN DÉVELOPPEMENT. Ne pas taguer ni fusionner une release v1.8 sans validation explicite.** EPIC #127, issues #148, #149, #150.

## CI et contrats automatiques

- [x] Fusion lexicale/vecteur RRF avec priorité aux titres/références exacts, GUID stable et retour lexical seul.
- [x] Filtres des résultats sémantiques réutilisant les critères front matter/projet/date de la recherche historique.
- [x] Métriques de pertinence calculées par requête annotée (P@5, P@10, R@10, p50/p95) et tests déterministes.
- [x] Status local de cache compatible/absent/corrompu et compteurs sans appel de fournisseur.
- [x] Quotas de fragments et de stockage avant écriture, sans destruction des anciennes données.
- [x] Diagnostic de réindexation sans modèle avant le consentement : passages nouveaux/réutilisés/supprimés, quota prévisionnel, interruption testée sur plus de 10 000 fragments.
- [x] Pause/annulation avec commit par document et reprise incrémentale ; tests avec provider simulé.
- [x] Purge confirmée et suppression des vecteurs des GUIDs supprimés à la réindexation explicite.
- [ ] Dernière CI `develop` verte pour **tous** les commits de v1.8 (build, style, smoke, archive reproductible, démarrage portable).
- [x] Banc d'évaluation français annoté (14 documents/14 requêtes) disponible et exécutable en mode lexical, sans adapter.
- [x] Chemin optionnel pour mesurer localement trois modes avec un véritable modèle et consentement CLI explicite.
- [x] Vérification des passages obsolètes au-delà du top 100, avec réhydratation unique par document.
- [ ] Rejouer le corpus en mode réel et les cas de sécurité, suppression, corruption et rollback après derniers changements.

## Recette réelle et représentativité

- [ ] Modèle local Ollama multilingue réel : tester termes techniques, synonymes français et références précises.
- [ ] Mesurer pertinence lexical vs vecteur vs mixte sur un jeu annoté de requêtes **identique** ; documenter les cas manqués.
- [ ] Mesurer Small/Medium/Large, p50/p95 du parcours complet, pic mémoire, taille disque, indexation à froid/chaud.
- [ ] Simuler arrêt modèle, annulation, reprise après interruption, effacement du cache et remplacement du modèle.
- [ ] Vérifier aucune indexation sans double consentement et aucun appel du provider en mode Texte seul.
- [ ] Windows 10/11, DPI 100/125/150 %, navigation clavier, focus, contraste dark mode et fenêtres réduites.
- [ ] Compatibilité des workspaces 1.0–1.7, sauvegarde/rollback, respect des sources Markdown et des exclusions.

## Documentation, sortie et limites

- [x] [Guide de recherche hybride](hybrid-search-1.8.md).
- [x] [Guide de gestion de l'index](semantic-1.8-index-management.md).
- [x] [Méthode de mesures](semantic-1.8-evaluation.md).
- [ ] Notes de version 1.8, release asset Windows et SHA-256, tag annoté et workflow validé après acceptation.
- [ ] Issues #148–#150 fermées par une qualification réelle, EPIC #127 clôturé, puis merge `develop` vers `main`.

Les résultats des fake providers et les benchmarks synthétiques **ne valident pas la pertinence d'un véritable modèle** ; ne pas les utiliser comme preuve de qualité de recherche réelle.
