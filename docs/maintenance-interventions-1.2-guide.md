# Maintenance avancée 1.2 — saisir et modifier une intervention

Fonctionnalité de l'issue [#107](https://github.com/TristanStark/Nodalis/issues/107), développée sur la branche de travail develop.

## Ajouter une intervention

1. Ouvrir le projet contenant **Fiche de Maintenance.md** (notamment un projet Ultra-simple), puis ouvrir la fiche dans l'éditeur Markdown.
2. Cliquer sur **Nouvelle intervention** dans l'en-tête de la fiche, ou choisir **Créer > Nouvelle intervention**, ou lancer la Command Palette (Ctrl+P) et chercher **Nouvelle intervention**.
3. Saisir le résumé. La date du jour est déjà renseignée et peut être modifiée.
4. Déplier **Détails facultatifs** pour renseigner symptômes, cause, diagnostic, action, résultat, statut, catégorie, tags ou rattacher l'intervention à un incident existant.
5. Cliquer sur **Enregistrer**. L'entrée est ajoutée dans la section **Historique de maintenance** du Markdown ; l'éditeur se place sur le nouveau bloc.

Aucune IA, connexion internet ou installation de dépendance n'est nécessaire. L'utilisateur peut toujours saisir ou modifier les interventions directement dans le Markdown, sans ouvrir de formulaire.

## Modifier une intervention

Depuis la fiche, cliquer sur **Modifier** ou utiliser **Créer > Modifier une intervention** ou la commande **Modifier une intervention**. La liste affiche les interventions existantes : choisir l'entrée, modifier ses champs, puis enregistrer. L'identifiant **INT-...** ne change pas, même si le document est déplacé.

Les champs supplémentaires ajoutés manuellement et non reconnus par le formulaire restent conservés. Les paragraphes Markdown libres et autres sections ne sont pas modifiés. Plusieurs interventions peuvent cibler le même incident. Pour enlever un lien, choisir **Aucun incident lié**.

La fermeture d'un formulaire modifié sans validation propose de confirmer l'abandon des changements. Aucun fichier n'est écrit à l'ouverture du formulaire ou lors de l'annulation.

## Enregistrement, récupération et conflits

- Une fiche sans historique structuré reste lisible : la section est créée **uniquement lors du premier ajout**.
- Aucun projet Ultra-simple n'obtient de nouveau fichier ou dossier métier à cette occasion : les deux documents initiaux demeurent **Notes rapides.md** et **Fiche de Maintenance.md**.
- Avant chaque écriture, Nodalis tente de sauvegarder les modifications locales de la fiche déjà ouverte. Un onglet sale ou en conflit interdit l'ajout depuis le formulaire tant que la situation n'est pas résolue.
- Le fichier est relu après fermeture du formulaire et avant la sauvegarde. Les changements externes sur des notes libres sont conservés. Si l'intervention choisie a été changée ou supprimée ailleurs pendant l'édition, l'enregistrement est refusé plutôt que d'écraser cette modification.
- La sauvegarde utilise **TextDocumentSession** et **AtomicFileWriter** : révision de fichier vérifiée, écriture dans un temporaire, remplacement atomique. Une modification concurrente détectée avant ce remplacement déclenche un conflit plutôt qu'une écriture silencieuse.
- Un historique ambigu, des identifiants répétés ou des références orphelines demandent une correction Markdown manuelle avant une insertion automatique.
- Aucune écriture n'est tentée dans un workspace en lecture seule. Si la fiche n'existe pas, le formulaire ne la crée pas implicitement.

## Validation technique

La suite **MaintenanceInterventionSmokeTests** vérifie la création de plusieurs interventions sur un incident, la modification non destructive, les IDs persistants, les notes ajoutées manuellement, les conflits d'édition, la sauvegarde de révisions et l'absence de création automatique d'un document métier. Elle est exécutée avec la suite de régression existante.

Les contrôles interactifs (focus clavier, dark mode, zoom DPI, comportement du calendrier, annulation et collisions avec des éditions simultanées) doivent aussi être validés sur Windows 10/11 avant la publication stable 1.2.

Le format Markdown détaillé est décrit dans [maintenance-markdown-1.2.md](maintenance-markdown-1.2.md).
