# Clôture et archivage d'un projet

Depuis l'explorateur, clic droit sur un **projet actif** > **Clore et archiver**. La même action est disponible dans Ctrl+P (« Clore et archiver le projet »). Après confirmation, Nodalis déplace l'intégralité du projet et ses sous-projets sous **Archives**, visible à la racine du workspace. Les données Markdown, JSON, les identifiants GUID et les pièces internes restent en place dans le dossier déplacé ; ce n'est **pas la Corbeille**.

Les projets clos sont consultables dans l'arbre Archives. Pour reprendre le projet, clic droit sur sa racine dans Archives > **Réouvrir le projet**, ou utilisez Ctrl+P. Nodalis tente de rétablir son ancien chemin, sans renommer ou remplacer un projet existant. Il refuse si le dossier parent d'origine manque ou si la destination est occupée.

L'archivage et la réouverture nécessitent de fermer **tous les onglets du projet**, sous-projets compris. Les projets déjà existants restent inchangés ; seuls les projets archivés obtiennent dans `.project.json` les propriétés facultatives `archivedUtc` et `archiveOriginalRelativePath`.

**Limite :** les liens internes basés sur les GUID sont réindexés après déplacement, mais les liens externes absolus et les chemins relatifs saisis manuellement dans du Markdown peuvent demander une correction. Après archivage/restauration, vérifier ces liens. Un projet archivé n'est pas verrouillé en lecture seule ; réouvrir le projet pour reprendre une activité normale.
