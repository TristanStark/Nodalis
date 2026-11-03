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

## Interface de sélection et copie séparée

Dans `Parcours` → opération → `Associer un prompt…`, un nouveau
sélecteur WPF filtre les prompts locaux par référence et titre et affiche
leur corps source original en lecture seule. `Sans prompt` retire
uniquement l'association à l'opération après la confirmation métier
existante. La sélection ne crée aucun document ni requête réseau.

`Préparer / copier` présente deux onglets distincts : le prompt
**UTILISATEUR** contextualisé et, pour les clés FRT-100, le référentiel
**SYSTEME**. Deux boutons de copie distincts évitent d'affirmer que
l'outil externe a déjà chargé FRT-100. Aucune configuration distante,
exécution IA ou injection dans le workspace n'a lieu.

**À poursuivre** : choix guidé des documents/sections par variable,
catalogues workspace/projet et conservation de provenance des versions ;
import de réponses structurées dans #197. Qualification visuelle réelle
Windows encore nécessaire pour #195/#196.

## Variables non résolues et brouillon éditable

La préparation énumère les variables `<DOCUMENT>`, `<SOURCE>`,
`<CONTEXTE>` et les autres marqueurs réellement présents dans le modèle,
sans les remplacer au hasard par un document du workspace. Le nombre et la
liste apparaissent dans les avertissements. Le brouillon **utilisateur**
est désormais modifiable uniquement en mémoire : la copie demande une
confirmation supplémentaire si des variables restent. Le prompt original
sur disque reste non modifié ; le prompt SYSTEME reste en lecture seule.
Le service ne déduit aucune association implicite entre un fichier lié
et une variable. La liaison par variable/section reste au backlog #196.

## Suggestions d'étapes sans activation implicite

Les modèles intégrés pour **nouveaux** Parcours Simple, Moyen et
Complexe proposent maintenant une clé de prompt FRT-100 authentique par
opération (par ex. EXI/extraction #1, critères #7, risques #15 et #14,
conception #11, préparation #17, traçabilité tests #10, MEP #18 et
clôture #30). L'association est seulement une proposition modifiable,
**aucune exécution ni approbation** automatique. Les Parcours
déjà initialisés et les modèles workspace personnalisés ne sont pas
réécrits. Les smoke tests vérifient que toutes les clés des neuf
étapes du modèle Complexe pointent vers un des 31 prompts réels.
