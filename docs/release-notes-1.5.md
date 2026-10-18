# Nodalis v1.5.0 — Vues dynamiques et navigation transversale

**Release stable du 18 octobre 2026 — Windows 10/11 x64 — .NET 10 — portable autonome**

La version 1.5 introduit les **vues dynamiques** sur le workspace unique. Elle permet de rechercher, filtrer et afficher ensemble les informations déjà présentes dans les projets, sans dupliquer une tâche, un jalon ou une note, sans serveur ni IA.

## Nouveautés

- **Vue transversale** des documents, tâches, jalons, décisions, incidents et interventions ; références GUID vers les sources Markdown.
- **Filtres typés et déterministes** : type, titre, statut, priorité, dates, tags, scope ; ET/OU imbriqués, tri stable, regroupement.
- **Trois présentations** sur les mêmes résultats : liste, tableau et cartes ; pagination et plafonds explicites.
- **Définitions sauvegardées** : JSON portable `.nodalis-views.json` ou vues privées dans `%LOCALAPPDATA%`. GUID stables, duplication, suppression non destructive, détection des conflits et fichier future-version en lecture seule.
- **Navigation intégrée** : épingles et « Mes vues » depuis l'accueil et le worktree, ouverture directe par la Command Palette (Ctrl+P), création et duplication de vues.
- **Actualisation** : projection réutilisée en mémoire, changements Markdown détectés, rafraîchissement regroupé, requêtes annulables hors du fil WPF ; réutilisation des tâches de documents non modifiés grâce aux empreintes du registre.
- **Sécurité et validation** : aucun résultat indexé ne devient une nouvelle source canonique, aucune réécriture de notes lors de la consultation ; diagnostics pour références manquantes.
- **Tests** : scénarios de 12 000 éléments sur 200 projets, pagination, suppression, champs absents, définition corrompue/restaurée et 50 annulations successives, CI Windows et packaging reproductible.

## Utilisation

Depuis **Ctrl+P → Vues dynamiques (aperçu 1.5)**, choisir une catégorie, saisir un filtre, sélectionner le tri et le regroupement. Enregistrer la définition dans le workspace ou localement ; activer **Épingler** pour l'afficher sur l'accueil. Double-cliquer une ligne pour revenir au document source exact. Les blocs sources restent modifiables uniquement dans leurs éditeurs habituels.

Guide : [Utiliser les vues dynamiques](views-user-guide-1.5.md) · [Modèle/limites](views-core-1.5.md) · [Stockage JSON](views-storage-1.5.md) · [Checklist de sortie](release-checklist-1.5.md).

## Compatibilité et sauvegarde

Le schéma du manifeste workspace reste inchangé. Les anciens dossiers de projet et leurs notes Markdown s'ouvrent sans migration destructive. Les vues partagées sont stockées dans un JSON additionnel optionnel à inclure dans les backups. Les vues privées restent sur la machine de l'utilisateur et demandent une sauvegarde séparée. Le retour à la version 1.4 conserve les fichiers Markdown ; la 1.4 ignore les définitions de vues 1.5.

## Limites connues

- L'éditeur de filtres graphiques présente surtout les conditions simples ; les groupes avancés sont compris par le moteur et les définitions JSON, mais pas encore entièrement éditables visuellement.
- Les changements externes peuvent provoquer une collecte complète des jalons, décisions et historiques de maintenance ; l'incrémentalité granulaire est pour l'instant prioritaire sur les tâches et l'index des liens.
- Ce n'est ni une base de données distincte ni un tableur. Aucune synchronisation réseau ou IA locale n'est requise.

## Livraison

Le workflow `Portable Windows Release` valide les contraintes, les tests et l'exécutable portable, crée le **tag annoté v1.5.0**, puis publie **Nodalis-win-x64.zip** et **SHA256SUMS.txt**.

Issues : [EPIC #125](https://github.com/TristanStark/Nodalis/issues/125), [#128](https://github.com/TristanStark/Nodalis/issues/128) à [#134](https://github.com/TristanStark/Nodalis/issues/134).
