# Nodalis v1.9.0 — Import DOCX avancé, performances et confort d'édition

**Version stable · Windows 10/11 x64 · application portable autonome (.NET 10) · données Markdown/JSON locales**

## En bref

Nodalis **v1.9.0** facilite le découpage des documents Word volumineux en notes structurées, renforce les protections de réimport et améliore la réactivité de l'application avec de nombreux documents et onglets ouverts. L'éditeur Markdown sombre bénéficie de repères visuels et d'une coloration syntaxique désactivable.

La recette fonctionnelle et visuelle de la version a été confirmée par l'utilisateur sur Windows. La chaîne de publication reconstruit le binaire portable et relance les contrôles automatisés.

## Import DOCX avancé

- **Reconnaissance de structure Word :** analyse des titres et de leur hiérarchie, correction des niveaux et sélection des sous-sections, paragraphes, tableaux et images utiles.
- **Découpage supervisé :** choix des destinations (Technique, Tests, Recette, documents ou sections personnalisés), conservation de l'ordre, possibilité d'ignorer des contenus. Rien n'est écrit dans le workspace sans confirmation.
- **Profils de mapping réutilisables :** règles explicites et reproductibles avec priorité aux arbitrages manuels.
- **Prévisualisation et plan exact :** comparaison du Word et du Markdown attendu, vérification des destinations, collisions et changements du document entre l'aperçu et la validation.
- **Tableaux et médias :** extraction locale et diagnostics en cas de conversion imparfaite ; ressources externes non téléchargées.
- **Traçabilité et réimport :** copie de la source, manifeste, historique et snapshots ; comparaison des changements côté Word et côté Nodalis, détection de conflits et résolution explicite avant écriture.
- **Sécurité transactionnelle :** opérations locales journalisées, sauvegardes et reprise/rollback conservateurs après échec ou interruption. Le DOCX original n'est jamais modifié.

Les protections ZIP/Open XML (fichiers malformés, références externes, limites, traversées de chemins) et les tests de récupération en cas d'interruption sont inclus dans les contrôles de régression.

## Éditeur Markdown et navigation

- **Coloration syntaxique à la source :** titres, liens, mise en forme, code, citations, tâches et métadonnées différenciés dans les deux panneaux.
- **Interrupteur « Couleurs » :** retour immédiat au rendu natif WPF ; préférence mémorisée.
- **Repères structurels :** identification visuelle discrète des titres, cases ouvertes ou terminées, priorités et blocs de contenu.
- **Documents volumineux :** mise en couleur limitée à la zone visible ; repli vers le texte natif lorsque les contraintes de rendu ou d'alignement ne sont pas satisfaites. Aucun changement dans le format Markdown.

## Performances et stabilité WPF

- Améliorations de la gestion des onglets, de la fermeture en lot (« clore ceux à droite »), de la réactivité de l'explorateur et du retour de veille.
- Réduction des recalculs inutiles lors des actualisations, index et parcours liés aux documents ; meilleure coordination entre rafraîchissements, autosave et panneaux.
- Annulation des résultats périmés et contrôles de fraîcheur lors des mises à jour asynchrones.
- Instruments de diagnostic de latence et scénarios de vérification pour l'interface, les vues, la recherche, les tâches, les jalons et les workspaces volumineux.

Les tests CI contrôlent les invariants, le build, la suite de régression et le démarrage du binaire publié. Les performances réelles restent dépendantes de la machine, du volume et du contexte Windows.

## Documentation et recette

- [Guide d'import/export](import-export.md)
- [Détection des titres Word](docx-1.9-heading-detection.md)
- [Arbre documentaire et destinations](docx-1.9-import-tree.md)
- [Mapping des sections](docx-1.9-mapping.md) et [profils réutilisables](docx-1.9-mapping-profiles.md)
- [Comparaison et prévisualisation](docx-1.9-preview-comparator.md)
- [Traçabilité des imports](docx-1.9-provenance.md)
- [Comparaison à trois voies](docx-1.9-reimport-diff.md) et [sécurité transactionnelle](docx-1.9-reimport-transaction.md)
- [Éditeur Markdown coloré](markdown-editor-syntax.md)
- [Budgets et mesures de performance](performance-budgets.md)
- [Grille de qualification 1.9](release-checklist-1.9.md) et [recette DOCX](docx-1.9-manual-qualification.md)

Un workspace de démonstration fournit des documents et des scénarios de qualification directement consultables dans Nodalis.

## Installation, mise à jour et retour arrière

1. Sauvegarder le workspace avant toute mise à jour, particulièrement avant de tester les réimports.
2. Télécharger **Nodalis-win-x64.zip** depuis les fichiers joints à la GitHub Release.
3. Comparer son empreinte SHA-256 avec **SHA256SUMS.txt**, puis extraire l'archive dans un dossier d'application.
4. Fermer Nodalis et remplacer uniquement les fichiers du programme : les données du workspace sont stockées séparément.
5. Ouvrir Nodalis et vérifier un document existant, l'explorateur, la recherche et un import de test.

Pour revenir à une version précédente, restaurer les binaires précédents et conserver la sauvegarde du workspace. Une ancienne version peut ne pas comprendre certains nouveaux états de traçabilité ou de réimport ; utiliser une copie de travail pour toute opération destructive.

## Compatibilité, confidentialité et limites

- **Pas d'IA requise** pour les imports DOCX et aucune connexion ou service distant pour ces fonctionnalités.
- **Aucune nouvelle dépendance NuGet**, les formats de notes Markdown/JSON restent consultables hors de Nodalis.
- Le mapping et l'interprétation des documents Word mal structurés restent des opérations à vérifier humainement avant validation.
- **Import XLSX et VSDX non livré en v1.9.0** ; ces formats restent des pistes futures.
- La coloration Markdown est un confort visuel, pas un parseur exhaustif de tous les langages contenus dans des blocs de code.

**Suivi :** [EPIC import DOCX #154](https://github.com/TristanStark/Nodalis/issues/154), [EPIC performances #180](https://github.com/TristanStark/Nodalis/issues/180) et [gate de qualification #171](https://github.com/TristanStark/Nodalis/issues/171).
