# Nodalis v1.8.0 — Recherche hybride et indexation incrémentale

**Windows 10/11 x64 · .NET 10 · installation portable · Markdown/JSON locaux · aucun service distant requis**

## Synthèse

La version 1.8 réunit la recherche classique **exacte/fuzzy** et la recherche **sémantique locale facultative** dans une même liste. Elle ajoute une gestion explicite de l'index, la reprise incrémentale, des quotas et un protocole d'évaluation reproductible. Aucun document n'est envoyé vers un fournisseur sans activation et consentement à l'indexation.

## Nouveautés

### Recherche hybride (#148)
- Fusion déterministe par rang réciproque (RRF) des résultats lexicaux et vectoriels, avec priorité aux correspondances de titres/références exactes.
- Résultats reliés aux GUID et extraits sources, filtrage par périmètre et critères métier ; pas de doublons de documents.
- **Repli vers la recherche lexicale seule** en absence de modèle, d'index compatible, de consentement ou en cas d'erreur du fournisseur. Les scores ne prouvent aucune relation métier.

### Indexation locale et qualité (#149)
- Prévisualisation des fragments nouveaux, réutilisés et supprimés avant confirmation, contrôles de quotas et progression/annulation.
- Réemploi des vecteurs pour les passages identiques du même document, même après déplacement de lignes/sections, avec comparaison SHA-256 ; ni reconstruction intégrale ni transfert automatique au modèle.
- Commit d'index par document et reprise après annulation, purge des documents supprimés sur réindexation explicite.
- Jeu français fictif de **14 documents et 14 requêtes annotées**, mesures P@5, P@10, R@10 et latence p50/p95, mode lexical sans fournisseur, comparaison facultative avec vrai modèle local.

### Confidentialité et gestion du cache (#150)
- Diagnostic de l'état du cache (compatible, manquant, invalide), quotas d'espace/fragments et effacement confirmé.
- Index dérivé stocké en local, sans modification des fichiers Markdown et sans indexation au démarrage.
- Tests de processus enfant JSON (fournisseur local simulé) : malformation, timeout, annulation, modèle incompatible, sortie tronquée et erreurs.

## Utilisation rapide

1. Ouvrir **Ctrl+P → Configurer la recherche sémantique locale** ; installer/configurer volontairement un modèle d'embeddings local compatible pour bénéficier du mode mixte.
2. Ouvrir **Ctrl+P → Rechercher par le sens (local)**, choisir le périmètre, consulter le diagnostic d'indexation, puis confirmer explicitement l'indexation.
3. Rechercher avec le mode hybride, appliquer les filtres et ouvrir les sources Markdown. En l'absence de modèle, le moteur lexical reste utilisable.
4. Utiliser les commandes d'état du cache, de reprise ou de purge si nécessaire. Consulter le guide pour les critères d'invalidation.

## Documentation

- [Guide du moteur hybride](hybrid-search-1.8.md)
- [Gestion du cache, quotas et confidentialité](semantic-1.8-index-management.md)
- [Méthode de benchmark français et restrictions](semantic-1.8-evaluation.md)
- [Checklist de qualification](release-checklist-1.8.md)
- [Guide du fournisseur local de la 1.7](semantic-1.7-provider.md)

## Compatibilité, précautions et limites

- Workspaces Markdown/JSON existants conservés ; pas de migration imposée. Le cache vectoriel peut être effacé/recréé sans toucher aux notes.
- Le fournisseur de la 1.7 reste **facultatif et local**. La recherche lexicale historique reste disponible.
- Les scores vectoriels dépendent du modèle : ils représentent un rapprochement statistique, jamais une probabilité de vérité.
- La CI valide les tests déterministes, le packaging et le démarrage automatisé. **Les mesures sur un Ollama réel, la recette utilisateur sur plusieurs DPI/postes Windows, ainsi que les performances Small/Medium/Large réelles ne sont pas certifiées par la CI** et doivent être réalisées sur la machine de destination.
- Aucun parseur XLSX/VSDX n'est inclus.

## Mise à jour et retour arrière

Télécharger l'archive **Nodalis-win-x64.zip** et vérifier son empreinte dans **SHA256SUMS.txt**. Sauvegarder le workspace, puis remplacer le répertoire de l'application. Conserver le bridge d'embeddings pour les usages locaux facultatifs. Pour revenir à 1.7, remettre les binaires précédents : les notes demeurent inchangées, mais l'interface hybride et ses diagnostics récents disparaissent.

**Suivi** : EPIC #127 ; issues #148, #149 et #150. Prochaine étape : [EPIC DOCX 1.9 #154](https://github.com/TristanStark/Nodalis/issues/154).
