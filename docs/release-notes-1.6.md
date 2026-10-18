# Nodalis v1.6.0 — Documents composites et rapports vivants

**Windows 10/11 x64 · .NET 10 · exécutable portable autonome · données locales**

## Nouveauté principale

Nodalis permet maintenant de **composer des rapports et des notes à partir d'autres fichiers Markdown** sans en dupliquer le contenu. Les sources demeurent indépendantes et l'inclusion est calculée à l'affichage.

```md
# Présentation du projet
![[550e8400-e29b-41d4-a716-446655440000#Tests]]
```

Les inclusions peuvent viser un document complet, ou une section avec ses sous-sections ; les identités GUID résistent aux renommages et déplacements réindexés.

## Fonctionnalités

- **Insertion depuis Ctrl+P** : choisir un document du workspace puis une de ses sections (ou la totalité) ; les références enregistrées restent de simples lignes Markdown.
- **Aperçu vivant** : expansion récursive en lecture seule dans le rendu WPF, source identifiée, lien de retour à la section, actualisation lors des changements externes ; les sources ne sont jamais réécrites.
- **Robustesse** : cycle A→B→A détecté, limites 8 niveaux / 256 fragments / 2 millions de caractères, annulation et diagnostics explicites pour références manquantes ou ambiguës. Aucune inclusion hors workspace ni accès distant.
- **Exports et impression hors ligne** : Markdown avec références portables ou instantané résolu, HTML autonome, document Word DOCX minimal sans package tiers, impression Windows. Un export refuse d'écraser les documents sources indexés.
- **Suivi local** : index d'inclusions temporaire distinct des relations métier d'analyse d'impact, reconstruction et invalidation sur changement externe.
- **Compatibilité** : les workspaces Markdown/JSON 1.0–1.5 restent utilisables, sans ajout de fichiers obligatoires dans les projets ultra-simples.

## Comment utiliser

Dans un document ouvert, utiliser **Ctrl+P → Insérer un document ou une section**, sélectionner la source puis son titre. Activer l'aperçu Markdown pour lire le contenu virtuel et ouvrir la source grâce au lien de provenance. **Ctrl+P → Exporter le document composite** propose les quatre formats ; **Imprimer le document composite** ouvre l'impression Windows.

Détails : [Guide utilisateur et technique](composite-documents-1.6.md) · [Qualification et rollback](release-checklist-1.6.md).

## Limitations connues

- Inclusion uniquement sur une ligne Markdown entière ; une ancre de section dupliquée demande un renommage explicite.
- HTML et DOCX assurent le texte, les titres, les tableaux simples et la provenance textuelle, **pas** la pleine reproduction des illustrations locales, des schémas Mermaid ou de toute la typographie du rendu WPF. Consulter le Markdown dans Nodalis pour les blocs visuels avancés.
- Les limites de profondeur et de taille sont volontaires pour protéger la réactivité.
- Les contrôles automatiques Windows (build, fixtures, archives, démarrage) ne remplacent pas un contrôle visuel sur chaque combinaison OS, DPI, écran et imprimante.

## Installation, sauvegarde, retour en arrière

Télécharger `Nodalis-win-x64.zip`, vérifier `SHA256SUMS.txt` puis remplacer les fichiers de l'application comme pour les versions précédentes, sans modifier le workspace. Sauvegarder le workspace avant mise à jour. En revenant à la 1.5, les marqueurs `![[...]]` restent lisibles mais ne sont plus développés dans l'aperçu.

Issues : [EPIC #126](https://github.com/TristanStark/Nodalis/issues/126), #135–#141.
