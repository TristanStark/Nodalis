# Bibliothèque originale FRT-100 pour Nodalis 2.0

Source utilisateur intégrale, non réécrite : `Prompts/collections/Prompts_Gestion_Projet_FRT100.md`.
Référentiel système intégral (FRT-100 v1.0, référentiel interne non officiel) : `Prompts/frt100-system-v1.md`.

Les 30 fichiers `frt100-01.md` à `frt100-30.md` et `frt100-combined.md`
sont extraits automatiquement de la collection originale. Seuls les séparateurs
Markdown inter-prompts ont été omis ; le titre et les consignes utilisateur sont
conservés à l'identique. Ils restent indépendants du prompt système.

Une IA externe ne respecte FRT-100 que si l'utilisateur configure effectivement
le prompt système dans l'application externe choisie. Nodalis n'exécute rien.

**Prochaine intégration (EPIC #196)** : un sélecteur WPF, les espaces
global/workspace/projet, les variables et sources choisies manuellement,
l'aperçu de l'assemblage, l'historique et l'export ; **EPIC #197** séparée
pour les réponses structurées avec validation humaine. Pas de réseau/IA requis.

## Bibliothèque retrouvée et installée sur develop

Le fichier original `Prompts_Gestion_Projet_FRT100.md` et le
référentiel original `FRT-100_Francais_Rationalise_Technique_v1.0.md`
ont été retrouvés dans les fichiers de l'utilisateur. Les 30 consignes
numérotées et le prompt combiné sont intégrés dans
`src/Nodalis.App/Prompts/frt100-01.md` à `frt100-30.md` et
`frt100-combined.md`. Le référentiel SYSTEM distinct est
`frt100-system-v1.md`. Aucun prompt n'a été inventé.

Le `FileSystemAiPromptStore.ListAsync` recense les prompts utilisateur
disponibles sans charger tous les corps. Le préprocesseur prépare les
deux canaux séparément, et le modèle `ProjectJourneyPromptDraft`
porte désormais `SystemPrompt` facultatif. Le statut de configuration
réelle du système FRT-100 dans un LLM externe reste inconnu.

La régression `ProjectFrt100PromptSmokeTests` compare les 31
extractions à la collection originale et vérifie la non-concaténation.
L'interface de sélection et de copie distincte reste à brancher dans le
Parcours. Aucun appel IA ni écriture de données métier.
