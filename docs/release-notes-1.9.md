# Nodalis 1.9 — Notes de version candidates (non publiées)

La v1.9 améliore l'import de documents Word longs et structurés **sans IA**.

## Nouveautés en qualification

- Arbre des titres Word et correction des niveaux détectés, sélection fine d'éléments.
- Mapping de sections vers Technique, Tests, Recette ou des sections personnalisées ; exclusion et réorganisation ordonnée.
- Profils de mapping réutilisables, avec priorité aux décisions manuelles.
- Comparateur Word/Markdown et plan d'écriture avant confirmation ; empreinte pour détecter une modification.
- Tableaux/images : conversion avec diagnostics explicites des structures non fidèles ; refus de téléchargements externes.
- Provenance locale : copie Word, manifestes et snapshots de réimport versionnés.
- Premier import journalisé : sauvegarde du manifeste préexistant, validation SHA-256 des sorties et récupération conservatrice après interruption (à qualifier en conditions Windows réelles).
- Réimport comparé à trois voies avec conflits à résoudre, sauvegardes, journal et restauration.
- Dossiers projet Ultra-simple et fichiers singleton correctement annoncés dans le plan ; destinations Markdown et DOCX figées après approbation.

## Sécurité et limitations

Aucune dépendance NuGet supplémentaire, aucun modèle local ou service réseau requis. Le DOCX original n'est jamais modifié. Les objets Word non représentables sont signalés plutôt qu'importés silencieusement.

**Cette note n'est pas une annonce de release.** La qualification sous arrêt brutal réel du premier import (#165), certains parcours P1 (#166 à #170) et la qualification Windows manuelle (#171) restent des conditions ou décisions de sortie. L'import XLSX et VSDX n'est **pas disponible en 1.9**.

Contrôles : `docs/release-checklist-1.9.md` et `docs/docx-1.9-manual-qualification.md`.
