# Créer un incident de maintenance

Ouvrir la **Fiche de Maintenance.md** du projet, puis **Nouvel incident** dans sa barre d'actions, **Créer > Nouvel incident**, ou Ctrl+P > **Nouvel incident**.

Saisir la date et un résumé sur une seule ligne. Les champs symptômes, cause, diagnostic, statut, catégorie et tags sont facultatifs. L'entrée est ajoutée directement au Markdown existant sous **Historique de maintenance**, avec un identifiant `INC-…` durable. Aucune IA ni nouveau fichier métier Ultra-simple.

Pour lier une intervention à cet incident, sélectionner **Nouvelle intervention** puis son ID dans **Incident lié**. Plusieurs interventions peuvent être liées au même incident. L'historique permet de revenir à la source.

Le formulaire ne modifie rien avant validation : il utilise le même autosave et les protections contre les modifications concurrentes que les interventions. Les entrées restent également éditables à la main dans le Markdown.
