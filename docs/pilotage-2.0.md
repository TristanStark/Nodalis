# Pilotage 2.0 — accès aux parcours, prompts et outils projet

## Objectif

Toutes les commandes de pilotage de Nodalis 2.0 sont regroupées dans
**Pilotage**, en haut à droite de la fenêtre principale. Elles réutilisent
les vues et services déjà en place. Le menu ne crée aucun document,
parcours, prompt ou incident simplement parce qu'il est ouvert.

## Où trouver chaque fonction

| Menu Pilotage | Accès et portée |
| --- | --- |
| **Parcours projet · 2.0 → Ouvrir le parcours du projet** | Onglet dédié avec étapes, opérations, jalons, gates et sources |
| **Parcours projet · 2.0 → Toutes les étapes du projet** | Liste dynamique des étapes *réelles du projet courant*, y compris étapes renommées, réordonnées ou personnalisées. Développer une étape puis une opération pour l'afficher, préparer son prompt ou rejoindre son association de prompt |
| **Parcours projet · 2.0 → Modèles de parcours** | Éditeur des modèles de parcours du workspace ; aucune modification des parcours existants sans confirmation |
| **Prompts et réponses IA → Préparer / copier les prompts du parcours** | Étape → opération → préparation locale du brouillon, choix éventuel des liaisons de variables, revue, copie séparée des prompts système et utilisateur |
| **Prompts et réponses IA → Associer un prompt à une opération** | Étape → opération ; sélection de l'opération réelle puis **ouverture automatique du sélecteur humain de prompts**, sans clic supplémentaire ni association automatique |
| **Prompts et réponses IA → Analyser / importer une réponse IA externe** | Prévisualisation locale, comparaison, décisions humaines ; aucune IA locale obligatoire |
| **Prompts et réponses IA → Prompt de mise à jour depuis une réunion** | Workflow antérieur de synthèse de réunion / préparation de prompt, distinct des prompts FRT-100 des opérations |
| **Exigences, critères et tests · 2.0** | Nouvelle exigence EXI, traçabilité EXI ↔ CA ↔ TEST, campagnes et passages REC ; **accès direct** à la fenêtre des anomalies REC, incidents et revalidation humaine ; GUID des blocs |
| **Risques, décisions et Go/No-Go · 2.0** | Registre RIS, registre des gates / réserves / Go-No-Go et décisions du projet |
| **Préparation / migration · 2.0** | Prévisualisation, migration explicite et restauration de la structure Complexe |
| **Maintenance** | Historique, nouvel incident, nouvelle intervention |
| **Pilotage général** | Tâches, jalons, calendrier, Kanban, health check et couverture / cohérence |

Les étapes détaillées de **préparation, de validation, de mise en
production et de clôture** sont celles du **parcours propre au projet**,
pas une liste fixe construite dans le menu. Les modèles Simple, Moyen et
Complexe, et toutes les personnalisations, restent donc synchronisés
avec la liste dynamique.

## Règles d'utilisation

1. Sélectionner d'abord un projet ou un document **de ce projet** dans
   l'explorateur. Lorsque l'**onglet Parcours est affiché**, ce dernier
   est le contexte prioritaire (il l'emporte sur une sélection
   précédente dans l'explorateur). Les commandes générales restent
   disponibles sans sélection de projet ; les vues liées à une campagne
   ou un parcours demandent un projet non ambigu.
2. Ouvrir **Pilotage → Parcours projet → Toutes les étapes du projet**.
   Les noms et opérations sont relus **à chaque ouverture** depuis
   `.project-journey.json`, hors du thread UI. Si le parcours n'est pas
   initialisé, une entrée **« Ouvrir / initialiser le parcours… »** est
   proposée : son activation **ne lance pas** l'initialisation sans
   le choix et la confirmation de l'utilisateur. Pour les modèles
   applicables, les étapes et opérations proposées peuvent être
   consultées dans le menu **avant création**, clairement marquées
   comme aperçu et sans commande d'édition.
3. Pour copier un prompt, ouvrir **Pilotage → Prompts… → Préparer / copier
   les prompts du parcours**, choisir l'étape puis l'opération. Un
   brouillon est calculé à partir des sources approuvées et des variables.
   Il est **relisible et éditable avant copie**. La copie du prompt
   utilisateur est indépendante de la copie du système FRT-100, et aucun
   modèle d'IA n'est exécuté automatiquement.
4. Pour changer une association de prompt, choisir l'opération via
   **Associer un prompt à une opération**. Nodalis sélectionne cette
   opération dans l'onglet Parcours et **ouvre directement** son
   sélecteur de prompts, même si l'onglet était encore en chargement.
   Le changement reste soumis à la confirmation explicite déjà en
   place et au contrôle de révision. Annuler ne modifie aucun fichier.
5. Les options liées à l'écriture sont indisponibles pour un
   workspace en lecture seule ou un projet archivé. Le menu préserve
   les possibilités de consultation des parcours et des brouillons.
6. Si le projet, l'étape ou l'opération change entre l'ouverture du
   sous-menu et le clic, Nodalis refuse de réutiliser un GUID ou un
   chemin périmé. Le menu doit être rouvert.

**Périmètre** : les entrées ouvrent les fonctionnalités
**actuellement implémentées** ; une étape de parcours sans automate
spécialisé reste accessible dans l'onglet Parcours. Le menu ne simule
pas une mise en production ni une clôture automatiquement effectuée.

## Recette Windows manuelle

- Ouvrir le menu avec un projet **Complexe** initialisé et vérifier
  que **chacune des 9 étapes** et chaque opération du parcours apparaît,
  sans élément oublié, même si une opération n'a aucun prompt associé.
  Les tests smoke doivent contrôler la projection métier correspondante.
- Renommer, déplacer et ajouter une étape/opération depuis le Parcours,
  rouvrir le menu : le nouveau libellé, l'ordre et les liens apparaissent
  immédiatement. Vérifier qu'un ancien élément devenu invalide est refusé.
- Naviguer entre **deux projets différents** pendant un chargement
  lent : aucune action sur un autre projet ne doit être possible.
  Si l'onglet Parcours du projet A est visible mais que l'explorateur
  conserve une sélection du projet B, **Pilotage doit afficher A** ;
  en quittant ce parcours, le contexte redevient la sélection de
  l'explorateur.
- Ouvrir la liste des étapes sans parcours initialisé : aucune
  écriture, option d'ouverture / initialisation visible.
- Préparer/copier un prompt **avec** et **sans** variables, puis
  avec un prompt système distinct, en vérifiant les avertissements de
  sources et l'absence d'envoi automatique. Depuis **Associer un
  prompt à une opération**, le sélecteur doit apparaître directement
  sur la bonne opération, sans enregistrer si l'utilisateur annule.
- Dans l'onglet Parcours, vérifier que la sélection pointe vers
  **la bonne étape et opération**, y compris quand l'onglet était
  déjà ouvert.
- Ouvrir REC, anomalies, traçabilité EXI, RIS et Go/No-Go depuis
  Pilotage ; confirmer la même logique métier et les mêmes restrictions
  que via la palette ou l'onglet Parcours.
- Vérifier un projet archivé et un workspace en lecture seule :
  consultation possible mais aucune édition ni association implicite.
- Naviguer au clavier dans les sous-menus imbriqués, contrôler les
  intitulés, le thème sombre et les longues listes d'opérations.

Aucune capture d'écran ni nom réel de workspace ne doit être
ajouté aux commits ou aux jeux de test.
