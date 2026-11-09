# Suspension urgente : import et conversion DOCX

**Décision appliquée : 9 novembre 2026.** L'importation de documents Word DOCX et leur conversion vers Markdown dans Nodalis sont désactivées, sans préférence permettant de les réactiver.

## Périmètre de la suspension

- Le menu **Données > Importer DOCX**, la commande de palette correspondante et l'assistant d'import ne sont plus accessibles.
- Le parseur, la conversion DOCX vers Markdown, la préparation/validation d'import et le réimport sont refusés par une politique *fail closed*, y compris par appel direct des services, avant lecture du fichier ou écriture.
- Le mécanisme de **récupération d'anciens imports interrompus** reste disponible pour protéger les fichiers existants.
- L'import Markdown, les projets existants, ainsi que l'export DOCX (fonctionnalité distincte) ne sont pas modifiés par ce correctif.

## Déploiement et conformité

Cette mesure ne peut pas désactiver à distance les exécutables déjà installés. **Ne plus utiliser l'import DOCX des anciennes versions** ; mettre à jour les postes concernés avec une compilation intégrant le correctif avant toute nouvelle utilisation. Les données déjà importées ne sont ni purgées ni effacées automatiquement. Tout traitement de documents d'entreprise doit suivre la politique interne applicable.

## Vérification

Les anciens scénarios de tests d'import/réimport DOCX (qui nécessitaient la fonctionnalité active) sont retirés de la séquence principale de smoke tests. Un nouveau test de blocage vérifie les points d'entrée de lecture, copie temporaire, plan, validation, conversion, import structuré et transaction de réimport, sans ouvrir de DOCX. Les tests Markdown continuent d'être exécutés.
