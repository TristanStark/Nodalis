# Nodalis 1.8 (develop) — Gérer l'index de recherche

**Cette fonctionnalité est en qualification sur `develop`.** Elle n'est pas annoncée dans la release stable v1.7.0.

## Accès et consultation

Ouvrir **Ctrl+P → Rechercher par le sens (local)**, puis la section repliable **État et gestion de l'index local** et cliquer sur **Afficher l'état**. Cette lecture n'ouvre aucun document Markdown et ne lance aucun modèle, serveur ou processus d'embeddings. Elle affiche :

- fichier dérivé présent, absent, incompatible ou corrompu ;
- modèle et dimensions configurés, date de la dernière écriture ;
- nombre de documents et de fragments, couverture Global/Application/Projet (déduite des chemins canoniques) et couverture du dossier sélectionné ;
- taille disque, estimation des seules valeurs flottantes en RAM (pas une mesure du pic réel de l'application) ;
- emplacement du cache sous `%LOCALAPPDATA%/Nodalis/SemanticIndexes`, échantillon de chemins indexés et nombre de motifs exclus.

La limite dure actuelle est de **10 000 fragments** ou **64 Mo** de JSON dérivé par workspace. Les passages source ont déjà une limite distincte (8 Mo par document). Une cache incompatible ne doit jamais être présentée comme valide : utiliser une nouvelle indexation après vérification du modèle/exclusions.

## Prévisualiser les modifications sans modèle

Le clic sur **Indexer / reprendre ce périmètre** lance d'abord un **diagnostic local annulable**, sans consentement d'envoi, sans démarrer de fournisseur et sans modifier les fichiers. Il compare les fragments Markdown canoniques sélectionnés aux vecteurs déjà présents : nouveaux passages à calculer, passages inchangés réutilisables, fragments obsolètes, fichiers manquants et vecteurs orphelins. Il affiche le nombre futur de fragments (quota 10 000) et une **estimation en Ko des seuls vecteurs numériques** ; cela n'est ni la taille exacte du JSON disque, ni la RAM du modèle Ollama.

En cas de dépassement prévisible du quota de fragments, le bouton refuse l'indexation **avant** tout appel au modèle. En cas de taille JSON excessive, le contrôle d'écriture atomique reste la seconde barrière. Pour les autres périmètres, un second écran demande l'**autorisation explicite** de transmettre les passages nouveaux au processus d'embeddings local : refuser conserve l'index intact. Les coûts calculés sont indicatifs si des documents changent après le diagnostic.

## Indexer, reprendre, arrêter

Le sélecteur **Global / Application / Projet** propose les dossiers existants à partir du catalogue local, sans lancer de recherche ni de modèle. Le champ de dossier relatif reste éditable pour un périmètre plus fin. Le bouton **Indexer / reprendre ce périmètre** demande une confirmation explicite avant de transmettre des passages au modèle local choisi. Le dossier relatif dans le champ *Périmètre* sélectionne uniquement les fichiers à traiter. Les fragments déjà inchangés sont réutilisés et ne sont pas renvoyés au modèle.

**Pause / Annuler** interrompt l'opération. Seuls les documents intégralement validés avant l'arrêt sont conservés ; lors d'une reprise volontaire avec le même périmètre, le modèle ne recalcule que les fragments absents ou modifiés. L'indexation n'a pas lieu en arrière-plan.

Pendant une indexation approuvée, Nodalis compare aussi le catalogue complet des documents éligibles aux GUIDs de l'index, puis supprime les vecteurs orphelins de documents réellement supprimés. Cette réconciliation ne porte **pas** sur les seules sources du dossier sélectionné : elle préserve les vecteurs des documents encore connus hors du périmètre choisi. Toute modification du modèle, des dimensions ou des exclusions invalide et efface le cache dérivé conformément au réglage de confidentialité.

Un quota dépassé bloque la création des nouveaux vecteurs et laisse intactes les données sources et les documents déjà indexés. Réduire le périmètre ou purger explicitement l'index.

## Purger et confidentialité

**Purger les vecteurs…** affiche une confirmation : la purge efface le fichier d'index du workspace courant, jamais les fichiers `.md`, images, projets ni sauvegardes. La commande existante de palette **Effacer le cache sémantique** reste une alternative. Désactiver la recherche sémantique supprime aussi l'index dérivé.

En mode **Texte seul**, le fournisseur d'embeddings n'est pas invoqué et la recherche d'origine reste disponible, même si le cache est absent ou corrompu. Un cache valide n'implique **pas** que ses résultats constituent des conclusions fiables.

## Validation avant publication

Les tests de régression couvrent le modèle d'index absent/incompatible/corrompu, les compteurs, les quotas, l'annulation/reprise et la purge. Restent à qualifier manuellement sur Windows : lisibilité des contrôles en dark mode, DPI 100/125/150 %, fenêtre réduite, clavier/focus, index volumineux et moteur Ollama réel. Voir [checklist 1.8](release-checklist-1.8.md).
